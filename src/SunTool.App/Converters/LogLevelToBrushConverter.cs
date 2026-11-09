using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SunTool.Core.Logging;

namespace SunTool.App.Converters;

/// <summary>터미널 창에서 로그 레벨별 글자색.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    private static readonly Brush TraceBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x81)));
    private static readonly Brush DebugBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E)));
    private static readonly Brush InfoBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE)));
    private static readonly Brush WarningBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE3, 0xB3, 0x41)));
    private static readonly Brush ErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x7B, 0x72)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            LogLevel.Trace => TraceBrush,
            LogLevel.Debug => DebugBrush,
            LogLevel.Warning => WarningBrush,
            LogLevel.Error => ErrorBrush,
            _ => InfoBrush,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
