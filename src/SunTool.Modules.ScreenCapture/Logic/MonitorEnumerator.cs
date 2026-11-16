using System.Drawing;
using static SunTool.Modules.ScreenCapture.Logic.NativeMethods;

namespace SunTool.Modules.ScreenCapture.Logic;

/// <summary>지금 연결된 모니터 목록 (화면 좌표, 픽셀).</summary>
public static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorInfo> List()
    {
        var found = new List<Rectangle>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref RECT r, IntPtr _) =>
        {
            found.Add(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            return true;
        }, IntPtr.Zero);

        var result = new List<MonitorInfo>(found.Count);
        for (var i = 0; i < found.Count; i++)
        {
            // 주 모니터는 원점(0,0)에서 시작합니다.
            var isPrimary = found[i].Left == 0 && found[i].Top == 0;
            result.Add(new MonitorInfo(i, found[i], isPrimary));
        }

        return result;
    }

    /// <summary>모든 모니터를 감싸는 전체 가상 화면 영역.</summary>
    public static Rectangle VirtualScreen(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            return Rectangle.Empty;
        }

        var left = monitors.Min(m => m.Bounds.Left);
        var top = monitors.Min(m => m.Bounds.Top);
        var right = monitors.Max(m => m.Bounds.Right);
        var bottom = monitors.Max(m => m.Bounds.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
