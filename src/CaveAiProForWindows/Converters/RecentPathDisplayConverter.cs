using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace CaveAiProForWindows.Converters;

/// <summary>Shows file name for recent-path rows; full path remains in ToolTip.</summary>
public sealed class RecentPathDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return "";
        path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.Exists(path))
            return Path.GetFileName(path) + Path.DirectorySeparatorChar;
        return Path.GetFileName(path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
