using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CaveAiProForWindows.Converters;

/// <summary><c>true</c> → <see cref="Visibility.Collapsed"/>, <c>false</c> → <see cref="Visibility.Visible"/> (for “blocked until” overlays).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var v = value is bool b && b;
        return v ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility vis && vis != Visibility.Visible;
}
