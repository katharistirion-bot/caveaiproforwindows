using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace CaveAiProForWindows.Services.Visualization;

/// <summary>
/// Embeds the CaveAI Pro logo on exported PNG/PDF rasters and WPF visual trees.
/// Uses SkiaSharp for byte[] PNG pipeline (already referenced by <see cref="PlanPdfExport"/>).
/// </summary>
public static class CaveMappingWatermarkRenderer
{
    public static byte[] ApplyToPngBytes(ReadOnlySpan<byte> pngBytes, CaveMappingWatermarkOptions? options = null)
    {
        options ??= CaveMappingWatermarkOptions.Default;
        using var decoded = SKBitmap.Decode(pngBytes)
            ?? throw new InvalidOperationException("Could not decode PNG for watermarking.");

        using var surface = SKSurface.Create(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(decoded, 0, 0);

        using var logo = LoadLogoSkBitmap(options.LogoPackUri);
        if (logo != null)
        {
            if (options.IncludeCentreFaintMark)
                DrawCornerOrCentre(canvas, logo, decoded.Width, decoded.Height, options, centre: true);

            DrawCornerOrCentre(canvas, logo, decoded.Width, decoded.Height, options, centre: false);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Overlays watermark on a WPF <see cref="FrameworkElement"/> before rasterization.</summary>
    public static void DecorateVisualTree(FrameworkElement root, CaveMappingWatermarkOptions? options = null)
    {
        options ??= CaveMappingWatermarkOptions.Default;
        if (root is not Panel panel)
            return;

        var logo = TryLoadLogoWpf(options.LogoPackUri);
        if (logo == null)
            return;

        var host = new Grid { IsHitTestVisible = false };
        if (panel.Children.Count == 1 && panel.Children[0] is FrameworkElement single)
        {
            panel.Children.RemoveAt(0);
            host.Children.Add(single);
        }
        else
        {
            foreach (UIElement child in panel.Children.Cast<UIElement>().ToList())
            {
                panel.Children.Remove(child);
                host.Children.Add(child);
            }
        }

        var shortEdge = Math.Min(host.ActualWidth > 0 ? host.ActualWidth : root.Width, host.ActualHeight > 0 ? host.ActualHeight : root.Height);
        if (shortEdge <= 0)
            shortEdge = 800;

        var logoSize = shortEdge * options.LogoWidthFraction;
        host.Children.Add(new Image
        {
            Source = logo,
            Width = logoSize,
            Height = logoSize,
            Opacity = options.Opacity,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = ToHorizontal(options.Corner),
            VerticalAlignment = ToVertical(options.Corner),
            Margin = new Thickness(shortEdge * options.MarginFraction),
            IsHitTestVisible = false,
        });

        panel.Children.Add(host);
    }

    public static void AppendSvgWatermark(StringWriter writer, float viewBoxWidth, float viewBoxHeight, CaveMappingWatermarkOptions? options = null)
    {
        options ??= CaveMappingWatermarkOptions.Default;
        var logoBytes = TryLoadLogoPngBytes(options.LogoPackUri);
        if (logoBytes == null || logoBytes.Length == 0)
            return;

        var b64 = Convert.ToBase64String(logoBytes);
        var shortEdge = Math.Min(viewBoxWidth, viewBoxHeight);
        var w = shortEdge * (float)options.LogoWidthFraction;
        var m = shortEdge * (float)options.MarginFraction;
        var (x, y) = CornerSvg(viewBoxWidth, viewBoxHeight, w, m, options.Corner);

        writer.WriteLine(
            $"""<image href="data:image/png;base64,{b64}" x="{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}" y="{y.ToString(System.Globalization.CultureInfo.InvariantCulture)}" width="{w.ToString(System.Globalization.CultureInfo.InvariantCulture)}" height="{w.ToString(System.Globalization.CultureInfo.InvariantCulture)}" opacity="{options.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}" />""");
    }

    private static void DrawCornerOrCentre(
        SKCanvas canvas,
        SKBitmap logo,
        int mapW,
        int mapH,
        CaveMappingWatermarkOptions options,
        bool centre)
    {
        var shortEdge = Math.Min(mapW, mapH);
        var targetW = (float)(shortEdge * options.LogoWidthFraction);
        var scale = targetW / logo.Width;
        var targetH = logo.Height * scale;
        var margin = (float)(shortEdge * options.MarginFraction);

        SKRect dest;
        if (centre)
        {
            dest = new SKRect(
                (mapW - targetW * 2.2f) / 2f,
                (mapH - targetH * 2.2f) / 2f,
                (mapW + targetW * 2.2f) / 2f,
                (mapH + targetH * 2.2f) / 2f);
        }
        else
        {
            var (x, y) = CornerSk(mapW, mapH, targetW, targetH, margin, options.Corner);
            dest = new SKRect(x, y, x + targetW, y + targetH);
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White.WithAlpha((byte)(255 * (centre ? options.CentreFaintOpacity : options.Opacity))),
        };
        canvas.DrawBitmap(logo, dest, paint);
    }

    private static (float x, float y) CornerSk(int mapW, int mapH, float w, float h, float margin, CaveMappingWatermarkCorner corner) =>
        corner switch
        {
            CaveMappingWatermarkCorner.TopLeft => (margin, margin),
            CaveMappingWatermarkCorner.TopRight => (mapW - w - margin, margin),
            CaveMappingWatermarkCorner.BottomLeft => (margin, mapH - h - margin),
            _ => (mapW - w - margin, mapH - h - margin),
        };

    private static (float x, float y) CornerSvg(float mapW, float mapH, float w, float margin, CaveMappingWatermarkCorner corner) =>
        corner switch
        {
            CaveMappingWatermarkCorner.TopLeft => (margin, margin),
            CaveMappingWatermarkCorner.TopRight => (mapW - w - margin, margin),
            CaveMappingWatermarkCorner.BottomLeft => (margin, mapH - w - margin),
            _ => (mapW - w - margin, mapH - w - margin),
        };

    private static SKBitmap? LoadLogoSkBitmap(string packUri)
    {
        var bytes = TryLoadLogoPngBytes(packUri);
        return bytes == null ? null : SKBitmap.Decode(bytes);
    }

    private static byte[]? TryLoadLogoPngBytes(string packUri)
    {
        try
        {
            var s = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute))?.Stream;
            if (s == null)
                return null;
            using (s)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryLoadLogoWpf(string packUri)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(packUri, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static HorizontalAlignment ToHorizontal(CaveMappingWatermarkCorner c) =>
        c is CaveMappingWatermarkCorner.TopRight or CaveMappingWatermarkCorner.BottomRight
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

    private static VerticalAlignment ToVertical(CaveMappingWatermarkCorner c) =>
        c is CaveMappingWatermarkCorner.BottomLeft or CaveMappingWatermarkCorner.BottomRight
            ? VerticalAlignment.Bottom
            : VerticalAlignment.Top;
}
