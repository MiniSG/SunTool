using System.Globalization;

namespace SunTool.Modules.ScreenCapture.Logic;

/// <summary>저장 파일 이름 만들기 (순수 로직, 테스트 대상). 패턴은 .NET 날짜 서식 문자열입니다.</summary>
public static class CaptureFileName
{
    public const string DefaultPattern = "'capture-'yyyyMMdd-HHmmss";

    /// <summary>패턴에 쓸 수 있는 토큰 키. 값은 <see cref="Format"/> 에 넘기는 사전에서 옵니다.</summary>
    public static readonly IReadOnlyList<string> TokenKeys = ["host", "ip", "user"];

    /// <summary>
    /// 패턴과 시각으로 PNG 파일 이름을 만듭니다. 빈 패턴이면 기본값을 씁니다.
    /// <paramref name="tokens"/> 가 있으면 패턴 속 <c>{host}·{ip}·{user}</c> 같은 토큰을 그 값으로 바꿉니다.
    /// </summary>
    public static string Format(string? pattern, DateTime now, IReadOnlyDictionary<string, string>? tokens = null)
    {
        var p = string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern.Trim();

        if (tokens is not null)
        {
            foreach (var (key, value) in tokens)
            {
                p = ReplaceToken(p, key, value);
            }
        }

        string name;
        try
        {
            name = now.ToString(p, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            name = now.ToString(DefaultPattern, CultureInfo.InvariantCulture);
        }

        name = Sanitize(name);
        if (name.Length == 0)
        {
            name = now.ToString(DefaultPattern, CultureInfo.InvariantCulture);
        }

        return name + ".png";
    }

    /// <summary>패턴 속 <c>{key}</c> 를 값으로 바꿉니다. 날짜 서식이 건드리지 않도록 리터럴(작은따옴표)로 감쌉니다.</summary>
    private static string ReplaceToken(string pattern, string key, string value)
    {
        var token = "{" + key + "}";
        var literal = "'" + value.Replace("'", "''") + "'";
        return pattern.Replace(token, literal, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>같은 이름이 이미 있으면 뒤에 -2, -3 … 을 붙여 겹치지 않는 이름을 만듭니다.</summary>
    public static string Unique(string fileName, Func<string, bool> exists)
    {
        if (!exists(fileName))
        {
            return fileName;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; i < 10000; i++)
        {
            var candidate = $"{stem}-{i}{ext}";
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        return $"{stem}-{Guid.NewGuid():N}{ext}";
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Where(c => Array.IndexOf(invalid, c) < 0).ToArray();
        return new string(chars).Trim().TrimEnd('.');
    }
}
