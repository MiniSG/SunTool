using System.Diagnostics;
using System.Drawing;
using static SunTool.Modules.ScreenCapture.Logic.NativeMethods;

namespace SunTool.Modules.ScreenCapture.Logic;

/// <summary>지금 화면에 보이는 일반 창 목록 만들기. 창을 건드리지 않고 조회만 합니다.</summary>
public static class WindowEnumerator
{
    /// <summary>제목이 있는, 보이는 최상위 창들. <paramref name="excludeProcessId"/>(보통 SunTool 자신)는 뺍니다.</summary>
    public static IReadOnlyList<WindowInfo> List(int excludeProcessId)
    {
        var list = new List<WindowInfo>();
        EnumWindows((hWnd, _) =>
        {
            if (TryDescribe(hWnd, excludeProcessId, out var info))
            {
                list.Add(info);
            }

            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// 캡처 대상이 될 "활성 창". 지금 활성 창이 SunTool 자신이 아니면 그 창을,
    /// 자신이면 Z 순서상 바로 아래(직전에 쓰던) 창을 돌려줍니다.
    /// </summary>
    public static WindowInfo? ForegroundOrPrevious(IntPtr selfWindow, int excludeProcessId)
    {
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != selfWindow && TryDescribe(fg, excludeProcessId, out var info))
        {
            return info;
        }

        // 자신이 활성이면 Z 순서로 내려가며 첫 번째 쓸 수 있는 창을 찾습니다.
        var start = fg != IntPtr.Zero ? fg : selfWindow;
        for (var h = GetWindow(start, GW_HWNDNEXT); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT))
        {
            if (h != selfWindow && TryDescribe(h, excludeProcessId, out var other))
            {
                return other;
            }
        }

        return null;
    }

    private static bool TryDescribe(IntPtr hWnd, int excludeProcessId, out WindowInfo info)
    {
        info = null!;
        if (!IsWindowVisible(hWnd) || GetAncestor(hWnd, GA_ROOT) != hWnd)
        {
            return false;
        }

        var style = GetWindowLong(hWnd, GWL_STYLE);
        if ((style & unchecked((int)WS_CHILD)) != 0)
        {
            return false;
        }

        var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        if ((exStyle & unchecked((int)WS_EX_TOOLWINDOW)) != 0)
        {
            return false;
        }

        // UWP 의 숨은 유령 창 거르기 (cloaked).
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return false;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowText(hWnd, buffer, buffer.Length);
        var title = new string(buffer, 0, copied).Trim();
        if (title.Length == 0)
        {
            return false;
        }

        GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == (uint)excludeProcessId)
        {
            return false;
        }

        info = new WindowInfo(hWnd, title, ProcessNameOf((int)pid), WindowBounds(hWnd));
        return true;
    }

    private static Rectangle WindowBounds(IntPtr hWnd)
    {
        var rectSize = System.Runtime.InteropServices.Marshal.SizeOf<RECT>();

        // 그림자를 뺀 실제 보이는 테두리 (DWM) 를 먼저 시도.
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, rectSize) == 0 && r.Width > 0 && r.Height > 0)
        {
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        return GetWindowRect(hWnd, out var w) ? Rectangle.FromLTRB(w.Left, w.Top, w.Right, w.Bottom) : Rectangle.Empty;
    }

    private static string ProcessNameOf(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
