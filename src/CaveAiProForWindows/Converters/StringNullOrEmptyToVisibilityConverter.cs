using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CaveAiProForWindows.Converters;

/// <summary>Visible when the string is null/empty/whitespace — for placeholder UI when there is no cover URI.</summary>
public sealed class StringNullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
