using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SunTool.Core.Localization;

/// <summary>
/// 화면 언어 (한국어 / English).
/// 한국어 원문 자체를 키로 쓰고, 영어는 각 어셈블리에 포함된 i18n/en.json ({"한국어": "English"}) 에서 찾습니다.
///  - 코드: Loc.T("원본 폴더"), Loc.F("{0}개 파일", count)
///  - XAML: Text="{l:T '원본 폴더'}"  (SunTool.Wpf.Localization.TExtension)
/// 언어는 시작할 때 한 번 정해지고, 바꾸면 프로그램을 다시 시작합니다.
/// 새 모듈은 프로젝트에 i18n/en.json 만 넣으면 됩니다 (Directory.Build.targets 가 자동 포함).
/// </summary>
public static class Loc
{
    public const string Korean = "ko";
    public const string English = "en";

    private static readonly ConcurrentDictionary<string, string> EnglishTexts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> Missing = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RegisteredAssemblies = new(StringComparer.Ordinal);

    public static string Language { get; private set; } = Korean;

    public static bool IsEnglish => Language == English;

    /// <summary>번역 사전 항목 수 (진단용).</summary>
    public static int Count => EnglishTexts.Count;

    /// <summary>영어 모드에서 사전에 없어 한국어로 표시된 문구 (터미널 'lang missing' 진단용).</summary>
    public static IReadOnlyCollection<string> MissingKeys => Missing.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public static void SetLanguage(string? language) =>
        Language = string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ? English : Korean;

    /// <summary>이름이 SunTool 로 시작하는, 현재 로드된 모든 어셈블리의 사전을 읽습니다.</summary>
    public static void RegisterLoadedAssemblies()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name?.StartsWith("SunTool", StringComparison.Ordinal) == true)
            {
                Register(assembly);
            }
        }
    }

    public static void Register(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? string.Empty;
        lock (RegisteredAssemblies)
        {
            if (!RegisteredAssemblies.Add(name))
            {
                return;
            }
        }

        foreach (var resource in assembly.GetManifestResourceNames().Where(r => r.EndsWith(".i18n.en.json", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is not null)
            {
                Load(stream);
            }
        }
    }

    /// <summary>{"한국어": "English"} 형식 JSON 을 읽어 사전에 더합니다 (주석 허용, 빈 값은 무시).</summary>
    public static void Load(Stream json)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        foreach (var (key, value) in map ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrEmpty(value))
            {
                EnglishTexts[key] = value;
            }
        }
    }

    /// <summary>한국어 원문을 현재 언어로 바꿉니다.</summary>
    public static string T(string korean)
    {
        if (!IsEnglish || string.IsNullOrEmpty(korean))
        {
            return korean;
        }

        if (EnglishTexts.TryGetValue(korean, out var english))
        {
            return english;
        }

        Missing.TryAdd(korean, 0);
        return korean;
    }

    /// <summary>서식 문자열 ("{0}개 파일") 을 번역한 뒤 값을 채웁니다.</summary>
    public static string F(string koreanFormat, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(koreanFormat), args);
}
