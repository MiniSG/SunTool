using System.Reflection;

namespace SunTool.Core;

/// <summary>프로그램 버전 (빌드 날짜 기준, 예: 2026.11.09). 모든 모듈에서 같은 값을 씁니다.</summary>
public static class AppVersion
{
    private static readonly Lazy<string> Value = new(Resolve);

    public static string Text => Value.Value;

    private static string Resolve()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
