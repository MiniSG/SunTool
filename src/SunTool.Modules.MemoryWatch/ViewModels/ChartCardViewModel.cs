using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using SunTool.Modules.MemoryWatch.Logic;

namespace SunTool.Modules.MemoryWatch.ViewModels;

/// <summary>범례 한 칸 (선 색 + 이름).</summary>
public sealed record LegendItem(string Name, string Color);

/// <summary>그래프 카드 하나 (메모리 / 핸들 / GDI·USER / 스레드).</summary>
public sealed partial class ChartCardViewModel : ObservableObject
{
    /// <summary>선 색: 첫 번째 = 파랑, 두 번째 = 주황.</summary>
    public static readonly string[] SeriesColors = ["#2F6FEB", "#E8912D"];

    public ChartCardViewModel(string title, MetricKind[] metrics, MetricKind[] judged, string[]? colors = null, bool judgeOnly32Bit = false)
    {
        Title = title;
        JudgeOnly32Bit = judgeOnly32Bit;
        Metrics = metrics;
        Judged = judged;
        Colors = colors ?? SeriesColors;
        Legend = metrics.Select((m, i) => new LegendItem(MetricInfo.Name(m), Colors[i % Colors.Length])).ToList();
    }

    public string Title { get; }

    public MetricKind[] Metrics { get; }

    /// <summary>32비트 프로그램일 때만 판정 (가상 크기: 64비트는 주소 공간이 매우 넓어 한계가 의미 없음).</summary>
    public bool JudgeOnly32Bit { get; }

    /// <summary>판정 배지를 보일지 (판정하지 않는 카드는 숨김).</summary>
    [ObservableProperty]
    private bool _showVerdict;

    /// <summary>추세를 판정할 값.</summary>
    public MetricKind[] Judged { get; }

    public string[] Colors { get; }

    public IReadOnlyList<LegendItem> Legend { get; }

    /// <summary>선이 둘 이상일 때만 범례 표시 (하나면 제목으로 충분).</summary>
    public bool ShowLegend => Metrics.Length > 1;

    [ObservableProperty]
    private string _statsText = string.Empty;

    [ObservableProperty]
    private string _trendText = string.Empty;

    [ObservableProperty]
    private TrendVerdict _verdict = TrendVerdict.Pending;

    [ObservableProperty]
    private string _verdictText = TrendAnalyzer.VerdictText(TrendVerdict.Pending);

    [ObservableProperty]
    private bool _hasData;

    public void Clear()
    {
        StatsText = string.Empty;
        TrendText = string.Empty;
        Verdict = TrendVerdict.Pending;
        VerdictText = TrendAnalyzer.VerdictText(TrendVerdict.Pending);
        HasData = false;
        ShowVerdict = false;
    }

    /// <summary>
    /// 숫자와 판정을 갱신합니다. <paramref name="segment"/> 는 마지막으로 (다시) 시작한 뒤의 기록만 —
    /// 프로그램이 다시 켜지면 메모리가 처음부터 다시 쌓이므로 그 전 기록은 판정에 섞지 않습니다.
    /// </summary>
    public void Update(IReadOnlyList<MemorySample> segment, TrendOptions options, bool is32Bit)
    {
        if (segment.Count == 0)
        {
            Clear();
            return;
        }

        HasData = true;
        var stats = new StringBuilder();
        foreach (var metric in Metrics)
        {
            var values = segment.Select(s => s.Get(metric)).OfType<double>().ToList();
            if (stats.Length > 0)
            {
                stats.AppendLine();
            }

            if (values.Count == 0)
            {
                stats.Append(Loc.F("{0}: 읽을 수 없음 (권한 부족)", MetricInfo.Name(metric)));
                continue;
            }

            var first = values[0];
            var current = values[^1];
            var line = Loc.F(
                "현재 {0} · 최고 {1} · 증가 {2}",
                MetricInfo.Format(metric, current),
                MetricInfo.Format(metric, values.Max()),
                MetricInfo.FormatDelta(metric, current - first));
            stats.Append(Metrics.Length > 1 ? $"{MetricInfo.Name(metric)}  {line}" : line);
        }

        StatsText = stats.ToString();

        if (JudgeOnly32Bit && !is32Bit)
        {
            ShowVerdict = false;
            Verdict = TrendVerdict.Pending;
            VerdictText = TrendAnalyzer.VerdictText(Verdict);
            TrendText = Loc.T("64비트 프로그램은 주소 공간이 매우 넓어 판정하지 않습니다 (기록만).");
            return;
        }

        ShowVerdict = true;

        // 여러 값이면 가장 나쁜 판정을 보여 줌 (증가 > 보류 > 안정)
        var results = Judged
            .Where(m => segment.Any(s => s.Get(m) is not null))
            .Select(m => TrendAnalyzer.Analyze(segment, m, options, is32Bit))
            .ToList();
        if (results.Count == 0)
        {
            Verdict = TrendVerdict.Pending;
            VerdictText = TrendAnalyzer.VerdictText(Verdict);
            TrendText = Loc.T("판정할 값이 없습니다.");
            return;
        }

        var worst = results.OrderByDescending(r => r.Verdict switch { TrendVerdict.Rising => 2, TrendVerdict.Pending => 1, _ => 0 }).First();
        Verdict = worst.Verdict;
        VerdictText = TrendAnalyzer.VerdictText(worst.Verdict);
        TrendText = results.Count > 1
            ? string.Join(Environment.NewLine, results.Select(r => $"{MetricInfo.Name(r.Metric)}: {TrendAnalyzer.Describe(r)}"))
            : TrendAnalyzer.Describe(worst);
    }
}
