namespace SunTool.Modules.MemoryWatch.Logic;

/// <summary>참고용 추세 판정.</summary>
public enum TrendVerdict
{
    /// <summary>관찰 시간이 부족하거나 아직 분명하지 않음.</summary>
    Pending,

    /// <summary>바닥선이 평평함.</summary>
    Stable,

    /// <summary>바닥선이 꾸준히 올라감 (누수 의심).</summary>
    Rising,
}

/// <summary>판정 조건. 기본값은 화면에서 쓰는 값이고, 테스트에서 짧게 바꿀 수 있습니다.</summary>
public sealed record TrendOptions
{
    /// <summary>처음 이 시간은 프로그램이 자리 잡는 중(워밍업)이라 계산에서 뺍니다.</summary>
    public TimeSpan Warmup { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>메모리는 워밍업 뒤 이만큼 봐야 판정합니다 (그 전에는 '판정 보류').</summary>
    public TimeSpan MinObservationMemory { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>핸들·GDI·USER·스레드는 정수라 더 짧게 봐도 됩니다.</summary>
    public TimeSpan MinObservationCount { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>판정 결과와 근거.</summary>
public sealed record TrendResult(
    MetricKind Metric,
    TrendVerdict Verdict,
    double PerHour,
    double FloorIncrease,
    TimeSpan Observed,
    TimeSpan Needed,
    double? HoursToLimit,
    string? LimitText)
{
    public static TrendResult PendingFor(MetricKind metric, TimeSpan observed, TimeSpan needed) =>
        new(metric, TrendVerdict.Pending, 0, 0, observed, needed, null, null);
}

/// <summary>
/// 같은 작업을 반복하는 동안의 '바닥선'(구간별 최저값) 변화로 증가 추세를 판단합니다.
/// 메모리는 톱니처럼 오르내리므로 평균 대신 최저값을 보면 캐시·GC 로 인한 오판이 줄어듭니다.
/// 결과는 참고용이며 원인 위치는 알려 주지 않습니다.
/// </summary>
public static class TrendAnalyzer
{
    /// <summary>GDI/USER 개체 기본 한계 (프로세스당).</summary>
    public const int GuiObjectLimit = 10_000;

    private const double Mb = 1024 * 1024;

    public static TrendResult Analyze(IReadOnlyList<MemorySample> samples, MetricKind metric, TrendOptions options, bool is32Bit = false)
    {
        var needed = MetricInfo.IsBytes(metric) ? options.MinObservationMemory : options.MinObservationCount;
        if (samples.Count == 0)
        {
            return TrendResult.PendingFor(metric, TimeSpan.Zero, needed);
        }

        var start = samples[0].Time + options.Warmup;
        var points = samples
            .Where(s => s.Time >= start)
            .Select(s => (s.Time, Value: s.Get(metric)))
            .Where(p => p.Value is not null)
            .Select(p => (p.Time, Value: p.Value!.Value))
            .ToList();

        var observed = points.Count > 1 ? points[^1].Time - points[0].Time : TimeSpan.Zero;
        if (observed < needed || points.Count < 8)
        {
            return TrendResult.PendingFor(metric, observed, needed);
        }

        // 시간으로 나눈 구간(4~12개)마다 최저값 = 바닥선
        var bucketCount = Math.Clamp(points.Count / 4, 4, 12);
        var t0 = points[0].Time;
        var span = observed.TotalSeconds;
        var floors = new List<(double Hours, double Value)>();
        for (var b = 0; b < bucketCount; b++)
        {
            var from = span * b / bucketCount;
            var to = span * (b + 1) / bucketCount;
            var bucket = points.Where(p =>
            {
                var sec = (p.Time - t0).TotalSeconds;
                return sec >= from && (b == bucketCount - 1 ? sec <= to : sec < to);
            }).ToList();
            if (bucket.Count == 0)
            {
                continue;
            }

            var min = bucket.MinBy(p => p.Value);
            floors.Add(((min.Time - t0).TotalHours, min.Value));
        }

        if (floors.Count < 4)
        {
            return TrendResult.PendingFor(metric, observed, needed);
        }

        var perHour = TheilSenSlope(floors);
        var tau = KendallTau(floors);
        var increase = floors[^1].Value - floors[0].Value;
        var threshold = MeaningfulIncrease(metric, floors[0].Value);

        var verdict = tau >= 0.5 && increase >= threshold && perHour > 0
            ? TrendVerdict.Rising
            : tau <= 0.3 || increase < threshold * 0.5
                ? TrendVerdict.Stable
                : TrendVerdict.Pending;

        double? hoursToLimit = null;
        string? limitText = null;
        if (verdict == TrendVerdict.Rising && Limit(metric, is32Bit, points[^1].Value) is { } limit)
        {
            var current = points[^1].Value;
            hoursToLimit = current >= limit.Value ? 0 : (limit.Value - current) / perHour;
            limitText = limit.Text;
        }

        return new TrendResult(metric, verdict, perHour, increase, observed, needed, hoursToLimit, limitText);
    }

    /// <summary>의미 있다고 볼 최소 증가량 (작은 출렁임은 무시).</summary>
    public static double MeaningfulIncrease(MetricKind metric, double baseline) => metric switch
    {
        MetricKind.PrivateBytes => Math.Max(5 * Mb, baseline * 0.02),
        MetricKind.PagedPool or MetricKind.NonPagedPool => Math.Max(256 * 1024, baseline * 0.10),
        MetricKind.VirtualSize => Math.Max(32 * Mb, baseline * 0.03),
        MetricKind.Threads => Math.Max(5, baseline * 0.10),
        _ => Math.Max(20, baseline * 0.05),
    };

    private static (double Value, string Text)? Limit(MetricKind metric, bool is32Bit, double current) => metric switch
    {
        MetricKind.PrivateBytes when is32Bit => (2048 * Mb, Loc.T("32비트 프로그램 한계 2 GB")),
        // 이미 2 GB 를 넘었으면 대용량 주소(LAA)를 쓰는 프로그램이라 4 GB 까지 쓸 수 있음
        MetricKind.VirtualSize when is32Bit && current >= 2048 * Mb => (4096 * Mb, Loc.T("32비트 주소 공간 한계 4 GB")),
        MetricKind.VirtualSize when is32Bit => (2048 * Mb, Loc.T("32비트 주소 공간 한계 2 GB")),
        MetricKind.Gdi => (GuiObjectLimit, Loc.T("GDI 한계 10,000개")),
        MetricKind.User => (GuiObjectLimit, Loc.T("USER 한계 10,000개")),
        _ => null,
    };

    /// <summary>모든 점 쌍의 기울기 중앙값 (튀는 값에 강함).</summary>
    public static double TheilSenSlope(IReadOnlyList<(double X, double Y)> points)
    {
        var slopes = new List<double>();
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var dx = points[j].X - points[i].X;
                if (dx > 0)
                {
                    slopes.Add((points[j].Y - points[i].Y) / dx);
                }
            }
        }

        if (slopes.Count == 0)
        {
            return 0;
        }

        slopes.Sort();
        var mid = slopes.Count / 2;
        return slopes.Count % 2 == 1 ? slopes[mid] : (slopes[mid - 1] + slopes[mid]) / 2;
    }

    /// <summary>순서대로 커지는 정도 (1 = 항상 증가, 0 = 무작위, -1 = 항상 감소).</summary>
    public static double KendallTau(IReadOnlyList<(double X, double Y)> points)
    {
        var concordant = 0;
        var discordant = 0;
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var d = points[j].Y - points[i].Y;
                if (d > 0)
                {
                    concordant++;
                }
                else if (d < 0)
                {
                    discordant++;
                }
            }
        }

        var pairs = points.Count * (points.Count - 1) / 2;
        return pairs == 0 ? 0 : (double)(concordant - discordant) / pairs;
    }

    /// <summary>화면에 쓰는 근거 문구.</summary>
    public static string Describe(TrendResult r)
    {
        if (r.Verdict == TrendVerdict.Pending && r.Observed < r.Needed)
        {
            var left = r.Needed - r.Observed;
            return Loc.F("판정 보류: 워밍업 뒤 {0:N0}분 더 관찰 필요", Math.Ceiling(left.TotalMinutes));
        }

        var observed = FormatSpan(r.Observed);
        var trend = Loc.F("바닥선 {0}/시간 · {1} 관찰", MetricInfo.FormatDelta(r.Metric, r.PerHour), observed);
        return r.Verdict switch
        {
            TrendVerdict.Rising when r.HoursToLimit is { } h => trend + " · " + (h <= 0
                ? Loc.F("이미 {0}에 도달", r.LimitText!)
                : Loc.F("이 속도면 약 {0}에 {1} 도달", FormatSpan(TimeSpan.FromHours(h)), r.LimitText!)),
            TrendVerdict.Pending => trend + " · " + Loc.T("아직 분명하지 않음 (관찰 계속)"),
            _ => trend,
        };
    }

    public static string VerdictText(TrendVerdict verdict) => verdict switch
    {
        TrendVerdict.Rising => Loc.T("증가 추세"),
        TrendVerdict.Stable => Loc.T("안정"),
        _ => Loc.T("판정 보류"),
    };

    public static string FormatSpan(TimeSpan span) =>
        span.TotalHours >= 48 ? Loc.F("{0:0.#}일", span.TotalDays)
        : span.TotalMinutes >= 90 ? Loc.F("{0:0.#}시간", span.TotalHours)
        : Loc.F("{0:N0}분", Math.Max(0, span.TotalMinutes));
}
