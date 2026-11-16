using System.Net.Sockets;

namespace SunTool.Modules.RemoteView.Logic;

/// <summary>
/// 뷰어: 호스트에 접속해 PIN 으로 인증하고, 받은 JPEG 프레임을 흘려 줍니다.
/// 호스트가 제어를 허용하면(<see cref="ControlGranted"/>) 마우스·키보드 입력을 호스트로 보낼 수 있습니다.
/// </summary>
public sealed class RemoteViewerClient : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly object _sendGate = new();
    private CancellationTokenSource? _cts;

    /// <summary>접속 성공. 화면 크기와 모니터 영역 목록(보낼 이미지 좌표)을 넘깁니다.</summary>
    public event Action<int, int, IReadOnlyList<(int X, int Y, int W, int H)>>? Connected;

    /// <summary>프레임(JPEG 바이트)을 받을 때마다.</summary>
    public event Action<byte[]>? FrameReceived;

    /// <summary>호스트가 제어 허용 여부를 알려 올 때.</summary>
    public event Action<bool>? ControlGranted;

    /// <summary>연결이 끝났을 때. 사유 문자열.</summary>
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(string host, int port, string pin, CancellationToken token)
    {
        var client = new TcpClient { NoDelay = true };
        _client = client;
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(8));
            await client.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke(ex is OperationCanceledException ? Loc.T("연결 시간이 초과되었습니다.") : ex.Message);
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _ = Task.Run(() => ReceiveLoop(client, pin, _cts.Token));
    }

    private void ReceiveLoop(TcpClient client, string pin, CancellationToken token)
    {
        try
        {
            var stream = client.GetStream();
            _stream = stream;
            RemoteProtocol.WriteMessage(stream, RemoteProtocol.EncodeHello(pin));

            var resp = RemoteProtocol.ReadMessage(stream);
            if (resp is null || RemoteProtocol.TypeOf(resp) == RemoteProtocol.MsgType.HelloError)
            {
                Disconnected?.Invoke(Loc.T("호스트가 연결을 거부했습니다. PIN 을 확인하세요."));
                return;
            }

            if (!RemoteProtocol.TryDecodeHelloOk(resp, out var w, out var h, out var monitors))
            {
                Disconnected?.Invoke(Loc.T("호스트 응답을 이해할 수 없습니다."));
                return;
            }

            Connected?.Invoke(w, h, monitors);

            while (!token.IsCancellationRequested)
            {
                var msg = RemoteProtocol.ReadMessage(stream);
                if (msg is null)
                {
                    break;
                }

                if (RemoteProtocol.TryDecodeFrame(msg, out var jpeg))
                {
                    FrameReceived?.Invoke(jpeg);
                }
                else if (RemoteProtocol.TryDecodeControlGrant(msg, out var granted))
                {
                    ControlGranted?.Invoke(granted);
                }
            }

            Disconnected?.Invoke(Loc.T("연결이 끊겼습니다."));
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                Disconnected?.Invoke(ex.Message);
            }
            else
            {
                Disconnected?.Invoke(Loc.T("연결을 끊었습니다."));
            }
        }
    }

    /// <summary>마우스 입력을 호스트로 보냅니다. 좌표는 전송 이미지 기준 0~1. 허용 전·연결 끊김이면 조용히 무시.</summary>
    public void SendMouse(RemoteProtocol.MouseAction action, float nx, float ny, int data = 0)
        => TrySendInput(RemoteProtocol.EncodeInputMouse(action, nx, ny, data));

    /// <summary>키보드 입력을 호스트로 보냅니다.</summary>
    public void SendKey(ushort vk, bool down, ushort modifiers = 0)
        => TrySendInput(RemoteProtocol.EncodeInputKey(vk, down, modifiers));

    private void TrySendInput(byte[] payload)
    {
        var stream = _stream;
        if (stream is null)
        {
            return;
        }

        try
        {
            lock (_sendGate)
            {
                RemoteProtocol.WriteMessage(stream, payload);
            }
        }
        catch (Exception)
        {
            // 연결이 끊기는 중일 수 있습니다. 수신 루프가 정리합니다.
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _client?.Close(); } catch { /* 무시 */ }
        _cts?.Dispose();
    }
}
