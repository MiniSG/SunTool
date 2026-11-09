namespace SunTool.Modules.BatchRename.Logic;

/// <summary>텍스트를 넣을 위치.</summary>
public enum TextPosition
{
    /// <summary>이름 앞.</summary>
    Prefix,

    /// <summary>이름 끝.</summary>
    Suffix,

    /// <summary>N번째 글자 위치 (FromEnd 면 끝에서부터).</summary>
    AtIndex,

    /// <summary>특정 문자열 앞.</summary>
    BeforeText,

    /// <summary>특정 문자열 뒤.</summary>
    AfterText,
}

public static class TextPlacement
{
    public static string Insert(
        string name,
        string text,
        TextPosition position,
        int index = 0,
        bool fromEnd = false,
        string? anchor = null,
        bool ignoreCase = true)
    {
        switch (position)
        {
            case TextPosition.Prefix:
                return text + name;

            case TextPosition.Suffix:
                return name + text;

            case TextPosition.AtIndex:
                return name.Insert(ResolveIndex(name.Length, index, fromEnd), text);

            case TextPosition.BeforeText:
            case TextPosition.AfterText:
                if (string.IsNullOrEmpty(anchor))
                {
                    return name;
                }

                var at = name.IndexOf(anchor, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                if (at < 0)
                {
                    return name; // 기준 문자열이 없으면 변경하지 않음
                }

                return name.Insert(position == TextPosition.BeforeText ? at : at + anchor.Length, text);

            default:
                return name;
        }
    }

    /// <summary>범위를 벗어난 위치는 처음/끝으로 맞춥니다.</summary>
    public static int ResolveIndex(int length, int index, bool fromEnd)
    {
        var clamped = Math.Clamp(index, 0, length);
        return fromEnd ? length - clamped : clamped;
    }

    public static string Describe(TextPosition position, int index, bool fromEnd, string? anchor) => position switch
    {
        TextPosition.Prefix => Loc.T("앞"),
        TextPosition.Suffix => Loc.T("끝"),
        TextPosition.AtIndex => fromEnd ? Loc.F("끝에서 {0}번째", index) : Loc.F("{0}번째 글자 위치", index),
        TextPosition.BeforeText => Loc.F("\"{0}\" 앞", anchor),
        TextPosition.AfterText => Loc.F("\"{0}\" 뒤", anchor),
        _ => position.ToString(),
    };
}
