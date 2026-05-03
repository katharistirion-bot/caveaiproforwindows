using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Loads rasters for UI underlays: WPF/WIC first, then SkiaSharp (more TIFF/WebP variants and many GeoTIFF payloads).
/// </summary>
public static class RasterImageDecoder
{
    private const int MaxUnderlayDimension = 8192;

    public static BitmapSource? TryLoadBitmap(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            return null;

        if (TryWpfBitmap(fullPath) is { } wpf)
            return wpf;

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext is ".tif" or ".tiff" or ".geotiff")
        {
            if (TryWpfTiffFirstFrame(fullPath) is { } tif)
                return tif;
        }

        return TrySkiaBitmap(fullPath);
    }

    private static BitmapSource? TryWpfBitmap(string fullPath)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(Path.GetFullPath(fullPath), UriKind.Absolute);
            bmp.EndInit();
            if (bmp.CanFreeze)
                bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Multi-page / float GeoTIFF often fails <see cref="BitmapImage"/>; WIC TIFF decoder + first frame is more reliable.</summary>
    private static BitmapSource? TryWpfTiffFirstFrame(string fullPath)
    {
        try
        {
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = new TiffBitmapDecoder(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return null;
            var frame = decoder.Frames[0];
            if (frame.Format == PixelFormats.Pbgra32 || frame.Format == PixelFormats.Bgra32)
            {
                if (frame.CanFreeze)
                    frame.Freeze();
                return frame;
            }

            var conv = new FormatConvertedBitmap();
            conv.BeginInit();
            conv.Source = frame;
            conv.DestinationFormat = PixelFormats.Pbgra32;
            conv.EndInit();
            if (conv.CanFreeze)
                conv.Freeze();
            return conv;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TrySkiaBitmap(string fullPath)
    {
        try
        {
            using var sk = SKBitmap.Decode(fullPath);
            if (sk == null || sk.Width <= 0 || sk.Height <= 0)
                return null;

            using var scaled = MaybeDownscaleIfNeeded(sk);
            var src = scaled ?? sk;
            var w = src.Width;
            var h = src.Height;

            using var bgra = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bgra))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(src, 0, 0);
            }

            using var image = SKImage.FromBitmap(bgra);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded == null)
                return null;

            using var ms = new MemoryStream(encoded.ToArray(), writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            if (bmp.CanFreeze)
                bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <returns>Non-null when a downscaled copy was created; otherwise use the original decode.</returns>
    private static SKBitmap? MaybeDownscaleIfNeeded(SKBitmap sk)
    {
        if (sk.Width <= MaxUnderlayDimension && sk.Height <= MaxUnderlayDimension)
            return null;

        var scale = Math.Min((double)MaxUnderlayDimension / sk.Width, (double)MaxUnderlayDimension / sk.Height);
        var nw = Math.Max(1, (int)Math.Round(sk.Width * scale));
        var nh = Math.Max(1, (int)Math.Round(sk.Height * scale));
        var info = new SKImageInfo(nw, nh, SKColorType.Bgra8888, SKAlphaType.Premul);
        return sk.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
    }
}
