using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFtoImage;
using SkiaSharp;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Loads rasters for UI underlays: WPF/WIC first, PDF first page (PDFium), then SkiaSharp (more TIFF/WebP variants and many GeoTIFF payloads).
/// </summary>
public static class RasterImageDecoder
{
    private const int MaxUnderlayDimension = 8192;

    /// <summary>DPI for rasterizing PDF plan maps (first page only) for Plan/Section underlay.</summary>
    private const int PdfUnderlayDpi = 300;

    public static BitmapSource? TryLoadBitmap(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            return null;

        try
        {
            var length = new FileInfo(fullPath).Length;
            if (length > BackupFileSizeLimits.MaxRasterDecodeFileBytes)
            {
                Debug.WriteLine(
                    $"[RasterImageDecoder] skip oversized raster ({length:N0} bytes): {fullPath}");
                return null;
            }
        }
        catch
        {
            return null;
        }

        if (TryWpfBitmap(fullPath) is { } wpf)
            return wpf;

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext is ".tif" or ".tiff" or ".geotiff")
        {
            Debug.WriteLine($"[RasterImageDecoder] TIFF load path={fullPath}");
            if (TryWpfTiffFirstFrame(fullPath) is { } tif)
                return tif;
            Debug.WriteLine($"[RasterImageDecoder] WARNING: WIC TiffBitmapDecoder failed, trying Skia fallback: {fullPath}");
        }

        if (ext == ".pdf")
        {
            try
            {
                using var pdfSk = TryPdfFirstPageAsSkBitmap(fullPath);
                if (pdfSk != null)
                    return SkBitmapToFrozenPngBitmap(pdfSk);
            }
            catch
            {
                /* fall through to Skia (unlikely) */
            }
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

    private static SKBitmap? TryPdfFirstPageAsSkBitmap(string fullPath)
    {
        try
        {
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var opts = new PDFtoImage.RenderOptions { Dpi = PdfUnderlayDpi };
            return Conversion.ToImage(fs, page: 0, leaveOpen: false, password: null, options: opts);
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
            return SkBitmapToFrozenPngBitmap(sk);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Encodes an <see cref="SKBitmap"/> as in-memory PNG then loads a frozen <see cref="BitmapImage"/> (WPF-safe).</summary>
    private static BitmapSource SkBitmapToFrozenPngBitmap(SKBitmap sk)
    {
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
            throw new InvalidOperationException("SKImage.Encode returned null.");

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
