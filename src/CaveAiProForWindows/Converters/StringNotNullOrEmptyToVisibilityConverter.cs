using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CaveAiProForWindows.Converters;

/// <summary>Visible when the string has content — used for optional library id chips.</summary>
public sealed class StringNotNullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
