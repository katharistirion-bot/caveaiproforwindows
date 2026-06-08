using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services;

/// <summary>High-resolution raster export of the full survey plan bounds (not the on-screen zoom rectangle).</summary>
public static class PlanMapRasterExporter
{
    /// <summary>WPF logical DIP to <see cref="MapExportQuality.Standard"/> scaling (300 / 96).</summary>
    public const double ExportDpiScale = 300.0 / 96.0;

    public const int MaxExportEdgePixels = 10_000;

    public const int MinExportEdgePixels = 800;

    /// <summary>Base long edge in pixels before DPI scaling (keeps output sharp but bounded).</summary>
    public const int BaseLongEdgePixels = 3200;

    /// <summary>PNG bytes of the full plan scene at export resolution, or null if nothing drawable.</summary>
    public static byte[]? TryCapturePlanPngHighRes(
        CaveProjectDocument project,
        SurveyVisualizationMode visualizationMode,
        bool highContrast,
        PlanCanvasDrawOptions drawOptions,
        IReadOnlyList<PlanRasterUnderlay> underlays,
        string? zipPath,
        int vectorViewMode = SurveyStationGeometry.AndroidViewModePlan,
        MapExportQuality quality = MapExportQuality.Standard)
    {
        if (visualizationMode == SurveyVisualizationMode.Pseudo3D)
            return null;

        var scene = PlanSceneBuilder.TryBuild(project, vectorViewMode, visualizationMode);
        if (scene == null && underlays.Count == 0)
            return null;

        var (pxW, pxH) = ComputeExportPixelSize(scene, quality);

        var canvas = new Canvas();
        TextOptions.SetTextFormattingMode(canvas, TextFormattingMode.Ideal);
        if (scene == null)
        {
            PlanCanvasRenderer.DrawRasterUnderlaysOnly(
                canvas,
                highContrast,
                pxW,
                pxH,
                underlays,
                "plan");
        }
        else
        {
            PlanCanvasRenderer.Draw(
                scene,
                canvas,
                highContrast,
                pxW,
                pxH,
                underlays,
                project,
                zipPath,
                drawOptions);
        }

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

    public static (int pxW, int pxH) ComputeExportPixelSize(PlanScene? scene, MapExportQuality quality = MapExportQuality.Standard) =>
        ComputeExportPixelSize(
            scene != null ? scene.SpanX : 1,
            scene != null ? scene.SpanY : 1,
            quality);

    public static (int pxW, int pxH) ComputeExportPixelSize(
        float spanX,
        float spanY,
        MapExportQuality quality = MapExportQuality.Standard)
    {
        var dpiScale = quality.DpiScale();
        var aspect = spanX / Math.Max(spanY, 1e-6f);
        double bw;
        double bh;
        if (aspect >= 1)
        {
            bw = BaseLongEdgePixels;
            bh = BaseLongEdgePixels / aspect;
        }
        else
        {
            bh = BaseLongEdgePixels;
            bw = BaseLongEdgePixels * aspect;
        }

        var pxW = (int)Math.Round(bw * dpiScale);
        var pxH = (int)Math.Round(bh * dpiScale);
        var maxDim = Math.Max(pxW, pxH);
        if (maxDim > MaxExportEdgePixels)
        {
            var f = MaxExportEdgePixels / (double)maxDim;
            pxW = Math.Max(MinExportEdgePixels, (int)(pxW * f));
            pxH = Math.Max(MinExportEdgePixels, (int)(pxH * f));
        }

        pxW = Math.Max(MinExportEdgePixels, pxW);
        pxH = Math.Max(MinExportEdgePixels, pxH);
        return (pxW, pxH);
    }

    /// <summary>
    /// WYSIWYG capture of the on-screen 3D shell (current camera, labels, overlays) at export DPI.
    /// </summary>
    public static byte[]? TryCaptureViewport3DPngWysiwyg(
        Visual hostShell,
        MapExportQuality quality = MapExportQuality.Print)
    {
        if (hostShell is not FrameworkElement fe)
            return null;

        var w = Math.Max(320, fe.ActualWidth);
        var h = Math.Max(240, fe.ActualHeight);
        if (w < 8 || h < 8)
        {
            w = fe.DesiredSize.Width > 8 ? Math.Max(320, fe.DesiredSize.Width) : 960;
            h = fe.DesiredSize.Height > 8 ? Math.Max(240, fe.DesiredSize.Height) : 640;
        }

        fe.Measure(new Size(w, h));
        fe.Arrange(new Rect(0, 0, w, h));
        fe.UpdateLayout();

        var dpi = quality.DpiScale();
        var pxW = (int)Math.Max(1, Math.Ceiling(w * dpi));
        var pxH = (int)Math.Max(1, Math.Ceiling(h * dpi));
        var maxDim = Math.Max(pxW, pxH);
        if (maxDim > MaxExportEdgePixels)
        {
            var f = MaxExportEdgePixels / (double)maxDim;
            pxW = Math.Max(MinExportEdgePixels, (int)(pxW * f));
            pxH = Math.Max(MinExportEdgePixels, (int)(pxH * f));
        }

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var vb = new VisualBrush(hostShell)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewbox = new Rect(0, 0, 1, 1),
            };
            dc.DrawRectangle(vb, null, new Rect(0, 0, pxW, pxH));
        }

        var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Off-screen 3D viewport capture (default camera; batch/report fallback).</summary>
    public static byte[]? TryCapture3DPng(
        CaveProjectDocument project,
        int width = 1600,
        int height = 1200,
        Viewport3DDisplayOptions? display = null)
    {
        var viewport = new Viewport3D { Width = width, Height = height };
        var shell = new Border { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(0x13, 0x14, 0x18)) };
        var labelCanvas = new Canvas { Width = width, Height = height };
        var root = new Grid { Width = width, Height = height };
        root.Children.Add(viewport);
        root.Children.Add(labelCanvas);

        if (!CaveViewport3DPresenter.TryPopulate(viewport, project, shell, labelCanvas, display))
            return null;

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        CaveViewport3DPresenter.Detach(viewport, shell, labelCanvas);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
