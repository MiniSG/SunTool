using SunTool.Modules.EventLogViewer.Logic;

namespace SunTool.Tests;

public class EventLogViewerTests
{
    [Fact]
    public void QueryBuilder_IncludesSelectedLevelsAndUtcRange()
    {
        var from = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Local);
        var to = new DateTime(2026, 11, 9, 23, 59, 59, DateTimeKind.Local);

        var xpath = EventLogQueryBuilder.Build(EventLevels.Critical | EventLevels.Error | EventLevels.Warning, from, to);

        Assert.StartsWith("*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime>='", xpath);
        Assert.Contains(from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss"), xpath);
        Assert.Contains(to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss"), xpath);
        Assert.DoesNotContain("Level=4", xpath);
    }

    [Fact]
    public void QueryBuilder_InformationIncludesLevelZero()
    {
        var xpath = EventLogQueryBuilder.Build(EventLevels.Information, DateTime.Today, DateTime.Now);
        Assert.Contains("(Level=0 or Level=4)", xpath);
    }

    [Fact]
    public void DayRange_CoversWholeDays()
    {
        var (from, to) = EventLogQueryBuilder.DayRange(new DateTime(2026, 10, 9, 15, 30, 0), new DateTime(2026, 11, 9, 8, 0, 0));
        Assert.Equal(new DateTime(2026, 10, 9), from);
        Assert.Equal(new DateTime(2026, 11, 9, 23, 59, 59, 999), to);
    }

    [Theory]
    [InlineData((byte)1, "위험")]
    [InlineData((byte)2, "오류")]
    [InlineData((byte)3, "경고")]
    [InlineData((byte)4, "정보")]
    [InlineData((byte)0, "정보")]
    public void LevelInfo_MapsToKorean(byte level, string expected) =>
        Assert.Equal(expected, EventLevelInfo.ToKorean(EventLevelInfo.FromLevel(level)));

    [Fact]
    public void Csv_EscapesCommasQuotesAndNewlines()
    {
        Assert.Equal("plain", EventLogCsv.Escape("plain"));
        Assert.Equal("\"a,b\"", EventLogCsv.Escape("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", EventLogCsv.Escape("say \"hi\""));
        Assert.Equal("\"line1\nline2\"", EventLogCsv.Escape("line1\nline2"));
    }

    [Fact]
    public void Searcher_ReadsSystemLog_RespectingFilters()
    {
        // 실제 이 PC 의 시스템 로그를 읽습니다 (관리자 권한 불필요).
        var (from, to) = EventLogQueryBuilder.DayRange(DateTime.Today.AddMonths(-1), DateTime.Today);
        var request = new EventLogSearchRequest
        {
            LogNames = ["System"],
            Levels = EventLevels.Critical | EventLevels.Error | EventLevels.Warning,
            From = from,
            To = to,
            MaxResults = 200,
        };

        var result = EventLogSearcher.Search(request);

        Assert.False(result.Cancelled);
        Assert.Empty(result.Warnings);
        Assert.True(result.Items.Count <= 200);
        Assert.All(result.Items, item =>
        {
            Assert.InRange(item.Level, (byte)1, (byte)3);
            Assert.InRange(item.TimeCreated, from, to);
            Assert.Equal("시스템", item.LogDisplayName);
        });

        // 최신순 정렬
        for (var i = 1; i < result.Items.Count; i++)
        {
            Assert.True(result.Items[i - 1].TimeCreated >= result.Items[i].TimeCreated);
        }
    }

    [Fact]
    public void Searcher_UnknownLog_ReportsWarningInsteadOfThrowing()
    {
        var request = new EventLogSearchRequest
        {
            LogNames = ["SunTool-Not-Existing-Log"],
            Levels = EventLevels.Error,
            From = DateTime.Today.AddDays(-1),
            To = DateTime.Now,
        };

        var result = EventLogSearcher.Search(request);

        Assert.Empty(result.Items);
        Assert.Single(result.Warnings);
    }
}
