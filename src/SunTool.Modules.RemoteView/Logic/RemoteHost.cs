using System.Net;
using System.Net.Sockets;

namespace SunTool.Modules.RemoteView.Logic;

/// <summary>
/// 호스트: 내 화면을 공유합니다. 지정한 주소·포트로 TCP 리슨하고, PIN 이 맞는 뷰어에게만
/// 화면을 JPEG 로 주기 전송합니다. 공유는 사용자가 직접 켜야만 시작됩니다.
/// 호스트가 제어를 허용(<see cref="SetAllowControl"/>)하면, 버전 2 뷰어 중 먼저 입력을 보낸
/// 한 명(단독 제어권)의 마우스·키보드 입력을 OS 에 주입합니다. 허용을 끄면 즉시 중단합니다.
/// </summary>
public sealed class RemoteHost : IDisposable
{
    private readonly string _pin;
    private readonly int _intervalMs;
    private readonly ScreenSource _source;
    private readonly List<Viewer> _viewers = [];
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private volatile bool _allowControl;
    private Viewer? _controller;

    public RemoteHost(string pin, int quality = 60, int maxWidth = 1920, int fps = 5)
    {
        _pin = pin;
        _intervalMs = Math.Clamp(1000 / Math.Clamp(fps, 1, 30), 33, 1000);
        _source = new ScreenSource(quality, maxWidth);
    }

    /// <summary>뷰어 수가 바뀌면 (스레드 풀 스레드에서) 호출됩니다.</summary>
    public event Action<int>? ViewerCountChanged;

    /// <summary>제어하는 뷰어가 바뀌면 그 주소(없으면 null)를 넘깁니다.</summary>
    public event Action<string?>? ControllerChanged;

    /// <summary>문제가 생기면 호출됩니다.</summary>
    public event Action<string>? ErrorOccurred;

    public int ViewerCount
    {
        get { lock (_gate) { return _viewers.Count; } }
    }

    /// <summary>지금 누군가 제어 중이면 그 주소, 아니면 null.</summary>
    public string? ControllerAddress
    {
        get { lock (_gate) { return _controller?.Address; } }
    }

    public void Start(IPAddress bind, int port)
    {
        _listener = new TcpListener(bind, port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _ = Task.Run(() => CaptureLoopAsync(_cts.Token));
    }

    /// <summary>원격 제어 허용 여부를 바꿉니다. 끄면 제어권을 즉시 반납합니다.</summary>
    public void SetAllowControl(bool allow)
    {
        _allowControl = allow;
        Viewer[] snapshot;
        lock (_gate)
        {
            if (!allow)
            {
                _controller = null;
            }

            snapshot = _viewers.ToArray();
        }

        foreach (var v in snapshot)
        {
            if (v.Version >= 2)
            {
                TrySend(v, RemoteProtocol.EncodeControlGrant(allow));
            }
        }

        if (!allow)
        {
            ControllerChanged?.Invoke(null);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        var listener = _listener!;
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke(ex.Message);
                }

                return;
            }

            _ = Task.Run(() => HandshakeAsync(client, token));
        }
    }

    private void HandshakeAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            stream.ReadTimeout = 10000;
            var hello = RemoteProtocol.ReadMessage(stream);
            if (hello is null || !RemoteProtocol.TryDecodeHello(hello, out var version, out var pin)
                || version < 1 || version > RemoteProtocol.Version || !Pin.Matches(_pin, pin))
            {
                TrySend(stream, RemoteProtocol.EncodeHelloError());
                client.Close();
                return;
            }

            var layout = _source.GetLayout();
            var monitors = layout.Monitors.Select(m => (m.X, m.Y, m.Width, m.Height)).ToList();
            stream.ReadTimeout = Timeout.Infinite;
            RemoteProtocol.WriteMessage(stream, RemoteProtocol.EncodeHelloOk(layout.Width, layout.Height, monitors));

            var viewer = new Viewer(client, version);
            lock (_gate)
            {
                _viewers.Add(viewer);
            }

            ViewerCountChanged?.Invoke(ViewerCount);

            // 제어 지원(버전 2) 뷰어에게 현재 허용 상태를 알립니다.
            if (version >= 2)
            {
                TrySend(viewer, RemoteProtocol.EncodeControlGrant(_allowControl));
            }

            ReceiveLoop(viewer, token);
        }
        catch (Exception)
        {
            try { client.Close(); } catch { /* 무시 */ }
        }
    }

    /// <summary>뷰어가 보내는 입력 메시지를 읽습니다. 프레임 전송(쓰기)과 방향이 달라 동시 실행이 안전합니다.</summary>
    private void ReceiveLoop(Viewer viewer, CancellationToken token)
    {
        try
        {
            var stream = viewer.Client.GetStream();
            while (!token.IsCancellationRequested)
            {
                var msg = RemoteProtocol.ReadMessage(stream);
                if (msg is null)
                {
                    break;
                }

                HandleInput(viewer, msg);
            }
        }
        catch (Exception)
        {
            // 연결이 끊긴 것으로 보고 정리합니다.
        }
        finally
        {
            Drop(viewer);
        }
    }

    private void HandleInput(Viewer viewer, byte[] msg)
    {
        var type = RemoteProtocol.TypeOf(msg);
        if (type is not (RemoteProtocol.MsgType.InputMouse or RemoteProtocol.MsgType.InputKey))
        {
            return;
        }

        // 허용 상태가 아니거나 버전 2 미만이면 무시합니다.
        if (!_allowControl || viewer.Version < 2)
        {
            return;
        }

        // 단독 제어권: 먼저 입력을 보낸 뷰어가 잡고, 반납(끊김·허용 해제) 전까지 유지합니다.
        bool becameController = false;
        lock (_gate)
        {
            if (_controller is null)
            {
                _controller = viewer;
                becameController = true;
            }
            else if (_controller != viewer)
            {
                return; // 다른 뷰어가 제어 중
            }
        }

        if (becameController)
        {
            ControllerChanged?.Invoke(viewer.Address);
        }

        if (type == RemoteProtocol.MsgType.InputMouse
            && RemoteProtocol.TryDecodeInputMouse(msg, out var action, out var nx, out var ny, out var data))
        {
            InputInjector.Mouse(action, nx, ny, data);
        }
        else if (type == RemoteProtocol.MsgType.InputKey
                 && RemoteProtocol.TryDecodeInputKey(msg, out var vk, out var down, out _))
        {
            InputInjector.Key(vk, down);
        }
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Viewer[] snapshot;
            lock (_gate)
            {
                snapshot = _viewers.ToArray();
            }

            if (snapshot.Length > 0)
            {
                byte[] frame;
                try
                {
                    var (jpeg, _, _) = _source.Capture();
                    frame = RemoteProtocol.EncodeFrame(jpeg);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(ex.Message);
                    frame = [];
                }

                if (frame.Length > 0)
                {
                    foreach (var viewer in snapshot)
                    {
                        if (!TrySend(viewer, frame))
                        {
                            Drop(viewer);
                        }
                    }
                }
            }

            try
            {
                await Task.Delay(_intervalMs, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Drop(Viewer viewer)
    {
        bool changed;
        bool wasController = false;
        lock (_gate)
        {
            changed = _viewers.Remove(viewer);
            if (_controller == viewer)
            {
                _controller = null;
                wasController = true;
            }
        }

        try { viewer.Client.Close(); } catch { /* 무시 */ }
        if (wasController)
        {
            ControllerChanged?.Invoke(null);
        }

        if (changed)
        {
            ViewerCountChanged?.Invoke(ViewerCount);
        }
    }

    /// <summary>한 뷰어에게 보냅니다. 뷰어별 잠금으로 프레임·제어 알림 쓰기를 직렬화합니다.</summary>
    private static bool TrySend(Viewer viewer, byte[] payload)
    {
        lock (viewer.SendGate)
        {
            return TrySend(viewer.Client.GetStreamOrNull(), payload);
        }
    }

    private static bool TrySend(Stream? stream, byte[] payload)
    {
        if (stream is null)
        {
            return false;
        }

        try
        {
            RemoteProtocol.WriteMessage(stream, payload);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* 무시 */ }
        lock (_gate)
        {
            foreach (var v in _viewers)
            {
                try { v.Client.Close(); } catch { /* 무시 */ }
            }

            _viewers.Clear();
            _controller = null;
        }

        _cts?.Dispose();
        _source.Dispose();
    }

    /// <summary>접속한 뷰어 한 명.</summary>
    private sealed class Viewer(TcpClient client, int version)
    {
        public TcpClient Client { get; } = client;

        public int Version { get; } = version;

        /// <summary>이 뷰어로의 모든 쓰기를 직렬화하는 잠금.</summary>
        public object SendGate { get; } = new();

        public string Address => (Client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
    }
}

internal static class TcpClientExtensions
{
    /// <summary>닫힌 소켓이면 null (예외 대신).</summary>
    public static Stream? GetStreamOrNull(this TcpClient client)
    {
        try
        {
            return client.Connected ? client.GetStream() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
