using System.Globalization;
using System.Windows.Data;

namespace CaveAiProForWindows.Converters;

/// <summary>First letter for placeholder tiles when no cover image is available.</summary>
public sealed class CaveNameInitialConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return "?";
        var t = s.Trim();
        return char.ToUpperInvariant(t[0]).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
