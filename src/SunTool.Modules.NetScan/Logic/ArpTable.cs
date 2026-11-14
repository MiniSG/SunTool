using System.Text.RegularExpressions;

namespace SunTool.Modules.NetScan.Logic;

/// <summary>
/// 운영체제의 ARP 캐시(<c>arp -a</c> 출력)를 읽어 IP → MAC 표를 만듭니다. 순수 파서라 테스트하기 쉽습니다.
/// MAC 은 대문자·콜론 구분(AA:BB:CC:DD:EE:FF)으로 통일합니다.
/// </summary>
public static partial class ArpTable
{
    [GeneratedRegex(@"(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9A-Fa-f]{2}(?:[-:][0-9A-Fa-f]{2}){5})", RegexOptions.CultureInvariant)]
    private static partial Regex RowRegex();

    /// <summary><c>arp -a</c> 출력 문자열을 IP → MAC 사전으로 바꿉니다.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string? arpOutput)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(arpOutput))
        {
            return map;
        }

        foreach (Match m in RowRegex().Matches(arpOutput))
        {
            var ip = m.Groups[1].Value;
            var mac = NormalizeMac(m.Groups[2].Value);
            if (mac is not null)
            {
                map[ip] = mac;
            }
        }

        return map;
    }

    /// <summary>MAC 을 대문자·콜론 구분으로 통일합니다. 6바이트가 아니면 null.</summary>
    public static string? NormalizeMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return null;
        }

        var hex = new List<char>(12);
        foreach (var c in mac)
        {
            if (Uri.IsHexDigit(c))
            {
                hex.Add(char.ToUpperInvariant(c));
            }
        }

        if (hex.Count != 12)
        {
            return null;
        }

        return string.Join(':', Enumerable.Range(0, 6).Select(i => new string(new[] { hex[i * 2], hex[i * 2 + 1] })));
    }
}
