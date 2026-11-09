namespace SunTool.Core.FileSystem;

/// <summary>Windows 파일 이름 규칙 검사.</summary>
public static class FileNameValidator
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public const int MaxNameLength = 255;

    /// <summary>문제가 없으면 null, 있으면 사용자에게 보여줄 오류 메시지를 반환합니다.</summary>
    public static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Loc.T("이름이 비어 있습니다.");
        }

        if (name.IndexOfAny(InvalidChars) >= 0)
        {
            return Loc.T("사용할 수 없는 문자가 있습니다 ( \\ / : * ? \" < > | ).");
        }

        if (name.EndsWith(' ') || name.EndsWith('.'))
        {
            return Loc.T("이름은 공백이나 마침표로 끝날 수 없습니다.");
        }

        if (name.Length > MaxNameLength)
        {
            return Loc.F("이름이 너무 깁니다 ({0}자, 최대 {1}자).", name.Length, MaxNameLength);
        }

        var stem = name.Split('.')[0].TrimEnd();
        if (ReservedNames.Contains(stem))
        {
            return Loc.F("'{0}'은(는) Windows 예약어라 쓸 수 없습니다.", stem);
        }

        return null;
    }
}
