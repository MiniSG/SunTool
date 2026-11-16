using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SunTool.Modules.RemoteView.Logic;

/// <summary>이 PC 의 네트워크 주소 (바인딩·안내 표시용). 바깥으로 연결하지 않고 어댑터만 읽습니다.</summary>
public static class NetInfo
{
    /// <summary>
    /// 쓸 수 있는 IPv4 주소들 (루프백 제외), 쓸모 있는 순서로.
    /// 실제 LAN·인터넷에 쓰이는 어댑터(기본 게이트웨이 있음)를 앞에 두고,
    /// 가상 어댑터(VMware·Hyper-V·WSL 등)는 뒤로 미룹니다.
    /// </summary>
    public static IReadOnlyList<string> LocalIPv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(n => new Candidate(
                    n.GetIPProperties().UnicastAddresses
                        .Select(a => a.Address)
                        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !IsApipa(a))
                        .ToList(),
                    HasIPv4Gateway(n),
                    LooksVirtual(n.Name) || LooksVirtual(n.Description)))
                .Where(c => c.Addresses.Count > 0)
                .OrderByDescending(c => c.HasGateway)
                .ThenBy(c => c.Virtual)
                .ThenByDescending(c => c.Addresses.Any(IsPrivate))
                .SelectMany(c => c.Addresses.Select(a => a.ToString()))
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>대표 IPv4 (실제 LAN 주소 우선). 없으면 빈 문자열.</summary>
    public static string PrimaryIPv4() => LocalIPv4().FirstOrDefault() ?? string.Empty;

    private sealed record Candidate(List<IPAddress> Addresses, bool HasGateway, bool Virtual);

    private static bool HasIPv4Gateway(NetworkInterface n) =>
        n.GetIPProperties().GatewayAddresses.Any(g =>
            g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

    private static readonly string[] VirtualHints =
        ["virtual", "vmware", "hyper-v", "vethernet", "vbox", "virtualbox", "loopback", "pseudo", "tunnel", "tap", "vpn", "docker", "wsl", "zerotier", "tailscale"];

    private static bool LooksVirtual(string? text) =>
        !string.IsNullOrEmpty(text) && VirtualHints.Any(h => text.Contains(h, StringComparison.OrdinalIgnoreCase));

    /// <summary>169.254.x.x — 주소를 못 받은 어댑터의 링크-로컬(APIPA). 연결에 쓸 수 없습니다.</summary>
    private static bool IsApipa(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }

    private static bool IsPrivate(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }
}
