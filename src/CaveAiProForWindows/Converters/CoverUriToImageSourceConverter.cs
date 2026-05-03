using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace CaveAiProForWindows.Converters;

/// <summary>Loads http(s) or local file paths into a frozen <see cref="BitmapImage"/> for card thumbnails.</summary>
public sealed class CoverUriToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return null;
        s = s.Trim();
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                bmp.UriSource = new Uri(s, UriKind.Absolute);
            }
            else if (s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var local = new Uri(s).LocalPath;
                if (!File.Exists(local))
                    return null;
                bmp.UriSource = new Uri(local, UriKind.Absolute);
            }
            else
            {
                var full = Path.GetFullPath(s);
                if (!File.Exists(full))
                    return null;
                bmp.UriSource = new Uri(full, UriKind.Absolute);
            }

            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
