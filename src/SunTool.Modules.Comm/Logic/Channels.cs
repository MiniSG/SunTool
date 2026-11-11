using System.Collections.Concurrent;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace SunTool.Modules.Comm.Logic;

/// <summary>
/// 통신 연결 하나 (시리얼 / TCP 서버 / TCP 클라이언트 / UDP).
/// 이벤트는 백그라운드 스레드에서 발생하므로 화면 갱신은 호출하는 쪽에서 UI 스레드로 넘깁니다.
/// </summary>
public interface ICommChannel : IDisposable
{
    /// <summary>연결 설명 (예: "COM3 · 9600 8N1", "TCP 서버 · 0.0.0.0:5000").</summary>
    string Title { get; }

    bool IsOpen { get; }

    /// <summary>데이터 받음: (상대 이름, 바이트). 상대 이름은 COM 포트 이름 또는 "IP:포트".</summary>
    event Action<string, byte[]>? Received;

    /// <summary>상태 알림: (메시지, 오류 여부). 예: 클라이언트 접속/끊김, 시리얼 오류.</summary>
    event Action<string, bool>? Notice;

    /// <summary>연결이 스스로 끊김 (상대가 끊음, 장치 분리 등). 사용자가 닫을 때는 발생하지 않습니다.</summary>
    event Action<string>? Closed;

    Task OpenAsync(CancellationToken cancellationToken);

    /// <summary>보냄. 받은 상대 이름(들)을 돌려줍니다 (TCP 서버는 접속한 모든 클라이언트).</summary>
    Task<string> SendAsync(byte[] data, CancellationToken cancellationToken);
}

public abstract class CommChannelBase : ICommChannel
{
    private int _closed;

    public abstract string Title { get; }

    public abstract bool IsOpen { get; }

    public event Action<string, byte[]>? Received;

    public event Action<string, bool>? Notice;

    public event Action<string>? Closed;

    public abstract Task OpenAsync(CancellationToken cancellationToken);

    public abstract Task<string> SendAsync(byte[] data, CancellationToken cancellationToken);

    protected bool IsDisposed => Volatile.Read(ref _closed) != 0;

    protected void RaiseReceived(string peer, byte[] data) => Received?.Invoke(peer, data);

    protected void RaiseNotice(string message, bool isError = false) => Notice?.Invoke(message, isError);

    /// <summary>연결이 스스로 끊겼을 때 한 번만 알리고 자원을 정리합니다.</summary>
    protected void RaiseClosed(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        CloseCore();
        Closed?.Invoke(reason);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        CloseCore();
        GC.SuppressFinalize(this);
    }

    protected abstract void CloseCore();
}

// ===================================================================== 시리얼

public sealed record SerialSettings(
    string PortName,
    int BaudRate = 9600,
    int DataBits = 8,
    Parity Parity = Parity.None,
    StopBits StopBits = StopBits.One,
    Handshake Handshake = Handshake.None)
{
    /// <summary>"9600 8N1" 형식.</summary>
    public string ShortText =>
        $"{BaudRate} {DataBits}{Parity switch { Parity.None => 'N', Parity.Odd => 'O', Parity.Even => 'E', Parity.Mark => 'M', _ => 'S' }}{StopBits switch { StopBits.OnePointFive => "1.5", StopBits.Two => "2", _ => "1" }}";
}

public sealed class SerialChannel(SerialSettings settings) : CommChannelBase
{
    private SerialPort? _port;

    public override string Title => $"{settings.PortName} · {settings.ShortText}";

    public override bool IsOpen => _port?.IsOpen == true;

    public override Task OpenAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
        {
            Handshake = settings.Handshake,
            ReadTimeout = 500,
            WriteTimeout = 2000,
            ReceivedBytesThreshold = 1,
        };

        port.DataReceived += OnDataReceived;
        port.ErrorReceived += (_, e) => RaiseNotice(Loc.F("시리얼 오류: {0}", e.EventType), isError: true);
        try
        {
            port.Open();
        }
        catch
        {
            port.Dispose();
            throw;
        }

        _port = port;
    }, cancellationToken);

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _port;
        if (port is null || IsDisposed)
        {
            return;
        }

        try
        {
            var count = port.BytesToRead;
            if (count <= 0)
            {
                return;
            }

            var buffer = new byte[count];
            var read = port.Read(buffer, 0, count);
            if (read > 0)
            {
                RaiseReceived(settings.PortName, read == count ? buffer : buffer[..read]);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            RaiseClosed(Loc.F("포트를 읽을 수 없습니다 (장치 분리?): {0}", ex.Message));
        }
        catch (TimeoutException)
        {
        }
    }

    public override Task<string> SendAsync(byte[] data, CancellationToken cancellationToken)
    {
        var port = _port ?? throw new InvalidOperationException(Loc.T("연결되어 있지 않습니다."));
        return Task.Run(() =>
        {
            port.Write(data, 0, data.Length);
            return settings.PortName;
        }, cancellationToken);
    }

    protected override void CloseCore()
    {
        var port = Interlocked.Exchange(ref _port, null);
        if (port is null)
        {
            return;
        }

        port.DataReceived -= OnDataReceived;
        try
        {
            // 장치가 이미 빠진 경우 Close 가 예외를 낼 수 있음
            port.Close();
        }
        catch
        {
        }

        port.Dispose();
    }
}

// ===================================================================== TCP 클라이언트

public sealed class TcpClientChannel(string host, int port, TimeSpan connectTimeout) : CommChannelBase
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly CancellationTokenSource _cts = new();

    public override string Title => Loc.F("TCP 클라이언트 · {0}:{1}", host, port);

    public override bool IsOpen => _client?.Connected == true;

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(connectTimeout);
        try
        {
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException(Loc.F("{0}초 안에 연결되지 않았습니다.", (int)connectTimeout.TotalSeconds));
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _stream = client.GetStream();
        var peer = client.Client.RemoteEndPoint?.ToString() ?? $"{host}:{port}";
        _ = Task.Run(() => ReadLoopAsync(_stream, peer, _cts.Token));
    }

    private async Task ReadLoopAsync(NetworkStream stream, string peer, CancellationToken token)
    {
        var buffer = new byte[8192];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0)
                {
                    RaiseClosed(Loc.T("상대가 연결을 끊었습니다."));
                    return;
                }

                RaiseReceived(peer, buffer[..read]);
            }
        }
        catch (Exception) when (token.IsCancellationRequested || IsDisposed)
        {
        }
        catch (Exception ex)
        {
            RaiseClosed(Loc.F("연결이 끊겼습니다: {0}", ex.Message));
        }
    }

    public override async Task<string> SendAsync(byte[] data, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException(Loc.T("연결되어 있지 않습니다."));
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        return _client?.Client.RemoteEndPoint?.ToString() ?? $"{host}:{port}";
    }

    protected override void CloseCore()
    {
        _cts.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _cts.Dispose();
    }
}

// ===================================================================== TCP 서버

public sealed class TcpServerChannel(IPAddress address, int port) : CommChannelBase
{
    private TcpListener? _listener;
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private readonly CancellationTokenSource _cts = new();

    public override string Title => Loc.F("TCP 서버 · {0}:{1}", address, port);

    public override bool IsOpen => _listener is not null;

    public int ClientCount => _clients.Count;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        _listener = listener;
        _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                client.NoDelay = true;
                var peer = client.Client.RemoteEndPoint?.ToString() ?? "?";
                _clients[peer] = client;
                RaiseNotice(Loc.F("클라이언트 접속: {0} (현재 {1}개)", peer, _clients.Count));
                _ = Task.Run(() => ClientLoopAsync(client, peer, token));
            }
        }
        catch (Exception) when (token.IsCancellationRequested || IsDisposed)
        {
        }
        catch (Exception ex)
        {
            RaiseClosed(Loc.F("서버가 멈췄습니다: {0}", ex.Message));
        }
    }

    private async Task ClientLoopAsync(TcpClient client, string peer, CancellationToken token)
    {
        var buffer = new byte[8192];
        var reason = Loc.T("상대가 끊음");
        try
        {
            var stream = client.GetStream();
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                RaiseReceived(peer, buffer[..read]);
            }
        }
        catch (Exception) when (token.IsCancellationRequested || IsDisposed)
        {
            return;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
        }

        if (_clients.TryRemove(peer, out _))
        {
            client.Dispose();
            RaiseNotice(Loc.F("클라이언트 끊김: {0} ({1}, 현재 {2}개)", peer, reason, _clients.Count));
        }
    }

    public override async Task<string> SendAsync(byte[] data, CancellationToken cancellationToken)
    {
        var targets = _clients.ToArray();
        if (targets.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("접속한 클라이언트가 없습니다."));
        }

        foreach (var (_, client) in targets)
        {
            await client.GetStream().WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        return targets.Length == 1 ? targets[0].Key : Loc.F("클라이언트 {0}개", targets.Length);
    }

    protected override void CloseCore()
    {
        _cts.Cancel();
        _listener?.Stop();
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
        _cts.Dispose();
    }
}

// ===================================================================== UDP

/// <summary>UDP: 내 포트로 받고, 대상(비어 있으면 마지막으로 보낸 상대)으로 보냅니다.</summary>
public sealed class UdpChannel(int localPort, string? remoteHost, int remotePort) : CommChannelBase
{
    private UdpClient? _udp;
    private IPEndPoint? _target;
    private IPEndPoint? _lastSender;
    private readonly CancellationTokenSource _cts = new();

    public override string Title => string.IsNullOrWhiteSpace(remoteHost)
        ? Loc.F("UDP · 받기 :{0}", LocalPort)
        : Loc.F("UDP · 받기 :{0} → 보내기 {1}:{2}", LocalPort, remoteHost, remotePort);

    public int LocalPort => (_udp?.Client.LocalEndPoint as IPEndPoint)?.Port ?? localPort;

    public override bool IsOpen => _udp is not null;

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(remoteHost))
        {
            var address = IPAddress.TryParse(remoteHost, out var ip)
                ? ip
                : (await Dns.GetHostAddressesAsync(remoteHost, cancellationToken).ConfigureAwait(false))
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                  ?? throw new InvalidOperationException(Loc.F("주소를 찾을 수 없습니다: {0}", remoteHost));
            _target = new IPEndPoint(address, remotePort);
        }

        var udp = new UdpClient(new IPEndPoint(IPAddress.Any, localPort)) { EnableBroadcast = true };
        // 상대 포트가 닫혀 있을 때 Windows 가 다음 Receive 를 실패시키는 동작(ICMP port unreachable) 끄기
        const int SIO_UDP_CONNRESET = -1744830452;
        try
        {
            udp.Client.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
        }
        catch
        {
        }

        _udp = udp;
        _ = Task.Run(() => ReceiveLoopAsync(udp, _cts.Token));
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(token).ConfigureAwait(false);
                _lastSender = result.RemoteEndPoint;
                RaiseReceived(result.RemoteEndPoint.ToString(), result.Buffer);
            }
        }
        catch (Exception) when (token.IsCancellationRequested || IsDisposed)
        {
        }
        catch (Exception ex)
        {
            RaiseClosed(Loc.F("UDP 받기가 멈췄습니다: {0}", ex.Message));
        }
    }

    public override async Task<string> SendAsync(byte[] data, CancellationToken cancellationToken)
    {
        var udp = _udp ?? throw new InvalidOperationException(Loc.T("연결되어 있지 않습니다."));
        var target = _target ?? _lastSender
            ?? throw new InvalidOperationException(Loc.T("보낼 대상이 없습니다. 대상 IP·포트를 넣거나, 상대에게서 먼저 받으세요."));
        await udp.SendAsync(data, target, cancellationToken).ConfigureAwait(false);
        return target.ToString();
    }

    protected override void CloseCore()
    {
        _cts.Cancel();
        _udp?.Dispose();
        _cts.Dispose();
    }
}
