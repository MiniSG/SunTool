namespace SunTool.Core.Logging;

/// <summary>로그 한 줄. 불변 객체라 여러 스레드에서 안전하게 공유할 수 있습니다.</summary>
public sealed record LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Source,
    string Message,
    string? Exception,
    int ThreadId)
{
    public string LevelTag => Level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "???",
    };

    public string TimeText => Timestamp.ToString("HH:mm:ss.fff");

    /// <summary>메시지와 예외를 합친 본문 (터미널 표시용).</summary>
    public string FullText => Exception is null ? Message : $"{Message}{Environment.NewLine}{Exception}";

    public override string ToString() =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{LevelTag}] [{Source}] (T{ThreadId}) {FullText}";
}
