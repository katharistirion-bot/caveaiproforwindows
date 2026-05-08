using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services;

/// <summary>High-resolution raster export of the full survey plan bounds (not the on-screen zoom rectangle).</summary>
public static class PlanMapRasterExporter
{
    /// <summary>WPF logical DIP to ~300 DPI print scaling (300 / 96).</summary>
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
        string? zipPath)
    {
        if (visualizationMode == SurveyVisualizationMode.Pseudo3D)
            return null;

        var scene = PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan, visualizationMode);
        if (scene == null && underlays.Count == 0)
            return null;

        var (pxW, pxH) = ComputeExportPixelSize(scene);

        var canvas = new Canvas();
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

    private static (int pxW, int pxH) ComputeExportPixelSize(PlanScene? scene)
    {
        double spanX = scene != null ? scene.SpanX : 1;
        double spanY = scene != null ? scene.SpanY : 1;
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

        var pxW = (int)Math.Round(bw * ExportDpiScale);
        var pxH = (int)Math.Round(bh * ExportDpiScale);
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
}
