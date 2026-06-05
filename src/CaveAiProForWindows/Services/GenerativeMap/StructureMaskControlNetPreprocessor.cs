using System.IO;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using System.Windows.Media.Imaging;

using CaveAiProForWindows.Services.SketchAssist;
using CaveAiProForWindows.Views;



namespace CaveAiProForWindows.Services.GenerativeMap;



/// <summary>

/// Builds LRUD corridor structure masks and prepares them for Replicate ControlNet scribble input.

/// </summary>

public static class StructureMaskControlNetPreprocessor

{

    public const int DefaultMaxEdgePixels = 768;



    /// <summary>

    /// Builds a grey-on-black LRUD corridor mask from survey data, then inverts to grey-on-white for ControlNet.

    /// </summary>

    public static byte[]? PrepareFromSession(

        SketchAssistSession session,

        SketchAssistStructureMaskOptions? options = null,

        int maxEdgePixels = DefaultMaxEdgePixels)

    {

        var mask = TryBuildGrayscaleLrudMaskPng(session, options);

        return mask == null ? null : PrepareScribbleInput(mask, maxEdgePixels);

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



    /// <summary>Inverts grey-on-black to grey-on-white and downscales for API limits (preserves grey tones).</summary>

    public static byte[]? PrepareScribbleInput(byte[] greyOnBlackMaskPng, int maxEdgePixels = DefaultMaxEdgePixels)

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



        var scaled = ScaleToMaxEdge(source, maxEdgePixels);

        var inverted = InvertGrayscale(scaled);

        return EncodePng(inverted);

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



    private static BitmapSource ScaleToMaxEdge(BitmapSource source, int maxEdge)

    {

        var w = source.PixelWidth;

        var h = source.PixelHeight;

        if (w <= 0 || h <= 0)

            return source;



        var maxDim = Math.Max(w, h);

        if (maxDim <= maxEdge)

            return source;



        var scale = maxEdge / (double)maxDim;

        var targetW = Math.Max(1, (int)Math.Round(w * scale));

        var targetH = Math.Max(1, (int)Math.Round(h * scale));



        var scaled = new TransformedBitmap(

            source,

            new ScaleTransform(targetW / (double)w, targetH / (double)h));

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



    private static byte[] EncodePng(BitmapSource bitmap)

    {

        var enc = new PngBitmapEncoder();

        enc.Frames.Add(BitmapFrame.Create(bitmap));

        using var ms = new MemoryStream();

        enc.Save(ms);

        return ms.ToArray();

    }

}


