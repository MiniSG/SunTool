using System.Globalization;
using System.Text;

namespace SunTool.Modules.Comm.Logic;

/// <summary>보내기 입력 방식.</summary>
public enum InputMode
{
    /// <summary>텍스트 (\r \n \t \\ \xHH 해석 가능).</summary>
    Text,

    /// <summary>HEX ("02 4F 4B", "024F4B", "0x02,0x4F" 모두 허용).</summary>
    Hex,
}

/// <summary>문자 인코딩 선택지.</summary>
public enum TextEncodingKind
{
    Ascii,
    Utf8,
    EucKr,
}

/// <summary>바이트 ↔ 문자열 변환 (보내기 해석, 화면 표시, 로그 저장).</summary>
public static class ByteCodec
{
    static ByteCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>제어 문자 이름 (화면에 ⟨STX⟩ 처럼 표시).</summary>
    private static readonly string[] ControlNames =
    [
        "NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "TAB", "LF", "VT", "FF", "CR", "SO", "SI",
        "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US",
    ];

    public static Encoding GetEncoding(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => new UTF8Encoding(false),
        TextEncodingKind.EucKr => Encoding.GetEncoding(949),
        _ => Encoding.Latin1, // ASCII 범위 밖 바이트도 1:1 로 보존
    };

    /// <summary>제어 문자 이름. 0x00~0x1F, 0x7F 만 해당, 그 밖은 null.</summary>
    public static string? ControlName(byte value) => value switch
    {
        < 0x20 => ControlNames[value],
        0x7F => "DEL",
        _ => null,
    };

    // ===================================================================== 보내기 해석

    /// <summary>HEX 문자열 → 바이트. 공백·쉼표·콜론·하이픈·0x 접두어는 구분자로 취급합니다.</summary>
    /// <exception cref="FormatException">16진수가 아닌 글자가 있거나 자릿수가 홀수일 때.</exception>
    public static byte[] ParseHex(string text)
    {
        var bytes = new List<byte>();
        foreach (var raw in text.Split([' ', ',', ':', '-', '\t', '\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw;
            if (token.Length == 0)
            {
                continue;
            }

            if (token.Length % 2 != 0)
            {
                // "2 4F" 처럼 한 자리만 쓴 경우는 앞에 0 을 붙여 허용 (단, 공백으로 나뉜 한 글자 토큰만)
                if (token.Length == 1)
                {
                    token = "0" + token;
                }
                else
                {
                    throw new FormatException(Loc.F("HEX 자릿수가 홀수입니다: '{0}'", raw));
                }
            }

            for (var i = 0; i < token.Length; i += 2)
            {
                if (!byte.TryParse(token.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                {
                    throw new FormatException(Loc.F("16진수가 아닌 글자가 있습니다: '{0}'", raw));
                }

                bytes.Add(b);
            }
        }

        return [.. bytes];
    }

    /// <summary>텍스트 → 바이트. escapes 가 true 면 \r \n \t \0 \\ \xHH 를 해석합니다.</summary>
    public static byte[] EncodeText(string text, TextEncodingKind encoding, bool escapes)
    {
        var enc = GetEncoding(encoding);
        if (!escapes || !text.Contains('\\'))
        {
            return enc.GetBytes(text);
        }

        var result = new List<byte>(text.Length);
        var pending = new StringBuilder();
        void Flush()
        {
            if (pending.Length > 0)
            {
                result.AddRange(enc.GetBytes(pending.ToString()));
                pending.Clear();
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i == text.Length - 1)
            {
                pending.Append(c);
                continue;
            }

            var next = text[i + 1];
            byte? escaped = next switch
            {
                'r' => 0x0D,
                'n' => 0x0A,
                't' => 0x09,
                '0' => 0x00,
                '\\' => (byte)'\\',
                _ => null,
            };

            if (escaped is { } e)
            {
                Flush();
                result.Add(e);
                i++;
            }
            else if ((next == 'x' || next == 'X') && i + 3 < text.Length
                     && byte.TryParse(text.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                Flush();
                result.Add(hex);
                i += 3;
            }
            else
            {
                pending.Append(c);
            }
        }

        Flush();
        return [.. result];
    }

    // ===================================================================== 표시

    /// <summary>"02 4F 4B  0D 0A" (groupSize 바이트마다 공백 하나 더).</summary>
    public static string ToHex(ReadOnlySpan<byte> data, int groupSize = 8)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(data.Length * 3 + data.Length / Math.Max(1, groupSize));
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(groupSize > 0 && i % groupSize == 0 ? "  " : " ");
            }

            sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>표시용 텍스트 조각. IsControl 이면 ⟨이름⟩ 으로 강조해서 그립니다.</summary>
    public readonly record struct TextPiece(string Text, bool IsControl);

    /// <summary>바이트 → 표시용 조각 (제어 문자는 이름, 해석 불가 바이트는 ·).</summary>
    public static List<TextPiece> ToTextPieces(ReadOnlySpan<byte> data, TextEncodingKind encoding)
    {
        var pieces = new List<TextPiece>();
        var start = 0;
        for (var i = 0; i <= data.Length; i++)
        {
            var name = i < data.Length ? ControlName(data[i]) : null;
            if (i < data.Length && name is null)
            {
                continue;
            }

            if (i > start)
            {
                pieces.Add(new TextPiece(Decode(data[start..i], encoding), false));
            }

            if (name is not null)
            {
                pieces.Add(new TextPiece(name, true));
            }

            start = i + 1;
        }

        return pieces;
    }

    /// <summary>한 줄 문자열 (로그 저장·검색용). 제어 문자는 &lt;CR&gt; 처럼 씁니다.</summary>
    public static string ToDisplayText(ReadOnlySpan<byte> data, TextEncodingKind encoding)
    {
        var sb = new StringBuilder(data.Length);
        foreach (var piece in ToTextPieces(data, encoding))
        {
            sb.Append(piece.IsControl ? $"<{piece.Text}>" : piece.Text);
        }

        return sb.ToString();
    }

    private static string Decode(ReadOnlySpan<byte> data, TextEncodingKind encoding)
    {
        if (encoding == TextEncodingKind.Ascii)
        {
            // ASCII 모드: 0x80 이상은 · 로 표시
            var chars = new char[data.Length];
            for (var i = 0; i < data.Length; i++)
            {
                chars[i] = data[i] < 0x80 ? (char)data[i] : '·';
            }

            return new string(chars);
        }

        return GetEncoding(encoding).GetString(data).Replace('�', '·');
    }

    /// <summary>16바이트 단위 덤프: "0000  02 52 45 ...   .REA..".</summary>
    public static string ToHexDump(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            var line = data.Slice(offset, Math.Min(16, data.Length - offset));
            sb.Append(offset.ToString("X4", CultureInfo.InvariantCulture)).Append("  ");
            var hex = ToHex(line, 8);
            sb.Append(hex.PadRight(16 * 3 + 1)).Append("  ");
            foreach (var b in line)
            {
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '·');
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
