using System.Diagnostics;
using SunTool.Modules.MemoryWatch.Logic;

namespace SunTool.Tests;

public class MemoryWatchTrendTests
{
    private const long Mb = 1024 * 1024;
    private static readonly DateTime T0 = new(2026, 11, 12, 9, 0, 0);

    private static readonly TrendOptions NoWarmup = new()
    {
        Warmup = TimeSpan.Zero,
        MinObservationMemory = TimeSpan.FromMinutes(30),
        MinObservationCount = TimeSpan.FromMinutes(10),
    };

    /// <summary>톱니 모양(10분 주기로 0~40 MB 오르내림) + 시간당 drift MB 증가. 1분 간격.</summary>
    private static List<MemorySample> Sawtooth(int minutes, double driftMbPerHour, int handles = 300, double handleDriftPerHour = 0, int gdi = 100, double gdiDriftPerHour = 0, double pagedKbDriftPerHour = 0, double virtualMbDriftPerHour = 0)
    {
        var list = new List<MemorySample>();
        for (var m = 0; m <= minutes; m++)
        {
            var hours = m / 60.0;
            var tooth = (m % 10) * 4.0; // 0..36 MB
            var mem = (long)((200 + tooth + driftMbPerHour * hours) * Mb);
            list.Add(new MemorySample(T0.AddMinutes(m), mem,
                handles + (int)(handleDriftPerHour * hours) + (m % 3), gdi + (int)(gdiDriftPerHour * hours), 40, 12)
            {
                PagedPool = (long)((300 + (m % 5) * 8 + pagedKbDriftPerHour * hours) * 1024),
                NonPagedPool = 40 * 1024,
                VirtualSize = (long)((900 + tooth + virtualMbDriftPerHour * hours) * Mb),
            });
        }

        return list;
    }

    [Fact]
    public void Sawtooth_WithSteadyDrift_IsRising_AndSlopeIsClose()
    {
        var r = TrendAnalyzer.Analyze(Sawtooth(120, driftMbPerHour: 30), MetricKind.PrivateBytes, NoWarmup);
        Assert.Equal(TrendVerdict.Rising, r.Verdict);
        Assert.InRange(r.PerHour / Mb, 24, 36);
    }

    [Fact]
    public void Sawtooth_WithoutDrift_IsStable_EvenThoughPeaksMoveAround()
    {
        var r = TrendAnalyzer.Analyze(Sawtooth(120, driftMbPerHour: 0), MetricKind.PrivateBytes, NoWarmup);
        Assert.Equal(TrendVerdict.Stable, r.Verdict);
    }

    [Fact]
    public void ShortObservation_IsPending_WithMinutesLeft()
    {
        var r = TrendAnalyzer.Analyze(Sawtooth(20, driftMbPerHour: 100), MetricKind.PrivateBytes, NoWarmup);
        Assert.Equal(TrendVerdict.Pending, r.Verdict);
        Assert.Contains("10분 더", TrendAnalyzer.Describe(r));
    }

    [Fact]
    public void WarmupGrowth_IsExcluded()
    {
        // 처음 15분 동안 크게 늘고(로딩) 그 뒤로는 평평
        var samples = Sawtooth(120, 0).Select((s, i) => i < 15
            ? s with { PrivateBytes = s.PrivateBytes - (15 - i) * 20 * Mb }
            : s).ToList();
        var withWarmup = NoWarmup with { Warmup = TimeSpan.FromMinutes(15) };
        Assert.Equal(TrendVerdict.Stable, TrendAnalyzer.Analyze(samples, MetricKind.PrivateBytes, withWarmup).Verdict);
    }

    [Fact]
    public void HandleLeak_IsRising_AfterTenMinutes()
    {
        var r = TrendAnalyzer.Analyze(Sawtooth(30, 0, handles: 300, handleDriftPerHour: 600), MetricKind.Handles, NoWarmup);
        Assert.Equal(TrendVerdict.Rising, r.Verdict);
        Assert.Null(r.HoursToLimit); // 핸들은 실질적 한계가 없어 표시하지 않음
    }

    [Fact]
    public void SmallJitter_IsNotRising()
    {
        // 핸들이 ±2 정도만 출렁임
        var r = TrendAnalyzer.Analyze(Sawtooth(60, 0), MetricKind.Handles, NoWarmup);
        Assert.NotEqual(TrendVerdict.Rising, r.Verdict);
    }

    [Fact]
    public void GdiLeak_ShowsTimeToLimit()
    {
        var r = TrendAnalyzer.Analyze(Sawtooth(60, 0, gdi: 1000, gdiDriftPerHour: 900), MetricKind.Gdi, NoWarmup);
        Assert.Equal(TrendVerdict.Rising, r.Verdict);
        Assert.NotNull(r.HoursToLimit);
        Assert.InRange(r.HoursToLimit!.Value, 8, 11); // (10000-1900)/900 ≈ 9시간
        Assert.Contains("GDI 한계", TrendAnalyzer.Describe(r));
    }

    [Fact]
    public void ThirtyTwoBitMemoryLeak_ShowsTimeTo2Gb()
    {
        var r = TrendAnalyzer.Analyze(Sawtooth(120, 100), MetricKind.PrivateBytes, NoWarmup, is32Bit: true);
        Assert.Equal(TrendVerdict.Rising, r.Verdict);
        Assert.NotNull(r.HoursToLimit);
        Assert.Contains("2 GB", TrendAnalyzer.Describe(r));
    }

    [Fact]
    public void PagedPoolLeak_IsRising_AndSmallPoolJitterIsStable()
    {
        // 페이징 풀이 시간당 600 KB 씩 늘어남 (기준 300 KB → 1시간 뒤 약 900 KB)
        var leak = TrendAnalyzer.Analyze(Sawtooth(60, 0, pagedKbDriftPerHour: 600), MetricKind.PagedPool, NoWarmup);
        Assert.Equal(TrendVerdict.Rising, leak.Verdict);
        Assert.InRange(leak.PerHour / 1024, 500, 700);
        Assert.Contains("KB", TrendAnalyzer.Describe(leak));

        Assert.Equal(TrendVerdict.Stable, TrendAnalyzer.Analyze(Sawtooth(60, 0), MetricKind.PagedPool, NoWarmup).Verdict);
        Assert.Equal(TrendVerdict.Stable, TrendAnalyzer.Analyze(Sawtooth(60, 0), MetricKind.NonPagedPool, NoWarmup).Verdict);
    }

    [Fact]
    public void ThirtyTwoBitVirtualGrowth_ShowsTimeToAddressLimit()
    {
        var r = TrendAnalyzer.Analyze(Sawtooth(120, 0, virtualMbDriftPerHour: 200), MetricKind.VirtualSize, NoWarmup, is32Bit: true);
        Assert.Equal(TrendVerdict.Rising, r.Verdict);
        Assert.InRange(r.HoursToLimit!.Value, 3, 5.5); // (2048 - 약 1300) / 200 ≈ 4시간
        Assert.Contains("주소 공간 한계 2 GB", TrendAnalyzer.Describe(r));

        // 이미 2 GB 를 넘었으면 대용량 주소(LAA) 프로그램 → 4 GB 기준
        var big = Sawtooth(120, 0, virtualMbDriftPerHour: 200).Select(s => s with { VirtualSize = s.VirtualSize + 1500 * Mb }).ToList();
        Assert.Contains("4 GB", TrendAnalyzer.Describe(TrendAnalyzer.Analyze(big, MetricKind.VirtualSize, NoWarmup, is32Bit: true)));

        // 64비트는 한계를 계산하지 않음
        Assert.Null(TrendAnalyzer.Analyze(Sawtooth(120, 0, virtualMbDriftPerHour: 200), MetricKind.VirtualSize, NoWarmup).HoursToLimit);
    }

    [Fact]
    public void VirtualCard_IsNotJudged_For64Bit()
    {
        var card = new SunTool.Modules.MemoryWatch.ViewModels.ChartCardViewModel("v", [MetricKind.VirtualSize], [MetricKind.VirtualSize], judgeOnly32Bit: true);
        var samples = Sawtooth(120, 0, virtualMbDriftPerHour: 200);
        card.Update(samples, NoWarmup, is32Bit: false);
        Assert.False(card.ShowVerdict);
        Assert.Contains("64비트", card.TrendText);

        card.Update(samples, NoWarmup, is32Bit: true);
        Assert.True(card.ShowVerdict);
        Assert.Equal(TrendVerdict.Rising, card.Verdict);
    }

    [Fact]
    public void UnreadableValues_ArePending()
    {
        var samples = Sawtooth(60, 0).Select(s => s with { Gdi = -1 }).ToList();
        Assert.Equal(TrendVerdict.Pending, TrendAnalyzer.Analyze(samples, MetricKind.Gdi, NoWarmup).Verdict);
    }

    [Fact]
    public void TheilSen_IgnoresOutlier()
    {
        var pts = new List<(double, double)> { (0, 0), (1, 1), (2, 2), (3, 300), (4, 4), (5, 5) };
        Assert.Equal(1, TrendAnalyzer.TheilSenSlope(pts), 3);
        Assert.True(TrendAnalyzer.KendallTau(pts) > 0.7);
    }
}

public class MemoryWatchDataTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 9, 0, 0);

    [Fact]
    public void Downsampler_LimitsPoints_AndKeepsSpikes()
    {
        var samples = Enumerable.Range(0, 100_000)
            .Select(i => new MemorySample(T0.AddSeconds(i), 100, i == 54_321 ? 9_999 : 100 + i % 7, 0, 0, 1))
            .ToList();
        var points = Downsampler.Reduce(samples, MetricKind.Handles, 400);
        Assert.InRange(points.Count, 400, 1600);
        Assert.Contains(points, p => p.Value == 9_999);           // 튀는 값 유지
        Assert.Equal(points.OrderBy(p => p.Time).Select(p => p.Time), points.Select(p => p.Time)); // 시간 순서
        Assert.Equal(T0, points[0].Time);
    }

    [Fact]
    public void Downsampler_SmallInput_ReturnsAllReadable()
    {
        var samples = new[] { new MemorySample(T0, 1, 1, -1, 1, 1), new MemorySample(T0.AddSeconds(1), 1, 1, 5, 1, 1) };
        Assert.Single(Downsampler.Reduce(samples, MetricKind.Gdi, 100));
    }

    [Fact]
    public void Csv_RowsUseMb_AndBlankForUnreadable()
    {
        var row = MemoryCsv.Row(new MemorySample(T0, 150 * 1024 * 1024 + 512 * 1024, 321, -1, 12, 8)
        {
            PagedPool = 300 * 1024 + 512,
            NonPagedPool = 40 * 1024,
            VirtualSize = 900L * 1024 * 1024,
            SystemCommit = 12_000L * 1024 * 1024,
            SystemCommitLimit = 32_000L * 1024 * 1024,
        }, 4242);
        Assert.Equal("2026-11-12 09:00:00,4242,150.5,300.5,40.0,900.0,321,,12,8,12000.0,32000.0,", row);
        Assert.Contains("커밋 (MB)", MemoryCsv.Header);
        Assert.DoesNotContain("Working Set", MemoryCsv.Header);

        // 머리글·측정 줄·기록 줄의 칸 수가 같아야 엑셀에서 열이 맞음
        var columns = MemoryCsv.Header.Split(',').Length;
        Assert.Equal(columns, row.Split(',').Length);
        var eventRow = MemoryCsv.EventRow(new WatchEvent(T0, "다시 시작", true), 7);
        Assert.Equal(columns, eventRow.Split(',').Length);
        Assert.EndsWith("\"다시 시작: a, b\"", MemoryCsv.EventRow(new WatchEvent(T0, "다시 시작: a, b", true), 0));
        Assert.Equal("a_b", MemoryCsv.SafeFileName("a/b"));
    }

    [Fact]
    public void CsvRecorder_WritesHeaderAndLinesImmediately()
    {
        var path = Path.Combine(Path.GetTempPath(), $"suntool-mw-{Guid.NewGuid():N}", "x.csv");
        using (var recorder = new CsvRecorder(path))
        {
            recorder.Write("a,b");
            // 쓰는 중에도 다른 프로그램(엑셀 등)이 읽을 수 있어야 함
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var text = reader.ReadToEnd();
            Assert.StartsWith(MemoryCsv.Header, text.TrimStart('﻿'));
            Assert.Contains("a,b", text);
        }

        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}

public class MemoryWatchSystemCommitTests
{
    [Fact]
    public void SystemCommit_IsFormatted_AndWarnsNearLimit()
    {
        var gb = 1024L * 1024 * 1024;
        var s = new MemorySample(DateTime.Now, 1, 1, 1, 1, 1) { SystemCommit = 12 * gb, SystemCommitLimit = 32 * gb };
        var text = SunTool.Modules.MemoryWatch.ViewModels.MemoryWatchViewModel.FormatSystemCommit(s, out var near);
        Assert.Contains("12.0 / 32.0 GB", text);
        Assert.False(near);

        SunTool.Modules.MemoryWatch.ViewModels.MemoryWatchViewModel.FormatSystemCommit(s with { SystemCommit = 30 * gb }, out near);
        Assert.True(near);
        Assert.Equal(string.Empty, SunTool.Modules.MemoryWatch.ViewModels.MemoryWatchViewModel.FormatSystemCommit(null, out _));
    }
}

public class MemoryWatchSamplerTests
{
    [Fact]
    public void Sampler_ReadsThisProcess()
    {
        using var sampler = ProcessSampler.Open(Environment.ProcessId)!;
        var sample = sampler.Sample(DateTime.Now)!;
        Assert.True(sample.PrivateBytes > 1024 * 1024);
        Assert.True(sample.Handles > 0);
        Assert.True(sample.Threads > 0);
        Assert.True(sampler.CanReadGui);
        Assert.True(sample.Gdi >= 0 && sample.User >= 0);
        Assert.True(sample.PagedPool > 0 && sample.NonPagedPool > 0);
        Assert.True(sample.VirtualSize > sample.PrivateBytes);
        Assert.True(sample.SystemCommit > sample.PrivateBytes && sample.SystemCommitLimit >= sample.SystemCommit);
        Assert.False(sampler.Is32Bit);
    }

    [Fact]
    public void Sampler_DetectsExit_AndFindsRestartByName()
    {
        var first = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        Process? second = null;
        try
        {
            using var sampler = ProcessSampler.Open(first.Id)!;
            Assert.NotNull(sampler.Sample(DateTime.Now));

            first.Kill();
            first.WaitForExit();
            Assert.Null(sampler.Sample(DateTime.Now));

            second = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
            using var again = ProcessSampler.OpenNewestByName("PING", excludeId: first.Id);
            Assert.NotNull(again);
            Assert.Equal(second.Id, again!.Id);
        }
        finally
        {
            if (!first.HasExited) first.Kill();
            if (second is { HasExited: false }) second.Kill();
        }
    }

    [Fact]
    public void ListProcesses_IncludesThisProcess()
    {
        Assert.Contains(ProcessSampler.ListProcesses(), p => p.Id == Environment.ProcessId);
    }

    [Fact]
    public void Open_ReturnsNull_ForMissingProcess()
    {
        Assert.Null(ProcessSampler.Open(int.MaxValue - 7));
    }
}
