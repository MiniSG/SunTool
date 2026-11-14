using System.Net;
using SunTool.Core.Text;
using SunTool.Modules.NetScan.Logic;

namespace SunTool.Tests;

public class NetScanTests
{
    // ================================================================= IpRange 파싱·열거

    [Fact]
    public void Cidr_24_EnumeratesAll256Addresses()
    {
        Assert.True(IpRange.TryParse("192.168.0.0/24", out var range, out var error));
        Assert.Null(error);
        Assert.NotNull(range);
        Assert.Equal(256, range!.Count);

        var all = range.Addresses().ToList();
        Assert.Equal(256, all.Count);
        Assert.Equal(IPAddress.Parse("192.168.0.0"), all[0]);
        Assert.Equal(IPAddress.Parse("192.168.0.255"), all[^1]);
    }

    [Fact]
    public void Cidr_32_IsSingleAddress()
    {
        Assert.True(IpRange.TryParse("10.0.0.5/32", out var range, out _));
        Assert.Equal(1, range!.Count);
        Assert.Equal(IPAddress.Parse("10.0.0.5"), range.Addresses().Single());
    }

    [Fact]
    public void Cidr_AlignsToNetworkBase()
    {
        // 호스트 비트가 섞인 주소도 네트워크 주소로 맞춰집니다.
        Assert.True(IpRange.TryParse("192.168.1.130/25", out var range, out _));
        Assert.Equal(128, range!.Count);
        Assert.Equal(IPAddress.Parse("192.168.1.128"), range.Addresses().First());
        Assert.Equal(IPAddress.Parse("192.168.1.255"), range.Addresses().Last());
    }

    [Fact]
    public void DashRange_FullForm_IsInclusive()
    {
        Assert.True(IpRange.TryParse("192.168.0.10-192.168.0.20", out var range, out _));
        Assert.Equal(11, range!.Count);
        Assert.Equal(IPAddress.Parse("192.168.0.10"), range.Addresses().First());
        Assert.Equal(IPAddress.Parse("192.168.0.20"), range.Addresses().Last());
    }

    [Fact]
    public void DashRange_ShortForm_UsesLastOctet()
    {
        Assert.True(IpRange.TryParse("192.168.0.100-120", out var range, out _));
        Assert.Equal(21, range!.Count);
        Assert.Equal(IPAddress.Parse("192.168.0.100"), range.Addresses().First());
        Assert.Equal(IPAddress.Parse("192.168.0.120"), range.Addresses().Last());
    }

    [Fact]
    public void SingleAddress_Parses()
    {
        Assert.True(IpRange.TryParse("8.8.8.8", out var range, out _));
        Assert.Equal(1, range!.Count);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), range.Addresses().Single());
    }

    [Fact]
    public void Cidr_16_IsAtTheCap_AndAllowed()
    {
        Assert.True(IpRange.TryParse("172.16.0.0/16", out var range, out var error));
        Assert.Null(error);
        Assert.Equal(IpRange.MaxAddresses, range!.Count);
    }

    [Fact]
    public void Cidr_15_ExceedsCap_IsRejected()
    {
        Assert.False(IpRange.TryParse("172.16.0.0/15", out var range, out var error));
        Assert.Null(range);
        Assert.NotNull(error);
    }

    [Fact]
    public void DashRange_OverCap_IsRejected()
    {
        // 0.0.0.0 - 255.255.255.255 는 약 43억개라 상한을 크게 넘김
        Assert.False(IpRange.TryParse("0.0.0.0-255.255.255.255", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ReversedRange_IsRejected()
    {
        Assert.False(IpRange.TryParse("192.168.0.50-192.168.0.10", out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-ip")]
    [InlineData("192.168.0.0/33")]
    [InlineData("192.168.0.0/-1")]
    [InlineData("999.1.1.1/24")]
    [InlineData("192.168.0.1-nope")]
    public void InvalidInputs_AreRejectedWithMessage(string input)
    {
        Assert.False(IpRange.TryParse(input, out var range, out var error));
        Assert.Null(range);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ToUInt_And_ToIp_RoundTrip()
    {
        var ip = IPAddress.Parse("192.168.1.200");
        Assert.Equal(ip, IpRange.ToIp(IpRange.ToUInt(ip)));
    }

    // ================================================================= OUI → 제조사

    [Theory]
    [InlineData("00:0C:29:AB:CD:EF", "VMware")]
    [InlineData("b8-27-eb-11-22-33", "Raspberry Pi")]
    [InlineData("080027AABBCC", "VirtualBox (Oracle)")]
    [InlineData("00:15:5D:00:11:22", "Microsoft (Hyper-V)")]
    public void VendorFor_KnownPrefixes(string mac, string expected)
    {
        Assert.Equal(expected, OuiLookup.VendorFor(mac));
    }

    [Theory]
    [InlineData("12:34:56:78:9A:BC")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("zz")]
    public void VendorFor_UnknownOrInvalid_IsEmpty(string? mac)
    {
        Assert.Equal(string.Empty, OuiLookup.VendorFor(mac));
    }

    [Fact]
    public void PrefixOf_NormalizesToUpperSixHex()
    {
        Assert.Equal("000C29", OuiLookup.PrefixOf("00:0c:29:ab:cd:ef"));
        Assert.Null(OuiLookup.PrefixOf("00:0c"));
    }

    // ================================================================= 제외 목록

    [Fact]
    public void ExcludeList_ParsesMixedSeparators()
    {
        var set = ExcludeList.Parse("192.168.0.1, 192.168.0.2\n192.168.0.3  192.168.0.4;192.168.0.5");
        Assert.Equal(5, set.Count);
        Assert.Contains(IpRange.ToUInt(IPAddress.Parse("192.168.0.3")), set);
    }

    [Fact]
    public void ExcludeList_IgnoresInvalidTokens()
    {
        var set = ExcludeList.Parse("192.168.0.1, garbage, 999.1.1.1, 192.168.0.2");
        Assert.Equal(2, set.Count);
        Assert.Contains(IpRange.ToUInt(IPAddress.Parse("192.168.0.1")), set);
        Assert.Contains(IpRange.ToUInt(IPAddress.Parse("192.168.0.2")), set);
    }

    [Fact]
    public void ExcludeList_Empty_IsEmptySet()
    {
        Assert.Empty(ExcludeList.Parse(null));
        Assert.Empty(ExcludeList.Parse("   "));
    }

    [Fact]
    public void ExcludeList_MembersMatchRangeAddresses()
    {
        var exclude = ExcludeList.Parse("192.168.0.1, 192.168.0.2");
        Assert.True(IpRange.TryParse("192.168.0.0/29", out var range, out _));
        var kept = range!.Addresses().Where(ip => !exclude.Contains(IpRange.ToUInt(ip))).ToList();
        Assert.DoesNotContain(IPAddress.Parse("192.168.0.1"), kept);
        Assert.DoesNotContain(IPAddress.Parse("192.168.0.2"), kept);
        Assert.Contains(IPAddress.Parse("192.168.0.3"), kept);
    }

    // ================================================================= ARP 표 파싱

    [Fact]
    public void ArpTable_ParsesWindowsOutput()
    {
        const string output = """
        Interface: 192.168.0.10 --- 0x5
          Internet Address      Physical Address      Type
          192.168.0.1           aa-bb-cc-dd-ee-ff     dynamic
          192.168.0.20          00-15-5d-01-02-03     dynamic
          192.168.0.255         ff-ff-ff-ff-ff-ff     static
        """;

        var map = ArpTable.Parse(output);
        Assert.Equal("AA:BB:CC:DD:EE:FF", map["192.168.0.1"]);
        Assert.Equal("00:15:5D:01:02:03", map["192.168.0.20"]);
        Assert.Equal(3, map.Count);
    }

    [Fact]
    public void ArpTable_Empty_IsEmpty()
    {
        Assert.Empty(ArpTable.Parse(null));
        Assert.Empty(ArpTable.Parse("no addresses here"));
    }

    [Theory]
    [InlineData("aa-bb-cc-dd-ee-ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
    public void NormalizeMac_ToUpperColon(string input, string expected)
    {
        Assert.Equal(expected, ArpTable.NormalizeMac(input));
    }

    [Theory]
    [InlineData("aa-bb-cc")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizeMac_WrongLength_IsNull(string? input)
    {
        Assert.Null(ArpTable.NormalizeMac(input));
    }

    // ================================================================= CSV 형식

    [Fact]
    public void Csv_Header_HasSixColumns()
    {
        var header = NetScanCsv.HeaderFields;
        Assert.Equal(6, header.Length);
        Assert.Equal("TTL", header[2]);
    }

    [Fact]
    public void Csv_Row_FormatsAllFields()
    {
        var row = new ScanRow(IPAddress.Parse("192.168.0.5"), 3, 64, "AA:BB:CC:DD:EE:FF", "VMware", "host.local");
        var fields = NetScanCsv.RowFields(row);
        Assert.Equal("192.168.0.5", fields[0]);
        Assert.Equal("3", fields[1]);
        Assert.Equal("64", fields[2]);
        Assert.Equal("AA:BB:CC:DD:EE:FF", fields[3]);
        Assert.Equal("VMware", fields[4]);
        Assert.Equal("host.local", fields[5]);
    }

    [Fact]
    public void Csv_Row_LeavesUnknownFieldsBlank()
    {
        var row = new ScanRow(IPAddress.Parse("192.168.0.9"), 1, null, string.Empty, string.Empty, string.Empty);
        var fields = NetScanCsv.RowFields(row);
        Assert.Equal(string.Empty, fields[2]); // TTL
        Assert.Equal(string.Empty, fields[3]); // MAC
        Assert.Equal(string.Empty, fields[4]); // vendor
        Assert.Equal(string.Empty, fields[5]); // name
    }

    [Fact]
    public void Csv_Rows_StartsWithHeaderThenRows()
    {
        var rows = new[]
        {
            new ScanRow(IPAddress.Parse("192.168.0.2"), 2, 64, "", "", ""),
            new ScanRow(IPAddress.Parse("192.168.0.1"), 1, 64, "", "", ""),
        };

        var lines = NetScanCsv.Rows(rows).Select(CsvWriter.Line).ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal(NetScanCsv.Header, lines[0]);
        Assert.Contains("192.168.0.2", lines[1]);
    }

    [Fact]
    public void ScanRow_IpValue_SortsNumerically()
    {
        var rows = new[]
        {
            new ScanRow(IPAddress.Parse("192.168.0.100"), 0, null, "", "", ""),
            new ScanRow(IPAddress.Parse("192.168.0.9"), 0, null, "", "", ""),
            new ScanRow(IPAddress.Parse("192.168.0.20"), 0, null, "", "", ""),
        };

        var sorted = rows.OrderBy(r => r.IpValue).Select(r => r.Ip).ToList();
        Assert.Equal(new[] { "192.168.0.9", "192.168.0.20", "192.168.0.100" }, sorted);
    }
}
