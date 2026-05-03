using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace CaveAiProForWindows.Converters;

/// <summary>Converts #AARRGGBB or #RRGGBB to <see cref="SolidColorBrush"/>.</summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return System.Windows.Media.Brushes.Gray;
        try
        {
            s = s.Trim();
            if (s.StartsWith('#')) s = s[1..];
            if (s.Length == 6)
            {
                var color = (System.Windows.Media.Color)MediaColorConverter.ConvertFromString("#" + s)!;
                return new SolidColorBrush(color);
            }

            if (s.Length == 8)
            {
                var a = byte.Parse(s[..2], NumberStyles.HexNumber, culture);
                var r = byte.Parse(s[2..4], NumberStyles.HexNumber, culture);
                var g = byte.Parse(s[4..6], NumberStyles.HexNumber, culture);
                var b = byte.Parse(s[6..8], NumberStyles.HexNumber, culture);
                return new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            }
        }
        catch
        {
            /* ignore */
        }

        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
