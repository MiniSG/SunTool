using System.Globalization;

namespace SunTool.Core.Text;

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : "0.#";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
