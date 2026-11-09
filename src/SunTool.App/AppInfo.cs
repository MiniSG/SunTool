using SunTool.Core;

namespace SunTool.App;

internal static class AppInfo
{
    /// <summary>빌드 날짜 기반 버전 (예: 2026.11.09).</summary>
    public static string Version => AppVersion.Text;
}
