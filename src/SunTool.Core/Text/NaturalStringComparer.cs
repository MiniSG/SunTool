namespace SunTool.Core.Text;

/// <summary>
/// 사람이 기대하는 순서로 정렬하는 비교자: "file2" &lt; "file10".
/// 숫자 구간은 수 크기로, 나머지 구간은 현재 문화권 기준 대소문자 무시로 비교합니다.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            var xDigit = char.IsAsciiDigit(x[i]);
            var yDigit = char.IsAsciiDigit(y[j]);
            var xChunk = ReadChunk(x, ref i, xDigit);
            var yChunk = ReadChunk(y, ref j, yDigit);

            int result;
            if (xDigit && yDigit)
            {
                result = CompareNumeric(xChunk, yChunk);
            }
            else
            {
                result = string.Compare(xChunk.ToString(), yChunk.ToString(), StringComparison.CurrentCultureIgnoreCase);
            }

            if (result != 0)
            {
                return result;
            }
        }

        var remaining = (x.Length - i).CompareTo(y.Length - j);
        return remaining != 0 ? remaining : string.CompareOrdinal(x, y);
    }

    private static ReadOnlySpan<char> ReadChunk(string s, ref int index, bool digits)
    {
        var start = index;
        while (index < s.Length && char.IsAsciiDigit(s[index]) == digits)
        {
            index++;
        }

        return s.AsSpan(start, index - start);
    }

    private static int CompareNumeric(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var ta = a.TrimStart('0');
        var tb = b.TrimStart('0');
        if (ta.Length != tb.Length)
        {
            return ta.Length.CompareTo(tb.Length);
        }

        var c = ta.SequenceCompareTo(tb);
        if (c != 0)
        {
            return c;
        }

        // 같은 수라면 앞자리 0이 적은 쪽을 먼저 ("1" < "01")
        return a.Length.CompareTo(b.Length);
    }
}
