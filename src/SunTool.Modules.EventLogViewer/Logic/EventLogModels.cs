namespace SunTool.Modules.EventLogViewer.Logic;

/// <summary>조회할 이벤트 수준 (여러 개 조합 가능).</summary>
[Flags]
public enum EventLevels
{
    None = 0,
    Critical = 1,
    Error = 2,
    Warning = 4,
    Information = 8,
    Verbose = 16,
}

/// <summary>화면 표시용 수준 분류.</summary>
public enum EventLevelKind
{
    Critical,
    Error,
    Warning,
    Information,
    Verbose,
}

public static class EventLevelInfo
{
    /// <summary>Windows 이벤트의 Level 값(0~5)을 분류합니다. 0(LogAlways)은 이벤트 뷰어처럼 '정보'로 봅니다.</summary>
    public static EventLevelKind FromLevel(byte level) => level switch
    {
        1 => EventLevelKind.Critical,
        2 => EventLevelKind.Error,
        3 => EventLevelKind.Warning,
        5 => EventLevelKind.Verbose,
        _ => EventLevelKind.Information,
    };

    public static string ToKorean(EventLevelKind kind) => kind switch
    {
        EventLevelKind.Critical => "위험",
        EventLevelKind.Error => "오류",
        EventLevelKind.Warning => "경고",
        EventLevelKind.Verbose => "자세한 정보",
        _ => "정보",
    };
}

/// <summary>이벤트 한 건 (UI 와 무관한 스냅샷).</summary>
public sealed class EventLogItem
{
    public required byte Level { get; init; }

    public required DateTime TimeCreated { get; init; }

    /// <summary>원래 로그 이름 (System, Application …).</summary>
    public required string LogName { get; init; }

    /// <summary>화면에 보여줄 로그 이름 (시스템, 응용 프로그램 …).</summary>
    public required string LogDisplayName { get; init; }

    public required string ProviderName { get; init; }

    public required int EventId { get; init; }

    public string TaskCategory { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string MachineName { get; init; } = string.Empty;

    public string? UserId { get; init; }

    public long? RecordId { get; init; }

    public EventLevelKind LevelKind => EventLevelInfo.FromLevel(Level);

    public string LevelText => EventLevelInfo.ToKorean(LevelKind);

    public string TimeText => TimeCreated.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>메시지를 한 줄로 합친 요약 (목록 표시용).</summary>
    public string Summary
    {
        get
        {
            var line = string.Join(' ', Message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return line.Length > 200 ? line[..200] + "…" : line;
        }
    }

    /// <summary>상세 창에 보여줄 전체 내용.</summary>
    public string Details =>
        $"수준: {LevelText}    날짜 및 시간: {TimeText}    로그: {LogDisplayName}\r\n" +
        $"원본: {ProviderName}    이벤트 ID: {EventId}    작업 범주: {(string.IsNullOrEmpty(TaskCategory) ? "없음" : TaskCategory)}\r\n" +
        $"컴퓨터: {MachineName}" + (UserId is null ? string.Empty : $"    사용자: {UserId}") + "\r\n" +
        new string('─', 60) + "\r\n" +
        Message.Replace("\r\n", "\n").Replace("\n", "\r\n");
}

public sealed class EventLogSearchRequest
{
    public required IReadOnlyList<string> LogNames { get; init; }

    public required EventLevels Levels { get; init; }

    /// <summary>시작 시각 (로컬 시간, 포함).</summary>
    public required DateTime From { get; init; }

    /// <summary>끝 시각 (로컬 시간, 포함).</summary>
    public required DateTime To { get; init; }

    /// <summary>최대 결과 수 (최신순으로 자름).</summary>
    public int MaxResults { get; init; } = 3000;
}

public sealed record EventLogSearchResult(
    IReadOnlyList<EventLogItem> Items,
    IReadOnlyList<string> Warnings,
    bool Truncated,
    bool Cancelled,
    TimeSpan Elapsed);
