using System.Globalization;
using System.Text;

namespace SunTool.Modules.Comm.Logic;

/// <summary>
/// 바이트를 글자로 바꾼 결과 + 글자마다 원래 바이트 위치.
/// 검색어가 텍스트로 일치한 위치를 바이트로 되돌려, 텍스트 줄과 HEX 줄을 함께 하이라이트하는 데 씁니다.
/// Text 는 <see cref="ByteCodec.ToDisplayText"/> 와 같습니다 (제어 문자는 &lt;STX&gt;).
/// </summary>
public sealed class DisplayMap
{
    private DisplayMap(string text, int[] byteIndex, bool[] isControl)
    {
        Text = text;
        ByteIndex = byteIndex;
        IsControl = isControl;
    }

    public string Text { get; }

    /// <summary>글자 i 가 나온 바이트 위치.</summary>
    public int[] ByteIndex { get; }

    /// <summary>글자 i 가 제어 문자 이름(&lt;STX&gt;)의 일부인지.</summary>
    public bool[] IsControl { get; }

    public static DisplayMap Build(ReadOnlySpan<byte> data, TextEncodingKind encoding)
    {
        var text = new StringBuilder(data.Length);
        var index = new List<int>(data.Length);
        var control = new List<bool>(data.Length);

        void Add(string s, int byteIndex, bool isControl)
        {
            foreach (var c in s)
            {
                text.Append(c);
                index.Add(byteIndex);
                control.Add(isControl);
            }
        }

        var decoder = encoding == TextEncodingKind.Ascii ? null : ByteCodec.GetEncoding(encoding).GetDecoder();
        var pendingStart = -1; // 여러 바이트 글자(UTF-8, EUC-KR)의 첫 바이트 위치
        var chars = new char[4];

        void FlushDecoder(int lastIndex)
        {
            if (decoder is null || pendingStart < 0)
            {
                return;
            }

            var n = decoder.GetChars([], 0, 0, chars, 0, flush: true);
            Add(Clean(new string(chars, 0, n)), pendingStart, false);
            pendingStart = -1;
            decoder.Reset();
        }

        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if (ByteCodec.ControlName(b) is { } name)
            {
                FlushDecoder(i - 1);
                Add($"<{name}>", i, true);
                continue;
            }

            if (decoder is null)
            {
                Add(b < 0x80 ? ((char)b).ToString() : "·", i, false);
                continue;
            }

            if (pendingStart < 0)
            {
                pendingStart = i;
            }

            var produced = decoder.GetChars([b], 0, 1, chars, 0, flush: false);
            if (produced > 0)
            {
                Add(Clean(new string(chars, 0, produced)), pendingStart, false);
                pendingStart = -1;
            }
        }

        FlushDecoder(data.Length - 1);
        return new DisplayMap(text.ToString(), [.. index], [.. control]);
    }

    private static string Clean(string s) => s.Replace('�', '·');
}

/// <summary>검색어가 로그 데이터의 어느 바이트와 일치하는지 찾습니다 (텍스트 일치 + HEX 일치).</summary>
public static class SearchMatcher
{
    /// <summary>
    /// 일치하는 바이트 표시 (길이 = data.Length). 일치가 없으면 null.
    /// 텍스트: 표시 글자(&lt;STX&gt; 포함)에서 대소문자 무시로 찾음.
    /// HEX: 검색어가 16진수(공백 무시, 짝수 자리, 2자리 이상)면 바이트 단위로 찾음.
    /// </summary>
    public static bool[]? FindBytes(ReadOnlySpan<byte> data, string? query, TextEncodingKind encoding)
    {
        if (string.IsNullOrWhiteSpace(query) || data.IsEmpty)
        {
            return null;
        }

        query = query.Trim();
        bool[]? mask = null;
        var length = data.Length;
        void Mark(int start, int endInclusive)
        {
            mask ??= new bool[length];
            for (var i = start; i <= endInclusive; i++)
            {
                mask[i] = true;
            }
        }

        // 1) 텍스트
        var map = DisplayMap.Build(data, encoding);
        foreach (var (start, count) in FindText(map.Text, query))
        {
            Mark(map.ByteIndex[start], map.ByteIndex[start + count - 1]);
        }

        // 2) HEX
        if (TryParseHexQuery(query, out var pattern))
        {
            for (var i = 0; i + pattern.Length <= data.Length; i++)
            {
                if (data.Slice(i, pattern.Length).SequenceEqual(pattern))
                {
                    Mark(i, i + pattern.Length - 1);
                }
            }
        }

        return mask;
    }

    /// <summary>일반 문자열에서 일치 위치 (대소문자 무시, 겹치지 않게).</summary>
    public static List<(int Start, int Length)> FindText(string? text, string? query)
    {
        var result = new List<(int, int)>();
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        query = query.Trim();
        var from = 0;
        while (from <= text.Length - query.Length)
        {
            var at = text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                break;
            }

            result.Add((at, query.Length));
            from = at + query.Length;
        }

        return result;
    }

    /// <summary>"03 0D", "030d" → [0x03, 0x0D]. 16진수가 아니거나 홀수 자리면 false.</summary>
    public static bool TryParseHexQuery(string query, out byte[] bytes)
    {
        bytes = [];
        var compact = query.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compact.Length < 2 || compact.Length % 2 != 0 || !compact.All(Uri.IsHexDigit))
        {
            return false;
        }

        bytes = new byte[compact.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(compact.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return true;
    }
}
