using System.Globalization;
using System.Windows.Data;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Converters;

/// <summary>Converts a <see cref="CaveProjectDocument"/> to a compact QC badge for the project list.</summary>
public sealed class ProjectQcBadgeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CaveProjectDocument p)
            return ProjectQcBadgeInfo.Empty;
        return TraverseQcStats.ComputeProjectListBadge(p);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
