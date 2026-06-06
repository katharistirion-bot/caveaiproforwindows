using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Services.SketchAssist;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>Result of preparing a structure mask for Replicate ControlNet scribble input.</summary>
public sealed class PreparedControlNetMask
{
    public required byte[] PngBytes { get; init; }

    /// <summary>Original full-resolution mask size (survey export pixels).</summary>
    public int SourceWidth { get; init; }

    public int SourceHeight { get; init; }

    /// <summary>Downscaled API input size (longest edge ≤ 1024, multiples of 8).</summary>
    public int ApiWidth { get; init; }

    public int ApiHeight { get; init; }
}

/// <summary>
/// Builds LRUD corridor structure masks and prepares them for Replicate ControlNet scribble input.
/// </summary>
public static class StructureMaskControlNetPreprocessor
{
    public const int DefaultMaxEdgePixels = ControlNetMaskDimensions.MaxEdgePixels;

    /// <summary>
    /// Builds a grey-on-black LRUD corridor mask from survey data, then inverts to grey-on-white for ControlNet.
    /// </summary>
    public static byte[]? PrepareFromSession(
        SketchAssistSession session,
        SketchAssistStructureMaskOptions? options = null,
        int maxEdgePixels = DefaultMaxEdgePixels)
    {
        var mask = TryBuildGrayscaleLrudMaskPng(session, options);
        return mask == null ? null : PrepareScribbleInput(mask, maxEdgePixels)?.PngBytes;
    }

    /// <summary>Grey-on-black PNG with LRUD corridor polygons (+ optional user sketch ink).</summary>
    public static byte[]? TryBuildGrayscaleLrudMaskPng(
        SketchAssistSession session,
        SketchAssistStructureMaskOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        options ??= SketchAssistStructureMaskOptions.Default;

        var (pxW, pxH) = SketchAssistExportSizing.ComputeExportPixelSize(session.Scene);
        if (!PlanCanvasRenderer.TryComputeSurveyLayout(session.Scene, pxW, pxH, out var exportLayout))
            return null;

        var canvas = new Canvas
        {
            Width = pxW,
            Height = pxH,
            Background = Brushes.Black,
            ClipToBounds = true,
        };

        if (options.IncludeLrudCorridor)
            StructureMaskLrudCorridorRenderer.DrawLrudCorridors(canvas, session, exportLayout);
        else if (options.IncludeTraverseCenterline)
            SketchAssistStructureMaskExporter.DrawTraverseCenterlineForMask(
                canvas, session.Scene, exportLayout, options.LineWidthPx);

        if (options.IncludeUserStrokes)
            SketchAssistStructureMaskExporter.DrawUserStrokesForMask(
                canvas, session.Document, exportLayout, options.LineWidthPx);

        if (options.IncludeUserSymbolStamps)
            SketchAssistStructureMaskExporter.DrawUserSymbolStampsForMask(
                canvas, session.Document, exportLayout, options.SymbolMarkerRadiusPx);

        return RenderCanvasToPng(canvas, pxW, pxH);
    }

    /// <summary>
    /// Inverts grey-on-black to grey-on-white, downscales for GPU limits, and returns API + source dimensions.
    /// </summary>
    public static PreparedControlNetMask? PrepareScribbleInput(
        byte[] greyOnBlackMaskPng,
        int maxEdgePixels = DefaultMaxEdgePixels)
    {
        if (greyOnBlackMaskPng.Length == 0)
            return null;

        BitmapSource source;
        try
        {
            source = LoadPng(greyOnBlackMaskPng);
        }
        catch
        {
            return null;
        }

        var sourceW = source.PixelWidth;
        var sourceH = source.PixelHeight;
        var (apiW, apiH) = ControlNetMaskDimensions.ComputeApiDimensions(sourceW, sourceH, maxEdgePixels);

        var scaled = ResizeBitmap(source, apiW, apiH);
        var inverted = InvertGrayscale(scaled);
        var png = EncodePng(inverted);
        if (png == null)
            return null;

        return new PreparedControlNetMask
        {
            PngBytes = png,
            SourceWidth = sourceW,
            SourceHeight = sourceH,
            ApiWidth = apiW,
            ApiHeight = apiH,
        };
    }

    /// <summary>Upscale (or downscale) a PNG to the original survey export size for canvas overlay alignment.</summary>
    public static byte[]? ResizePngToDimensions(byte[] pngBytes, int targetWidth, int targetHeight)
    {
        if (pngBytes.Length == 0 || targetWidth <= 0 || targetHeight <= 0)
            return null;

        BitmapSource source;
        try
        {
            source = LoadPng(pngBytes);
        }
        catch
        {
            return null;
        }

        if (source.PixelWidth == targetWidth && source.PixelHeight == targetHeight)
            return pngBytes;

        var resized = ResizeBitmap(source, targetWidth, targetHeight);
        return EncodePng(resized);
    }

    internal static byte[]? RenderCanvasToPng(Canvas canvas, int pxW, int pxH)
    {
        canvas.Measure(new Size(pxW, pxH));
        canvas.Arrange(new Rect(0, 0, pxW, pxH));
        canvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static (int Width, int Height) TryReadPngDimensions(byte[] png)
    {
        try
        {
            var img = LoadPng(png);
            return (img.PixelWidth, img.PixelHeight);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static BitmapSource LoadPng(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private static BitmapSource ResizeBitmap(BitmapSource source, int targetW, int targetH)
    {
        if (source.PixelWidth == targetW && source.PixelHeight == targetH)
            return source;

        var scaled = new TransformedBitmap(
            source,
            new ScaleTransform(
                targetW / (double)Math.Max(1, source.PixelWidth),
                targetH / (double)Math.Max(1, source.PixelHeight)));
        scaled.Freeze();
        return scaled;
    }

    /// <summary>Inverts RGB channels so black→white and grey G→(255−G), preserving tonal structure.</summary>
    private static BitmapSource InvertGrayscale(BitmapSource source)
    {
        var fmt = PixelFormats.Bgra32;
        var converted = new FormatConvertedBitmap(source, fmt, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(255 - pixels[i]);
            pixels[i + 1] = (byte)(255 - pixels[i + 1]);
            pixels[i + 2] = (byte)(255 - pixels[i + 2]);
        }

        var result = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            96,
            96,
            fmt,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    private static byte[]? EncodePng(BitmapSource bitmap)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
