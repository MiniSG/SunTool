using System.Text;
using SunTool.Core;
using SunTool.Core.Text;

namespace SunTool.Modules.SystemInfo.Logic;

/// <summary>수집 결과를 CSV(엑셀용)·텍스트로 만듭니다.</summary>
public static class SystemInfoReport
{
    public static readonly string[] Header = [Loc.T("분류"), Loc.T("구분"), Loc.T("항목"), Loc.T("값"), Loc.T("상태"), Loc.T("비고")];

    public static List<string?[]> ToRows(IEnumerable<(string Category, InfoSection Section)> sections, DateTime collectedAt)
    {
        var rows = new List<string?[]>
        {
            Header,
            new[] { Loc.T("보고서"), Loc.T("정보"), Loc.T("PC 이름"), Environment.MachineName, string.Empty, string.Empty },
            new[] { Loc.T("보고서"), Loc.T("정보"), Loc.T("수집 시각"), collectedAt.ToString("yyyy-MM-dd HH:mm:ss"), string.Empty, string.Empty },
            new[] { Loc.T("보고서"), Loc.T("정보"), Loc.T("SunTool 버전"), AppVersion.Text, string.Empty, string.Empty },
        };

        foreach (var (category, section) in sections)
        {
            foreach (var item in section.Items)
            {
                rows.Add([category, section.Title, item.Name, item.Value, item.StatusText, item.Detail ?? string.Empty]);
            }
        }

        return rows;
    }

    public static void WriteCsv(string path, IEnumerable<(string Category, InfoSection Section)> sections, DateTime collectedAt) =>
        CsvWriter.Write(path, ToRows(sections, collectedAt));

    public static string ToText(IEnumerable<(string Category, InfoSection Section)> sections, DateTime collectedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Loc.F("[SunTool 시스템 정보] {0} · {1:yyyy-MM-dd HH:mm} · v{2}", Environment.MachineName, collectedAt, AppVersion.Text));
        string? lastCategory = null;
        foreach (var (category, section) in sections)
        {
            if (category != lastCategory)
            {
                builder.AppendLine().AppendLine($"■ {category}");
                lastCategory = category;
            }

            builder.AppendLine($"  [{section.Title}]");
            foreach (var item in section.Items)
            {
                var status = item.HasStatus ? $" [{item.StatusText}]" : string.Empty;
                builder.AppendLine($"    {item.Name}: {item.Value}{status}");
                if (item.HasDetail)
                {
                    builder.AppendLine($"      - {item.Detail}");
                }
            }
        }

        return builder.ToString();
    }
}
