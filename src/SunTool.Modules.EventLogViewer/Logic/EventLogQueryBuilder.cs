using System.Globalization;

namespace SunTool.Modules.EventLogViewer.Logic;

/// <summary>
/// 이벤트 뷰어의 '현재 로그 필터링'과 같은 XPath 쿼리를 만듭니다.
/// 예: *[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime&gt;='…' and @SystemTime&lt;='…']]]
/// </summary>
public static class EventLogQueryBuilder
{
    public static string Build(EventLevels levels, DateTime fromLocal, DateTime toLocal)
    {
        var conditions = new List<string>();

        var levelTerms = new List<string>();
        if (levels.HasFlag(EventLevels.Critical))
        {
            levelTerms.Add("Level=1");
        }

        if (levels.HasFlag(EventLevels.Error))
        {
            levelTerms.Add("Level=2");
        }

        if (levels.HasFlag(EventLevels.Warning))
        {
            levelTerms.Add("Level=3");
        }

        if (levels.HasFlag(EventLevels.Information))
        {
            levelTerms.Add("Level=0"); // LogAlways — 이벤트 뷰어는 '정보'로 표시
            levelTerms.Add("Level=4");
        }

        if (levels.HasFlag(EventLevels.Verbose))
        {
            levelTerms.Add("Level=5");
        }

        if (levelTerms.Count > 0)
        {
            conditions.Add("(" + string.Join(" or ", levelTerms) + ")");
        }

        conditions.Add($"TimeCreated[@SystemTime>='{ToUtcText(fromLocal)}' and @SystemTime<='{ToUtcText(toLocal)}']");
        return $"*[System[{string.Join(" and ", conditions)}]]";
    }

    /// <summary>날짜만 고른 경우: 시작일 00:00:00 ~ 종료일 23:59:59.999</summary>
    public static (DateTime From, DateTime To) DayRange(DateTime fromDate, DateTime toDate) =>
        (fromDate.Date, toDate.Date.AddDays(1).AddMilliseconds(-1));

    private static string ToUtcText(DateTime local) =>
        local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
