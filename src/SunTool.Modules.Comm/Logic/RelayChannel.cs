using System.Threading.Channels;

namespace SunTool.Modules.Comm.Logic;

/// <summary>
/// Steal(중계) 모드: 두 연결(보통 COM 포트 2개) 사이에 끼어, 한쪽에서 받은 데이터를 반대쪽으로 그대로 전달하면서 기록합니다.
/// listenOnly 면 전달하지 않고 기록만 합니다 (Y 케이블로 선로를 엿볼 때).
/// 전달은 방향마다 순서를 지키는 큐로 처리해서, 화면 갱신과 상관없이 바로 넘어갑니다.
/// </summary>
public sealed class RelayChannel : CommChannelBase
{
    private readonly ICommChannel _side1;
    private readonly ICommChannel _side2;
    private readonly bool _listenOnly;
    private readonly Channel<byte[]> _to2 = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<byte[]> _to1 = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private bool _open;

    public RelayChannel(ICommChannel side1, string name1, ICommChannel side2, string name2, bool listenOnly)
    {
        _side1 = side1;
        _side2 = side2;
        Name1 = name1;
        Name2 = name2;
        _listenOnly = listenOnly;
    }

    /// <summary>장비 1 쪽 이름 (예: COM3).</summary>
    public string Name1 { get; }

    /// <summary>장비 2 쪽 이름 (예: COM4).</summary>
    public string Name2 { get; }

    public bool ListenOnly => _listenOnly;

    public override string Title => _listenOnly
        ? Loc.F("Steal (듣기만) · {0} / {1}", _side1.Title, _side2.Title)
        : Loc.F("Steal (중계) · {0} ⇄ {1}", _side1.Title, _side2.Title);

    public override bool IsOpen => _open;

    /// <summary>
    /// 중계 데이터: (보낸 쪽 1 또는 2, 바이트). 1 = 장비 1 → 장비 2, 2 = 장비 2 → 장비 1.
    /// </summary>
    public event Action<int, byte[]>? Relayed;

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        _side1.Received += OnSide1Received;
        _side2.Received += OnSide2Received;
        _side1.Notice += (m, e) => RaiseNotice($"{Name1}: {m}", e);
        _side2.Notice += (m, e) => RaiseNotice($"{Name2}: {m}", e);
        _side1.Closed += r => RaiseClosed($"{Name1}: {r}");
        _side2.Closed += r => RaiseClosed($"{Name2}: {r}");

        await _side1.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _side2.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 한쪽만 열린 채로 두지 않음
            _side1.Dispose();
            throw;
        }

        _open = true;
        if (!_listenOnly)
        {
            _ = Task.Run(() => PumpAsync(_to2.Reader, _side2, Name2, _cts.Token));
            _ = Task.Run(() => PumpAsync(_to1.Reader, _side1, Name1, _cts.Token));
        }
    }

    private void OnSide1Received(string peer, byte[] data)
    {
        if (!_listenOnly)
        {
            _to2.Writer.TryWrite(data);
        }

        Relayed?.Invoke(1, data);
    }

    private void OnSide2Received(string peer, byte[] data)
    {
        if (!_listenOnly)
        {
            _to1.Writer.TryWrite(data);
        }

        Relayed?.Invoke(2, data);
    }

    private async Task PumpAsync(ChannelReader<byte[]> reader, ICommChannel target, string targetName, CancellationToken token)
    {
        try
        {
            await foreach (var data in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await target.SendAsync(data, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RaiseNotice(Loc.F("{0} 쪽으로 전달하지 못했습니다: {1}", targetName, ex.Message), isError: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>중계 중에 한쪽으로 직접 보내기 (시험용 끼워 넣기). side: 1 = 장비 1 쪽, 2 = 장비 2 쪽.</summary>
    public async Task<string> SendToAsync(int side, byte[] data, CancellationToken cancellationToken)
    {
        var target = side == 1 ? _side1 : _side2;
        await target.SendAsync(data, cancellationToken).ConfigureAwait(false);
        return side == 1 ? Name1 : Name2;
    }

    /// <summary>일반 보내기는 장비 1 쪽으로.</summary>
    public override Task<string> SendAsync(byte[] data, CancellationToken cancellationToken) => SendToAsync(1, data, cancellationToken);

    protected override void CloseCore()
    {
        _open = false;
        _cts.Cancel();
        _to1.Writer.TryComplete();
        _to2.Writer.TryComplete();
        _side1.Received -= OnSide1Received;
        _side2.Received -= OnSide2Received;
        _side1.Dispose();
        _side2.Dispose();
        _cts.Dispose();
    }
}
