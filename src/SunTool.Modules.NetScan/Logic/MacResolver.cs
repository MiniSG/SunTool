using System.Net;
using System.Runtime.InteropServices;

namespace SunTool.Modules.NetScan.Logic;

/// <summary>
/// 같은 서브넷 장치의 MAC 주소를 읽습니다. iphlpapi 의 SendARP (링크 계층 ARP 조회) 를 먼저 쓰고,
/// 안 되면 운영체제 ARP 캐시(<c>arp -a</c>) 표를 봅니다. 대상에 포트를 열거나 페이로드를 보내지 않습니다.
/// </summary>
public static class MacResolver
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint macAddrLen);

    /// <summary>IP 의 MAC 을 구합니다. 못 구하면 빈 문자열. <paramref name="arpFallback"/> 는 미리 읽어 둔 ARP 캐시 표.</summary>
    public static string Resolve(IPAddress ip, IReadOnlyDictionary<string, string>? arpFallback = null)
    {
        var mac = TrySendArp(ip);
        if (!string.IsNullOrEmpty(mac))
        {
            return mac;
        }

        if (arpFallback is not null && arpFallback.TryGetValue(ip.ToString(), out var cached))
        {
            return cached;
        }

        return string.Empty;
    }

    private static string TrySendArp(IPAddress ip)
    {
        try
        {
            // SendARP 는 네트워크 바이트 순서의 little-endian uint 를 받습니다 (GetAddressBytes 순서 그대로).
            var bytes = ip.GetAddressBytes();
            var dest = (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
            var mac = new byte[6];
            var len = (uint)mac.Length;
            if (SendARP(dest, 0, mac, ref len) != 0 || len < 6)
            {
                return string.Empty;
            }

            if (mac.All(b => b == 0))
            {
                return string.Empty;
            }

            return ArpTable.NormalizeMac(string.Concat(mac.Select(b => b.ToString("X2")))) ?? string.Empty;
        }
        catch (DllNotFoundException)
        {
            return string.Empty;
        }
        catch (EntryPointNotFoundException)
        {
            return string.Empty;
        }
    }
}
