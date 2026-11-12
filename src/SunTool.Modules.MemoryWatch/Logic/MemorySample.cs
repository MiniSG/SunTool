namespace SunTool.Modules.MemoryWatch.Logic;

/// <summary>기록하는 값의 종류.</summary>
public enum MetricKind
{
    /// <summary>커밋 크기 = 그 프로세스만 쓰는 메모리 (메모리릭을 볼 때 가장 중요). 작업 관리자의 '커밋 크기'.</summary>
    PrivateBytes,

    /// <summary>이 프로세스 때문에 커널이 쓰는 페이징 풀 (드라이버·SDK·핸들 쪽 누수).</summary>
    PagedPool,

    /// <summary>이 프로세스 때문에 커널이 쓰는 비페이징(NP) 풀.</summary>
    NonPagedPool,

    /// <summary>예약까지 포함한 주소 공간 크기 (32비트 프로그램은 2 GB 한계).</summary>
    VirtualSize,

    Handles,

    /// <summary>GDI 개체 수 (프로세스당 기본 한계 10,000).</summary>
    Gdi,

    /// <summary>USER 개체 수 (프로세스당 기본 한계 10,000).</summary>
    User,

    Threads,
}

/// <summary>
/// 측정 한 번. 읽지 못한 값(권한 부족 등)은 -1.
/// 시스템 전체 커밋은 판정하지 않고 참고로만 남깁니다 (한도에 차면 모든 프로그램이 메모리를 못 얻음).
/// </summary>
public sealed record MemorySample(
    DateTime Time,
    long PrivateBytes,
    int Handles,
    int Gdi,
    int User,
    int Threads)
{
    public long PagedPool { get; init; } = -1;

    public long NonPagedPool { get; init; } = -1;

    public long VirtualSize { get; init; } = -1;

    /// <summary>시스템 전체 커밋 (바이트).</summary>
    public long SystemCommit { get; init; } = -1;

    /// <summary>시스템 전체 커밋 한도 (바이트).</summary>
    public long SystemCommitLimit { get; init; } = -1;

    /// <summary>값 하나 (읽지 못했으면 null). 메모리는 바이트.</summary>
    public double? Get(MetricKind kind)
    {
        double value = kind switch
        {
            MetricKind.PrivateBytes => PrivateBytes,
            MetricKind.PagedPool => PagedPool,
            MetricKind.NonPagedPool => NonPagedPool,
            MetricKind.VirtualSize => VirtualSize,
            MetricKind.Handles => Handles,
            MetricKind.Gdi => Gdi,
            MetricKind.User => User,
            _ => Threads,
        };
        return value < 0 ? null : value;
    }
}

/// <summary>감시 중 일어난 일 (프로세스 종료·다시 시작 등). 그래프에 세로선으로 표시합니다.</summary>
public sealed record WatchEvent(DateTime Time, string Text, bool IsRestart);

public static class MetricInfo
{
    public static bool IsBytes(MetricKind kind) => kind is MetricKind.PrivateBytes or MetricKind.PagedPool or MetricKind.NonPagedPool or MetricKind.VirtualSize;

    public static string Name(MetricKind kind) => kind switch
    {
        MetricKind.PrivateBytes => Loc.T("커밋"),
        MetricKind.PagedPool => Loc.T("페이징 풀"),
        MetricKind.NonPagedPool => Loc.T("NP 풀"),
        MetricKind.VirtualSize => Loc.T("가상 크기"),
        MetricKind.Handles => Loc.T("핸들"),
        MetricKind.Gdi => "GDI",
        MetricKind.User => "USER",
        _ => Loc.T("스레드"),
    };

    /// <summary>화면 표시용 값 (메모리는 MB, 커널 풀처럼 작은 값은 KB).</summary>
    public static string Format(MetricKind kind, double value) =>
        !IsBytes(kind) ? $"{value:N0}"
        : kind is MetricKind.PagedPool or MetricKind.NonPagedPool && Math.Abs(value) < 10 * 1024 * 1024 ? $"{value / 1024:N0} KB"
        : Math.Abs(value) >= 10.0 * 1024 * 1024 * 1024 ? $"{value / (1024.0 * 1024 * 1024):N1} GB" // 64비트 가상 크기 (TB 단위)
        : $"{value / (1024 * 1024):N1} MB";

    /// <summary>증가량 표시용 (+12.3 MB, -5).</summary>
    public static string FormatDelta(MetricKind kind, double delta)
    {
        var text = Format(kind, Math.Abs(delta));
        // 반올림해서 0 이 되면 "-0" 대신 "+0"
        return (delta < 0 && text.Any(c => c is >= '1' and <= '9') ? "-" : "+") + text;
    }
}
