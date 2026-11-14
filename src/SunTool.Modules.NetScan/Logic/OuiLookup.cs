namespace SunTool.Modules.NetScan.Logic;

/// <summary>
/// MAC 주소 앞 3바이트(OUI)로 제조사를 찾습니다. 전체 IEEE 목록이 아니라 자주 보이는
/// 제조사만 추린 작은 내장 표입니다. 못 찾으면 빈 문자열을 돌려줍니다.
/// </summary>
public static class OuiLookup
{
    // 앞 3바이트(6 hex, 대문자, 구분자 없음) → 제조사.
    private static readonly IReadOnlyDictionary<string, string> Vendors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["000C29"] = "VMware",
        ["005056"] = "VMware",
        ["000569"] = "VMware",
        ["001C14"] = "VMware",
        ["080027"] = "VirtualBox (Oracle)",
        ["0003FF"] = "Microsoft (Hyper-V)",
        ["00155D"] = "Microsoft (Hyper-V)",
        ["525400"] = "QEMU/KVM",
        ["001A11"] = "Google",
        ["3C5AB4"] = "Google",
        ["F4F5E8"] = "Google",
        ["DCA632"] = "Raspberry Pi",
        ["B827EB"] = "Raspberry Pi",
        ["E45F01"] = "Raspberry Pi",
        ["28CDC1"] = "Raspberry Pi",
        ["D83ADD"] = "Raspberry Pi",
        ["001D0F"] = "TP-Link",
        ["5091E3"] = "TP-Link",
        ["F4EC38"] = "TP-Link",
        ["AC84C6"] = "TP-Link",
        ["0018E7"] = "Netgear",
        ["A040A0"] = "Netgear",
        ["001F33"] = "Netgear",
        ["002275"] = "Belkin",
        ["000D4B"] = "D-Link",
        ["001195"] = "D-Link",
        ["14D64D"] = "D-Link",
        ["0017A4"] = "ASUSTek",
        ["2C56DC"] = "ASUSTek",
        ["AC220B"] = "ASUSTek",
        ["001A2B"] = "Cisco",
        ["00000C"] = "Cisco",
        ["0050BA"] = "Cisco",
        ["F8E71E"] = "Ubiquiti",
        ["24A43C"] = "Ubiquiti",
        ["788A20"] = "Ubiquiti",
        ["001517"] = "Intel",
        ["001B21"] = "Intel",
        ["3C9709"] = "Intel",
        ["A0C589"] = "Intel",
        ["00E04C"] = "Realtek",
        ["52544C"] = "Realtek",
        ["000EC6"] = "Realtek",
        ["001632"] = "Samsung",
        ["002490"] = "Samsung",
        ["5CF6DC"] = "Samsung",
        ["F80377"] = "Samsung",
        ["F0D1A9"] = "Apple",
        ["ACBC32"] = "Apple",
        ["3C0754"] = "Apple",
        ["A4C361"] = "Apple",
        ["D0817A"] = "Apple",
        ["F02475"] = "Apple",
        ["001D7E"] = "Linksys",
        ["48F8B3"] = "Linksys",
        ["001E58"] = "WD (Western Digital)",
        ["0090A9"] = "WD (Western Digital)",
        ["00904C"] = "Epson",
        ["002655"] = "Epson",
        ["002136"] = "HP",
        ["3CD92B"] = "HP",
        ["9CB654"] = "HP",
        ["F4CE46"] = "HP",
        ["000874"] = "Dell",
        ["18A99B"] = "Dell",
        ["D067E5"] = "Dell",
        ["001124"] = "Lenovo",
        ["54EE75"] = "Lenovo",
        ["8C1645"] = "Xiaomi",
        ["640980"] = "Xiaomi",
        ["3CBD3E"] = "Xiaomi",
        ["00166F"] = "Sony",
        ["FC0FE6"] = "Sony",
        ["ECFABC"] = "Amazon",
        ["44650D"] = "Amazon",
        ["F0272D"] = "Amazon",
    };

    /// <summary>MAC 주소에서 제조사를 찾습니다. 못 찾으면 빈 문자열.</summary>
    public static string VendorFor(string? mac)
    {
        var prefix = PrefixOf(mac);
        return prefix is not null && Vendors.TryGetValue(prefix, out var vendor) ? vendor : string.Empty;
    }

    /// <summary>MAC 문자열에서 앞 3바이트(대문자 6 hex)를 뽑습니다. 자리수가 모자라면 null.</summary>
    public static string? PrefixOf(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return null;
        }

        var hex = new char[6];
        var count = 0;
        foreach (var c in mac)
        {
            if (Uri.IsHexDigit(c))
            {
                hex[count++] = char.ToUpperInvariant(c);
                if (count == 6)
                {
                    return new string(hex);
                }
            }
        }

        return null;
    }
}
