using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SketchAssist;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>High-resolution cartography PNG for cloud publish (survey plan + sketch design overlay).</summary>
public static class SketchEditorPublishCapture
{
    public static byte[]? TryCaptureAiMapPng(
        CaveProjectDocument project,
        SurveyVisualizationMode visualizationMode,
        PlanCanvasDrawOptions drawOptions,
        IReadOnlyList<PlanRasterUnderlay> underlays,
        string? zipPath,
        Canvas? designLayer,
        double editorCanvasWidth,
        double editorCanvasHeight)
    {
        ArgumentNullException.ThrowIfNull(project);

        var baseBytes = PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            visualizationMode,
            highContrast: false,
            drawOptions,
            underlays,
            zipPath);

        if (baseBytes == null)
            return null;

        if (designLayer == null || designLayer.Children.Count == 0 || editorCanvasWidth < 1 || editorCanvasHeight < 1)
            return baseBytes;

        var scene = PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan, visualizationMode);
        if (scene == null)
            return baseBytes;

        var (pxW, pxH) = SketchAssistExportSizing.ComputeExportPixelSize(scene);
        if (pxW < 1 || pxH < 1)
            return baseBytes;

        designLayer.Measure(new Size(editorCanvasWidth, editorCanvasHeight));
        designLayer.Arrange(new Rect(0, 0, editorCanvasWidth, editorCanvasHeight));
        designLayer.UpdateLayout();

        var baseImage = LoadPng(baseBytes);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(baseImage, new Rect(0, 0, pxW, pxH));
            var overlay = new VisualBrush(designLayer)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, editorCanvasWidth, editorCanvasHeight),
            };
            dc.DrawRectangle(overlay, null, new Rect(0, 0, pxW, pxH));
        }

        var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

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
}
