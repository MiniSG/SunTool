using System.Drawing;
using SunTool.Modules.ScreenCapture.Logic;

namespace SunTool.Tests;

/// <summary>화면 캡처 모듈의 순수 로직 검사 (화면·Win32 없이 돌아가는 부분만).</summary>
public class ScreenCaptureTests
{
    private static readonly DateTime Sample = new(2026, 11, 16, 15, 30, 0);

    // ===== 파일 이름 규칙 =====

    [Fact]
    public void Format_UsesDefault_WhenPatternEmpty()
    {
        Assert.Equal("capture-20261116-153000.png", CaptureFileName.Format(null, Sample));
        Assert.Equal("capture-20261116-153000.png", CaptureFileName.Format("   ", Sample));
    }

    [Fact]
    public void Format_AppliesPattern_AndAddsPng()
    {
        Assert.Equal("shot_2026-11-16.png", CaptureFileName.Format("'shot_'yyyy-MM-dd", Sample));
    }

    [Fact]
    public void Format_StripsInvalidFileNameChars()
    {
        // 사용자 패턴에 파일명에 못 쓰는 ':' 가 들어가도(시간 구분자) 지워집니다.
        var name = CaptureFileName.Format("'t'HH:mm", Sample);
        Assert.Equal("t1530.png", name);
    }

    [Fact]
    public void Format_SubstitutesHostIpUserTokens()
    {
        var tokens = new Dictionary<string, string> { ["host"] = "PC-1", ["ip"] = "192.168.0.5", ["user"] = "kim" };
        var name = CaptureFileName.Format("'cap-'{host}'-'{ip}'-'{user}", Sample, tokens);
        Assert.Equal("cap-PC-1-192.168.0.5-kim.png", name);
    }

    [Fact]
    public void Format_TokensAreCaseInsensitive()
    {
        var tokens = new Dictionary<string, string> { ["host"] = "PC-1" };
        Assert.Equal("PC-1.png", CaptureFileName.Format("{HOST}", Sample, tokens));
    }

    [Fact]
    public void Format_CombinesTokensAndDate()
    {
        var tokens = new Dictionary<string, string> { ["host"] = "PC" };
        Assert.Equal("PC-20261116.png", CaptureFileName.Format("{host}'-'yyyyMMdd", Sample, tokens));
    }

    [Fact]
    public void Unique_ReturnsSame_WhenNotExists()
    {
        Assert.Equal("a.png", CaptureFileName.Unique("a.png", _ => false));
    }

    [Fact]
    public void Unique_AppendsCounter_WhenExists()
    {
        var taken = new HashSet<string> { "a.png", "a-2.png" };
        Assert.Equal("a-3.png", CaptureFileName.Unique("a.png", taken.Contains));
    }

    // ===== 가상 화면 계산 =====

    [Fact]
    public void VirtualScreen_BoundsAllMonitors()
    {
        var monitors = new[]
        {
            new MonitorInfo(0, Rectangle.FromLTRB(0, 0, 1920, 1080), true),
            new MonitorInfo(1, Rectangle.FromLTRB(1920, 0, 3200, 1024), false),
        };

        var vs = MonitorEnumerator.VirtualScreen(monitors);
        Assert.Equal(Rectangle.FromLTRB(0, 0, 3200, 1080), vs);
    }

    [Fact]
    public void VirtualScreen_HandlesNegativeOrigin()
    {
        var monitors = new[]
        {
            new MonitorInfo(0, Rectangle.FromLTRB(0, 0, 1920, 1080), true),
            new MonitorInfo(1, Rectangle.FromLTRB(-1280, 0, 0, 1024), false),
        };

        var vs = MonitorEnumerator.VirtualScreen(monitors);
        Assert.Equal(Rectangle.FromLTRB(-1280, 0, 1920, 1080), vs);
    }

    [Fact]
    public void VirtualScreen_Empty_WhenNoMonitors()
    {
        Assert.Equal(Rectangle.Empty, MonitorEnumerator.VirtualScreen([]));
    }

    // ===== 표시 문구 =====

    [Fact]
    public void MonitorInfo_DisplayText_MarksPrimaryAndSize()
    {
        Assert.Equal("모니터 1 (주) · 1920×1080", new MonitorInfo(0, Rectangle.FromLTRB(0, 0, 1920, 1080), true).DisplayText);
        Assert.Equal("모니터 2 · 1280×1024", new MonitorInfo(1, Rectangle.FromLTRB(1920, 0, 3200, 1024), false).DisplayText);
    }

    [Fact]
    public void WindowInfo_DisplayText_CombinesProcessAndTitle()
    {
        Assert.Equal("notepad — 제목 없음", new WindowInfo(IntPtr.Zero, "제목 없음", "notepad", Rectangle.Empty).DisplayText);
        Assert.Equal("제목만", new WindowInfo(IntPtr.Zero, "제목만", string.Empty, Rectangle.Empty).DisplayText);
    }
}
