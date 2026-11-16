using System.Drawing;

namespace SunTool.Modules.ScreenCapture.Logic;

/// <summary>모니터 한 대. <see cref="Bounds"/> 는 화면 좌표(픽셀).</summary>
public sealed record MonitorInfo(int Index, Rectangle Bounds, bool IsPrimary)
{
    /// <summary>화면에 보여 줄 이름. 예) "모니터 1 (주) · 1920×1080".</summary>
    public string DisplayText =>
        Loc.F("모니터 {0}{1} · {2}×{3}", Index + 1, IsPrimary ? Loc.T(" (주)") : string.Empty, Bounds.Width, Bounds.Height);
}
