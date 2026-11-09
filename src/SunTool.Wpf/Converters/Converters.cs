using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SunTool.Wpf.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}

/// <summary>
/// enum 값이 ConverterParameter 목록("AtIndex,BeforeText")에 있으면 Visible.
/// 매개변수 앞에 '!' 를 붙이면 반대로 동작합니다.
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string text)
        {
            return Visibility.Collapsed;
        }

        var invert = text.StartsWith('!');
        if (invert)
        {
            text = text[1..];
        }

        var current = value.ToString();
        var match = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase));

        return match ^ invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>bool → Visibility. ConverterParameter="Invert" 이면 반대.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>문자열/객체가 비어 있지 않으면 Visible. ConverterParameter="Invert" 이면 비어 있을 때 Visible.</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true,
        };

        if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
