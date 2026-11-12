namespace SunTool.Modules.MemoryWatch.Logic;

/// <summary>
/// 그래프용 점 줄이기. 화면 가로 칸(버킷)마다 처음·최저·최고·마지막 점만 남겨
/// 며칠치 기록도 빠르게 그리면서 튀는 값(최고·최저)은 잃지 않습니다. 기록 자체는 줄이지 않습니다.
/// </summary>
public static class Downsampler
{
    public static List<(DateTime Time, double Value)> Reduce(IReadOnlyList<MemorySample> samples, MetricKind metric, int buckets)
    {
        var result = new List<(DateTime, double)>();
        if (samples.Count == 0)
        {
            return result;
        }

        buckets = Math.Max(1, buckets);
        if (samples.Count <= buckets * 4)
        {
            foreach (var s in samples)
            {
                if (s.Get(metric) is { } v)
                {
                    result.Add((s.Time, v));
                }
            }

            return result;
        }

        var start = samples[0].Time.Ticks;
        var span = Math.Max(1, samples[^1].Time.Ticks - start);
        var index = 0;
        for (var b = 0; b < buckets && index < samples.Count; b++)
        {
            var end = start + span * (b + 1) / buckets;
            (DateTime T, double V)? first = null, last = null, min = null, max = null;
            while (index < samples.Count && (samples[index].Time.Ticks <= end || b == buckets - 1))
            {
                var s = samples[index++];
                if (s.Get(metric) is not { } v)
                {
                    continue;
                }

                var p = (s.Time, v);
                first ??= p;
                last = p;
                if (min is null || v < min.Value.V)
                {
                    min = p;
                }

                if (max is null || v > max.Value.V)
                {
                    max = p;
                }
            }

            if (first is null)
            {
                continue;
            }

            // 시간 순서를 지켜 추가 (중복 제거)
            foreach (var p in new[] { first.Value, min!.Value, max!.Value, last!.Value }.Distinct().OrderBy(p => p.T))
            {
                result.Add(p);
            }
        }

        return result;
    }
}
