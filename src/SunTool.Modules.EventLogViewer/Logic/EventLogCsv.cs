using System.Text;

namespace SunTool.Modules.EventLogViewer.Logic;

/// <summary>조회 결과를 엑셀에서 바로 열 수 있는 CSV(UTF-8 BOM)로 저장합니다.</summary>
public static class EventLogCsv
{
    public static void Write(string path, IEnumerable<EventLogItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Loc.T("수준,날짜 및 시간,로그,원본,이벤트 ID,작업 범주,메시지"));
        foreach (var item in items)
        {
            builder.Append(Escape(item.LevelText)).Append(',')
                .Append(Escape(item.TimeText)).Append(',')
                .Append(Escape(item.LogDisplayName)).Append(',')
                .Append(Escape(item.ProviderName)).Append(',')
                .Append(item.EventId).Append(',')
                .Append(Escape(item.TaskCategory)).Append(',')
                .Append(Escape(item.Message)).AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static string Escape(string? value)
    {
        value ??= string.Empty;
        var needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
