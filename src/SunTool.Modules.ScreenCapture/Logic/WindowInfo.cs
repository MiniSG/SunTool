using System.Drawing;

namespace SunTool.Modules.ScreenCapture.Logic;

/// <summary>열려 있는 창 하나 (목록에서 골라 캡처할 대상).</summary>
public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, Rectangle Bounds)
{
    /// <summary>목록에 보여 줄 이름. 예) "메모장 — 제목 없음".</summary>
    public string DisplayText =>
        string.IsNullOrEmpty(ProcessName) ? Title : $"{ProcessName} — {Title}";
}
