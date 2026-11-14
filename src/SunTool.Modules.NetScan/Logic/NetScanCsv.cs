using SunTool.Core.Text;

namespace SunTool.Modules.NetScan.Logic;

/// <summary>검색 결과를 CSV(UTF-8 BOM) 로 내보냅니다.</summary>
public static class NetScanCsv
{
    public static string[] HeaderFields =>
    [
        Loc.T("IP 주소"),
        Loc.T("응답(ms)"),
        "TTL",
        Loc.T("MAC 주소"),
        Loc.T("제조사"),
        Loc.T("이름"),
    ];

    public static string Header => CsvWriter.Line(HeaderFields);

    public static string[] RowFields(ScanRow row) =>
    [
        row.Ip,
        row.ResponseMs.ToString(),
        row.TtlText,
        row.Mac,
        row.Vendor,
        row.HostName,
    ];

    public static string Row(ScanRow row) => CsvWriter.Line(RowFields(row));

    /// <summary>헤더 + 모든 행을 담은 CSV 전체 텍스트.</summary>
    public static IEnumerable<string?[]> Rows(IEnumerable<ScanRow> rows)
    {
        yield return HeaderFields;
        foreach (var row in rows)
        {
            yield return RowFields(row);
        }
    }
}
