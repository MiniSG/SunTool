using System.Net;

namespace SunTool.Modules.NetScan.Logic;

/// <summary>핑에 응답한 장치 한 대. 모르는 값(MAC·제조사·이름)은 빈 문자열입니다.</summary>
public sealed class ScanRow(IPAddress ip, long responseMs, int? ttl, string mac, string vendor, string hostName)
{
    public string Ip { get; } = ip.ToString();

    /// <summary>IP 를 숫자로 정렬하기 위한 키 (DataGrid SortMemberPath).</summary>
    public uint IpValue { get; } = IpRange.ToUInt(ip);

    public long ResponseMs { get; } = responseMs;

    public int? Ttl { get; } = ttl;

    public string Mac { get; } = mac;

    public string Vendor { get; } = vendor;

    public string HostName { get; } = hostName;

    public string TtlText => Ttl?.ToString() ?? string.Empty;
}
