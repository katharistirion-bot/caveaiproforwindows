using System.Globalization;
using System.Windows.Data;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Converters;

/// <summary>Human site-type label for Cave Library cards (CAVE → Cave).</summary>
public sealed class SurveySiteTypeDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string raw)
            return "";
        var label = SurveySiteType.GetMapLabel(SurveySiteType.Parse(raw));
        return string.IsNullOrWhiteSpace(label) ? raw : label;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
