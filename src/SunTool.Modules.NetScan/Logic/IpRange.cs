using System.Net;
using System.Net.Sockets;

namespace SunTool.Modules.NetScan.Logic;

/// <summary>
/// 핑으로 훑을 IPv4 주소 범위. CIDR (192.168.0.0/24) 또는 시작–끝 (192.168.0.1-192.168.0.50,
/// 192.168.0.1-50) 을 받습니다. 주소는 32비트 정수로 다룹니다 (네트워크 바이트 순서).
/// </summary>
public sealed record IpRange
{
    /// <summary>범위 크기 상한 (/16 = 65,536개). 더 크면 거부합니다.</summary>
    public const long MaxAddresses = 65536;

    private IpRange(uint start, uint end)
    {
        Start = start;
        End = end;
    }

    public uint Start { get; }

    public uint End { get; }

    public long Count => (long)End - Start + 1;

    /// <summary>범위 안의 모든 IPv4 주소 (시작·끝 포함).</summary>
    public IEnumerable<IPAddress> Addresses()
    {
        for (var value = Start; ; value++)
        {
            yield return ToIp(value);
            if (value == End)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// 입력을 범위로 바꿉니다. 실패하면 false 와 사람이 읽을 오류 문구를 돌려줍니다.
    /// 성공해도 범위가 <see cref="MaxAddresses"/> 보다 크면 거부합니다.
    /// </summary>
    public static bool TryParse(string? input, out IpRange? range, out string? error)
    {
        range = null;
        error = null;
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            error = Loc.T("IP 범위를 입력하세요. 예) 192.168.0.0/24 또는 192.168.0.1-192.168.0.50");
            return false;
        }

        uint start;
        uint end;
        if (text.Contains('/'))
        {
            if (!TryParseCidr(text, out start, out end, out error))
            {
                return false;
            }
        }
        else if (text.Contains('-'))
        {
            if (!TryParseDashRange(text, out start, out end, out error))
            {
                return false;
            }
        }
        else if (TryParseIp(text, out var single))
        {
            start = single;
            end = single;
        }
        else
        {
            error = Loc.F("IP 범위를 이해할 수 없습니다: {0}", text);
            return false;
        }

        if (start > end)
        {
            error = Loc.T("시작 주소가 끝 주소보다 큽니다.");
            return false;
        }

        var count = (long)end - start + 1;
        if (count > MaxAddresses)
        {
            error = Loc.F("범위가 너무 큽니다 ({0:N0}개). 한 번에 최대 {1:N0}개 (/16) 까지만 됩니다.", count, MaxAddresses);
            return false;
        }

        range = new IpRange(start, end);
        return true;
    }

    private static bool TryParseCidr(string text, out uint start, out uint end, out string? error)
    {
        start = 0;
        end = 0;
        error = null;
        var parts = text.Split('/', 2);
        if (!TryParseIp(parts[0], out var baseIp))
        {
            error = Loc.F("IP 주소를 이해할 수 없습니다: {0}", parts[0]);
            return false;
        }

        if (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 32)
        {
            error = Loc.F("프리픽스 길이가 올바르지 않습니다 (0~32): {0}", parts[1]);
            return false;
        }

        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        start = baseIp & mask;
        end = start | ~mask;
        return true;
    }

    private static bool TryParseDashRange(string text, out uint start, out uint end, out string? error)
    {
        start = 0;
        end = 0;
        error = null;
        var parts = text.Split('-', 2);
        var left = parts[0].Trim();
        var right = parts[1].Trim();
        if (!TryParseIp(left, out start))
        {
            error = Loc.F("시작 IP 주소를 이해할 수 없습니다: {0}", left);
            return false;
        }

        // 짧은 형태: 192.168.0.1-50 (마지막 옥텟만). 점이 없으면 옥텟으로 봅니다.
        if (!right.Contains('.'))
        {
            if (byte.TryParse(right, out var lastOctet))
            {
                end = (start & 0xFFFFFF00u) | lastOctet;
                return true;
            }

            error = Loc.F("끝 IP 주소를 이해할 수 없습니다: {0}", right);
            return false;
        }

        if (TryParseIp(right, out end))
        {
            return true;
        }

        error = Loc.F("끝 IP 주소를 이해할 수 없습니다: {0}", right);
        return false;
    }

    private static bool TryParseIp(string text, out uint value)
    {
        value = 0;
        if (!IPAddress.TryParse(text.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        value = ToUInt(ip);
        return true;
    }

    /// <summary>IPv4 주소를 네트워크 바이트 순서의 32비트 정수로.</summary>
    public static uint ToUInt(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    public static IPAddress ToIp(uint value) =>
        new(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
}
