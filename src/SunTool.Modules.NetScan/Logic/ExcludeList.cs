using System.Net;
using System.Net.Sockets;

namespace SunTool.Modules.NetScan.Logic;

/// <summary>
/// 건너뛸 IP 목록. 쉼표·공백·줄바꿈으로 구분된 IPv4 주소를 읽어 32비트 정수 집합으로 만듭니다.
/// 올바르지 않은 토큰은 조용히 무시합니다.
/// </summary>
public static class ExcludeList
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    public static HashSet<uint> Parse(string? text)
    {
        var set = new HashSet<uint>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return set;
        }

        foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(token, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                set.Add(IpRange.ToUInt(ip));
            }
        }

        return set;
    }
}
