using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SunTool.Modules.SystemInfo.Logic.Providers;

/// <summary>통신: 네트워크 어댑터(비전용 고급 설정 포함), 추가된 COM 포트, 기타 통신 장치.</summary>
public sealed partial class CommunicationInfoProvider : ISystemInfoProvider
{
    private const string NetClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    public string Id => "communication";

    public string Title => "통신";

    public string IconGlyph => "";

    public int Order => 20;

    public ProviderResult Collect(CollectionContext context, CancellationToken cancellationToken)
    {
        var result = new ProviderResult();
        CollectNetwork(context, result);
        cancellationToken.ThrowIfCancellationRequested();
        CollectSerialPorts(context, result);
        CollectOtherDevices(context, result);
        return result;
    }

    // ===== 네트워크 =====

    private static void CollectNetwork(CollectionContext context, ProviderResult result)
    {
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
                .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (NetworkInformationException ex)
        {
            result.Sections.Add(new InfoSection("네트워크", [new InfoItem("읽기 실패", ex.Message, InfoStatus.Error)]));
            return;
        }

        var wired = 0;
        var wiredUp = 0;
        foreach (var adapter in adapters)
        {
            var isWired = adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT;
            var isVirtual = IsVirtual(adapter.Description);
            var isUp = adapter.OperationalStatus == OperationalStatus.Up;
            if (isWired && !isVirtual)
            {
                wired++;
                wiredUp += isUp ? 1 : 0;
            }

            var section = new InfoSection($"네트워크 · {adapter.Name}");
            section.Items.Add(new InfoItem("어댑터", adapter.Description, Detail: isVirtual ? "가상 어댑터" : TypeName(adapter.NetworkInterfaceType)));
            section.Items.Add(new InfoItem("상태", isUp ? "연결됨" : "연결 안 됨", isUp ? InfoStatus.Ok : InfoStatus.None));
            if (isUp && adapter.Speed > 0)
            {
                section.Items.Add(new InfoItem("링크 속도", FormatSpeed(adapter.Speed)));
            }

            section.Items.Add(new InfoItem("MAC 주소", FormatMac(adapter.GetPhysicalAddress())));
            AddIpInfo(adapter, section);

            if (isWired && !isVirtual)
            {
                AddAdvancedSettings(adapter.Id, section);
            }

            result.Sections.Add(section);
        }

        result.Summary.Add(new InfoItem("유선 LAN", $"{wired}개 (연결됨 {wiredUp}개)"));
    }

    private static void AddIpInfo(NetworkInterface adapter, InfoSection section)
    {
        try
        {
            var props = adapter.GetIPProperties();
            var ipv4 = props.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => $"{a.Address}/{a.PrefixLength}")
                .ToList();
            if (ipv4.Count > 0)
            {
                section.Items.Add(new InfoItem("IPv4 주소", string.Join(", ", ipv4)));
            }

            var gateways = props.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Select(g => g.Address.ToString()).ToList();
            if (gateways.Count > 0)
            {
                section.Items.Add(new InfoItem("게이트웨이", string.Join(", ", gateways)));
            }

            var dns = props.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString()).ToList();
            if (dns.Count > 0)
            {
                section.Items.Add(new InfoItem("DNS", string.Join(", ", dns)));
            }

            var v4 = props.GetIPv4Properties();
            if (v4 is not null)
            {
                section.Items.Add(new InfoItem("IP 설정", v4.IsDhcpEnabled ? "자동 (DHCP)" : "고정 IP"));
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            // IPv4 가 없는 어댑터 등 — 표시할 것만 표시
        }
    }

    /// <summary>GigE 카메라에 중요한 점보 프레임·수신 버퍼 설정 (레지스트리의 어댑터 고급 속성).</summary>
    private static void AddAdvancedSettings(string adapterId, InfoSection section)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(NetClassKey);
            if (classKey is null)
            {
                return;
            }

            foreach (var name in classKey.GetSubKeyNames())
            {
                RegistryKey? key;
                try
                {
                    key = classKey.OpenSubKey(name);
                }
                catch (System.Security.SecurityException)
                {
                    continue;
                }

                using (key)
                {
                    if (key is null || !string.Equals(key.GetValue("NetCfgInstanceId") as string, adapterId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (key.GetValue("*JumboPacket") is string jumboText && int.TryParse(jumboText, out var jumbo))
                    {
                        section.Items.Add(jumbo > 1514
                            ? new InfoItem("점보 프레임", $"사용 ({jumbo} bytes)", InfoStatus.Ok)
                            : new InfoItem("점보 프레임", $"사용 안 함 ({jumbo} bytes)", InfoStatus.Warning, "GigE 카메라를 연결하는 포트라면 9014 권장"));
                    }

                    if (key.GetValue("*ReceiveBuffers") is string buffers)
                    {
                        section.Items.Add(new InfoItem("수신 버퍼", buffers, Detail: "GigE 카메라용 포트는 최대값 권장"));
                    }

                    return;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // 고급 설정은 못 읽어도 나머지는 표시
        }
    }

    // ===== 시리얼 포트 =====

    private static void CollectSerialPorts(CollectionContext context, ProviderResult result)
    {
        var section = new InfoSection("시리얼 포트 (COM)")
        {
            Note = "메인보드 내장 포트 외에 USB 변환기·PCI 확장 카드로 추가된 포트인지 표시합니다. (포트를 열어 보지는 않습니다)",
        };

        var ports = new List<SerialPortInfo>();
        foreach (var device in context.PnpDevices.Where(d => string.Equals(d.PnpClass, "Ports", StringComparison.OrdinalIgnoreCase)))
        {
            var match = ComNameRegex().Match(device.Name);
            if (match.Success)
            {
                ports.Add(new SerialPortInfo(match.Groups[1].Value.ToUpperInvariant(), device.Name, ClassifyPort(device.DeviceId), device));
            }
        }

        var systemPorts = ReadSerialCommPorts();

        foreach (var name in systemPorts.Where(n => !ports.Any(p => string.Equals(p.Port, n, StringComparison.OrdinalIgnoreCase))))
        {
            ports.Add(new SerialPortInfo(name.ToUpperInvariant(), "(장치 이름 없음)", SerialPortKind.Virtual, null));
        }

        ports = ports.OrderBy(p => int.TryParse(p.Port.AsSpan(3), out var n) ? n : 999).ToList();
        foreach (var port in ports)
        {
            var (status, stateText) = port.Device is null ? (InfoStatus.None, string.Empty) : PnpDeviceReader.DescribeState(port.Device);
            var added = port.Kind != SerialPortKind.Builtin;
            section.Items.Add(new InfoItem(
                port.Port,
                $"{KindText(port.Kind)}{(added ? " — 추가된 포트" : string.Empty)}",
                port.Device is not null && status != InfoStatus.Ok ? status : added ? InfoStatus.Installed : InfoStatus.None,
                $"{port.Description}{(stateText.Length > 0 && status != InfoStatus.Ok ? $" · {stateText}" : string.Empty)}"));
        }

        var addedPorts = ports.Where(p => p.Kind != SerialPortKind.Builtin).Select(p => p.Port).ToList();
        var builtin = ports.Count - addedPorts.Count;
        if (ports.Count == 0)
        {
            section.Items.Add(new InfoItem("COM 포트", "없음", InfoStatus.NotInstalled));
        }

        result.Sections.Add(section);
        result.Summary.Add(addedPorts.Count > 0
            ? new InfoItem("추가된 COM 포트", $"{addedPorts.Count}개 ({string.Join(", ", addedPorts)})", InfoStatus.Installed, builtin > 0 ? $"내장 포트 {builtin}개 별도" : null)
            : new InfoItem("추가된 COM 포트", "없음", InfoStatus.NotInstalled, builtin > 0 ? $"내장 포트 {builtin}개" : null));
    }

    /// <summary>Windows 가 현재 인식한 COM 포트 이름 (SerialPort.GetPortNames 와 같은 출처).</summary>
    private static string[] ReadSerialCommPorts()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            return key?.GetValueNames().Select(n => key.GetValue(n) as string).OfType<string>().ToArray() ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return Array.Empty<string>();
        }
    }

    public static SerialPortKind ClassifyPort(string deviceId)
    {
        var prefix = deviceId.Split('\\', 2)[0].ToUpperInvariant();
        return prefix switch
        {
            "ACPI" => SerialPortKind.Builtin,
            "USB" or "FTDIBUS" or "USBSER" or "SILABSER" => SerialPortKind.Usb,
            "PCI" or "MF" or "MXPCIE" or "MOXA" => SerialPortKind.PciCard,
            "BTHENUM" => SerialPortKind.Bluetooth,
            _ => SerialPortKind.Virtual,
        };
    }

    private static string KindText(SerialPortKind kind) => kind switch
    {
        SerialPortKind.Builtin => "메인보드 내장",
        SerialPortKind.Usb => "USB 변환",
        SerialPortKind.PciCard => "PCI/PCIe 확장 카드",
        SerialPortKind.Bluetooth => "블루투스",
        _ => "가상/기타",
    };

    // ===== 기타 통신 장치 =====

    private static void CollectOtherDevices(CollectionContext context, ProviderResult result)
    {
        var devices = context.PnpDevices
            .Where(d => OtherCommRegex().IsMatch(d.Name) || OtherCommRegex().IsMatch(d.Manufacturer))
            .Where(d => !string.Equals(d.PnpClass, "Net", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (devices.Count == 0)
        {
            return; // 없으면 카드 자체를 생략
        }

        var section = new InfoSection("기타 통신 · IO 장치")
        {
            Note = "GPIB, NI DAQ, EtherCAT, CAN, 모션·디지털 IO 보드 등을 장치 이름으로 찾은 결과입니다.",
        };
        foreach (var device in devices)
        {
            var (status, text) = PnpDeviceReader.DescribeState(device);
            section.Items.Add(new InfoItem(device.Name, $"{PnpDeviceReader.BusOf(device.DeviceId)} · {ValueText.OrDash(device.Manufacturer)}", status, text));
        }

        result.Sections.Add(section);
    }

    private static bool IsVirtual(string description) => VirtualRegex().IsMatch(description);

    private static string TypeName(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "유선 (이더넷)",
        NetworkInterfaceType.Wireless80211 => "무선 (Wi-Fi)",
        _ => type.ToString(),
    };

    private static string FormatSpeed(long bitsPerSecond) =>
        bitsPerSecond >= 1_000_000_000 ? $"{bitsPerSecond / 1e9:0.#} Gbps" : $"{bitsPerSecond / 1e6:0} Mbps";

    private static string FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? "-" : string.Join("-", bytes.Select(b => b.ToString("X2")));
    }

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ComNameRegex();

    [GeneratedRegex(@"Virtual|Hyper-V|VMware|VirtualBox|Loopback|TAP-|WAN Miniport|Bluetooth|Wi-Fi Direct|Npcap", RegexOptions.IgnoreCase)]
    private static partial Regex VirtualRegex();

    [GeneratedRegex(@"GPIB|NI[- ]?DAQ|NI PCI|NI PCIe|NI USB-|EtherCAT|PROFINET|DeviceNet|\bCAN\b|Kvaser|PEAK|Ajinextek|\bAXL\b|Comizoa|Advantech|Adlink|Digital I/?O|Motion Control|Moxa|IXXAT", RegexOptions.IgnoreCase)]
    private static partial Regex OtherCommRegex();

    private sealed record SerialPortInfo(string Port, string Description, SerialPortKind Kind, PnpDevice? Device);
}

public enum SerialPortKind
{
    Builtin,
    Usb,
    PciCard,
    Bluetooth,
    Virtual,
}
