using System.Globalization;
using System.Management;

namespace SunTool.Modules.SystemInfo.Logic;

/// <summary>WMI 조회 도우미. 결과를 속성 이름 → 값 사전으로 돌려줍니다.</summary>
public static class Wmi
{
    public static List<Dictionary<string, object?>> Query(string wql, string scope = @"root\cimv2", int timeoutSeconds = 20)
    {
        var options = new System.Management.EnumerationOptions
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            ReturnImmediately = true,
            Rewindable = false,
        };

        var rows = new List<Dictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
        using var results = searcher.Get();
        foreach (ManagementBaseObject obj in results)
        {
            using (obj)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in obj.Properties)
                {
                    row[property.Name] = property.Value;
                }

                rows.Add(row);
            }
        }

        return rows;
    }

    public static string Str(this IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return value is string[] array
            ? string.Join(", ", array)
            : (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
    }

    public static long? Long(this IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>WMI 날짜 문자열(20240115000000.000000+540) → DateTime.</summary>
    public static DateTime? Date(this IReadOnlyDictionary<string, object?> row, string key)
    {
        var text = row.Str(key);
        if (text.Length < 14)
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(text);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>WQL 문자열 리터럴 이스케이프 (\ 와 ').</summary>
    public static string EscapeLiteral(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
