using System.Net;
using System.Net.Sockets;

namespace SunTool.Modules.RemoteView.Logic;

/// <summary>
/// 호스트: 내 화면을 공유합니다. 지정한 주소·포트로 TCP 리슨하고, PIN 이 맞는 뷰어에게만
/// 화면을 JPEG 로 주기 전송합니다. 공유는 사용자가 직접 켜야만 시작됩니다.
/// </summary>
public sealed class RemoteHost : IDisposable
{
    private readonly string _pin;
    private readonly int _intervalMs;
    private readonly ScreenSource _source;
    private readonly List<TcpClient> _viewers = [];
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public RemoteHost(string pin, int quality = 60, int maxWidth = 1920, int fps = 5)
    {
        _pin = pin;
        _intervalMs = Math.Clamp(1000 / Math.Clamp(fps, 1, 30), 33, 1000);
        _source = new ScreenSource(quality, maxWidth);
    }

    /// <summary>뷰어 수가 바뀌면 (스레드 풀 스레드에서) 호출됩니다.</summary>
    public event Action<int>? ViewerCountChanged;

    /// <summary>문제가 생기면 호출됩니다.</summary>
    public event Action<string>? ErrorOccurred;

    public int ViewerCount
    {
        get { lock (_gate) { return _viewers.Count; } }
    }

    public void Start(IPAddress bind, int port)
    {
        _listener = new TcpListener(bind, port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _ = Task.Run(() => CaptureLoopAsync(_cts.Token));
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
                || version != RemoteProtocol.Version || !Pin.Matches(_pin, pin))
            {
                TrySend(stream, RemoteProtocol.EncodeHelloError());
                client.Close();
                return;
            }

            var layout = _source.GetLayout();
            var monitors = layout.Monitors.Select(m => (m.X, m.Y, m.Width, m.Height)).ToList();
            stream.ReadTimeout = Timeout.Infinite;
            RemoteProtocol.WriteMessage(stream, RemoteProtocol.EncodeHelloOk(layout.Width, layout.Height, monitors));

            lock (_gate)
            {
                _viewers.Add(client);
            }

            ViewerCountChanged?.Invoke(ViewerCount);
        }
        catch (Exception)
        {
            try { client.Close(); } catch { /* 무시 */ }
        }
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient[] snapshot;
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
                        if (!TrySend(viewer.GetStreamOrNull(), frame))
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

    private void Drop(TcpClient viewer)
    {
        bool changed;
        lock (_gate)
        {
            changed = _viewers.Remove(viewer);
        }

        try { viewer.Close(); } catch { /* 무시 */ }
        if (changed)
        {
            ViewerCountChanged?.Invoke(ViewerCount);
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
                try { v.Close(); } catch { /* 무시 */ }
            }

            _viewers.Clear();
        }

        _cts?.Dispose();
        _source.Dispose();
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
