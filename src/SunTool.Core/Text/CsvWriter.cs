using System.Text;

namespace SunTool.Core.Text;

/// <summary>엑셀에서 한글이 깨지지 않는 CSV(UTF-8 BOM) 작성 도우미.</summary>
public static class CsvWriter
{
    public static string Escape(string? value)
    {
        value ??= string.Empty;
        var needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Line(params string?[] values) => string.Join(",", values.Select(Escape));

    public static void Write(string path, IEnumerable<string?[]> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            builder.AppendLine(Line(row));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
