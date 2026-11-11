namespace SunTool.Modules.Comm.Logic;

/// <summary>받은 데이터를 한 줄(프레임)로 나누는 방식.</summary>
public enum FrameMode
{
    /// <summary>시간 간격: 이어서 들어온 조각을 GapMs 안이면 한 줄로.</summary>
    Gap,

    /// <summary>끝 문자: 끝 바이트(예: 0D 0A, 03)가 나올 때마다 한 줄.</summary>
    EndMarker,

    /// <summary>시작·끝 문자: 시작 바이트(예: 02)부터 끝 바이트(예: 03)까지 한 줄.</summary>
    StartEnd,

    /// <summary>고정 길이: N 바이트마다 한 줄.</summary>
    FixedLength,
}

/// <summary>프레임 나누기 설정. Start/End 는 바이트 배열, Length 는 고정 길이.</summary>
public sealed record FrameSettings(FrameMode Mode, int GapMs, byte[] Start, byte[] End, int Length)
{
    public static FrameSettings Default { get; } = new(FrameMode.Gap, 20, [], [], 0);

    /// <summary>시간 간격이 아닌 방식에서, 끝이 안 온 데이터를 이 시간(ms) 뒤에 그대로 내보냄.</summary>
    public int PartialTimeoutMs => Math.Max(GapMs, 200);

    /// <summary>설정이 그 방식에 쓸 수 있는지 (아니면 시간 간격으로 동작).</summary>
    public FrameSettings Effective => Mode switch
    {
        FrameMode.EndMarker when End.Length == 0 => this with { Mode = FrameMode.Gap },
        FrameMode.StartEnd when Start.Length == 0 || End.Length == 0 => this with { Mode = FrameMode.Gap },
        FrameMode.FixedLength when Length <= 0 => this with { Mode = FrameMode.Gap },
        _ => this,
    };
}

public static class Framer
{
    /// <summary>
    /// 버퍼에서 완성된 프레임을 잘라 냅니다. 남은(아직 끝나지 않은) 바이트는 buffer 에 그대로 둡니다.
    /// 시작·끝 방식에서 시작 바이트 앞의 바이트는 따로 한 줄로 내보냅니다 (버리지 않음).
    /// </summary>
    public static List<byte[]> Split(List<byte> buffer, FrameSettings settings)
    {
        var frames = new List<byte[]>();
        switch (settings.Mode)
        {
            case FrameMode.EndMarker:
                while (IndexOf(buffer, settings.End, 0) is var end and >= 0)
                {
                    Take(buffer, end + settings.End.Length, frames);
                }

                break;

            case FrameMode.StartEnd:
                while (buffer.Count > 0)
                {
                    var start = IndexOf(buffer, settings.Start, 0);
                    if (start < 0)
                    {
                        // 시작 바이트가 아직 없음: 그대로 두고, 시작이 오면 앞부분을 한 줄로 / 안 오면 시간 지나 그대로 내보냄
                        break;
                    }

                    if (start > 0)
                    {
                        Take(buffer, start, frames); // 시작 앞 쓰레기 바이트
                        continue;
                    }

                    var end = IndexOf(buffer, settings.End, settings.Start.Length);
                    if (end < 0)
                    {
                        break;
                    }

                    Take(buffer, end + settings.End.Length, frames);
                }

                break;

            case FrameMode.FixedLength:
                while (buffer.Count >= settings.Length)
                {
                    Take(buffer, settings.Length, frames);
                }

                break;
        }

        return frames;
    }

    private static void Take(List<byte> buffer, int count, List<byte[]> frames)
    {
        frames.Add([.. buffer.GetRange(0, count)]);
        buffer.RemoveRange(0, count);
    }

    private static int IndexOf(List<byte> buffer, byte[] pattern, int from)
    {
        for (var i = from; i + pattern.Length <= buffer.Count; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// 받은 조각을 상대별로 모아 프레임 단위로 내보냅니다.
/// 시간 간격 방식: GapMs 안에 이어진 조각을 한 줄로. 그 밖의 방식: 완성된 프레임은 바로, 끝이 안 온 나머지는 일정 시간 뒤 그대로.
/// </summary>
public sealed class ReceiveAssembler : IDisposable
{
    private const int MaxChunk = 64 * 1024;
    private readonly Action<string, byte[], DateTime> _flush;
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Timer _timer;
    private FrameSettings _settings = FrameSettings.Default;

    public ReceiveAssembler(Action<string, byte[], DateTime> flush)
    {
        _flush = flush;
        _timer = new Timer(_ => FlushDue(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>시간 간격(ms). 시간 간격 방식에서 0 이면 받은 즉시 한 줄씩.</summary>
    public int GapMs
    {
        get => _settings.GapMs;
        set => Settings = _settings with { GapMs = value };
    }

    public FrameSettings Settings
    {
        get => _settings;
        set
        {
            FlushAll(); // 방식이 바뀌면 모아 둔 것은 그대로 내보냄
            _settings = value;
        }
    }

    private int WaitMs => _settings.Effective.Mode == FrameMode.Gap ? _settings.GapMs : _settings.PartialTimeoutMs;

    public void Add(string peer, byte[] data, DateTime now)
    {
        var settings = _settings.Effective;
        if (settings.Mode == FrameMode.Gap && settings.GapMs <= 0)
        {
            _flush(peer, data, now);
            return;
        }

        var ready = new List<(byte[] Data, DateTime Time)>();
        lock (_gate)
        {
            if (!_pending.TryGetValue(peer, out var pending))
            {
                pending = new Pending(now);
                _pending[peer] = pending;
            }

            pending.Buffer.AddRange(data);
            pending.Last = now;

            if (settings.Mode != FrameMode.Gap)
            {
                foreach (var frame in Framer.Split(pending.Buffer, settings))
                {
                    ready.Add((frame, pending.First));
                    pending.First = now; // 다음 프레임 시각은 이번 조각부터
                }
            }

            if (pending.Buffer.Count >= MaxChunk)
            {
                ready.Add(([.. pending.Buffer], pending.First));
                pending.Buffer.Clear();
            }

            if (pending.Buffer.Count == 0)
            {
                _pending.Remove(peer);
            }

            if (_pending.Count > 0)
            {
                _timer.Change(WaitMs, Timeout.Infinite);
            }
        }

        foreach (var (frame, time) in ready)
        {
            _flush(peer, frame, time);
        }
    }

    /// <summary>남은 조각을 모두 내보냅니다 (연결 끊을 때, 방식 바꿀 때).</summary>
    public void FlushAll() => FlushWhere(_ => true);

    private void FlushDue()
    {
        var now = DateTime.Now;
        var wait = WaitMs;
        FlushWhere(p => (now - p.Last).TotalMilliseconds >= wait - 1);
        lock (_gate)
        {
            if (_pending.Count > 0)
            {
                _timer.Change(wait, Timeout.Infinite);
            }
        }
    }

    private void FlushWhere(Func<Pending, bool> due)
    {
        List<(string Peer, byte[] Data, DateTime Time)> ready = [];
        lock (_gate)
        {
            foreach (var (peer, pending) in _pending.Where(kv => due(kv.Value)).ToList())
            {
                if (pending.Buffer.Count > 0)
                {
                    ready.Add((peer, [.. pending.Buffer], pending.First));
                }

                _pending.Remove(peer);
            }
        }

        foreach (var (peer, data, time) in ready)
        {
            _flush(peer, data, time);
        }
    }

    public void Dispose() => _timer.Dispose();

    private sealed class Pending(DateTime first)
    {
        public List<byte> Buffer { get; } = [];

        public DateTime First { get; set; } = first;

        public DateTime Last { get; set; } = first;
    }
}
