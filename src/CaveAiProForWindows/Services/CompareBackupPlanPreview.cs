using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services;

/// <summary>Mini plan raster for backup comparison dialog.</summary>
public static class CompareBackupPlanPreview
{
    public const double OverlayUnderlayOpacity = 0.45;

    public static ImageSource? TryRenderMiniPlan(CaveProjectDocument? project, int decodeWidth = 360) =>
        TryRenderPlan(project, decodeWidth, underlay: null);

    /// <summary>Semi-transparent file B under file A vectors for visual diff.</summary>
    public static ImageSource? TryRenderOverlayPlan(
        CaveProjectDocument? projectA,
        CaveProjectDocument? projectB,
        int decodeWidth = 360,
        double underlayOpacity = OverlayUnderlayOpacity)
    {
        if (projectA == null)
            return TryRenderMiniPlan(projectB, decodeWidth);
        if (projectB == null)
            return TryRenderMiniPlan(projectA, decodeWidth);

        PlanRasterUnderlay? underlay = null;
        var sceneB = PlanSceneBuilder.TryBuild(projectB, SurveyStationGeometry.AndroidViewModePlan);
        if (sceneB != null)
        {
            var bytesB = PlanMapRasterExporter.TryCapturePlanPngHighRes(
                projectB,
                SurveyVisualizationMode.Standard,
                highContrast: false,
                PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan),
                Array.Empty<PlanRasterUnderlay>(),
                zipPath: null,
                baseLongEdgePixels: 960);
            if (bytesB is { Length: > 0 })
            {
                underlay = new PlanRasterUnderlay
                {
                    Bitmap = DecodeBitmap(bytesB),
                    Category = "compare-B",
                    OpacityOverride = underlayOpacity,
                    WorldExtentMetres = new PlanRasterWorldExtentMetres
                    {
                        MinX = sceneB.MinX,
                        MaxX = sceneB.MaxX,
                        MinY = sceneB.MinY,
                        MaxY = sceneB.MaxY,
                    },
                };
            }
        }

        var underlays = underlay != null ? new[] { underlay } : Array.Empty<PlanRasterUnderlay>();
        return TryRenderPlan(projectA, decodeWidth, underlays);
    }

    private static ImageSource? TryRenderPlan(
        CaveProjectDocument? project,
        int decodeWidth,
        IReadOnlyList<PlanRasterUnderlay>? underlay)
    {
        if (project == null)
            return null;

        if (PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan) == null &&
            underlay is not { Count: > 0 })
            return null;

        var bytes = PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            SurveyVisualizationMode.Standard,
            highContrast: false,
            PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan),
            underlay ?? Array.Empty<PlanRasterUnderlay>(),
            zipPath: null,
            baseLongEdgePixels: 960);
        if (bytes == null || bytes.Length == 0)
            return null;

        var img = DecodeBitmap(bytes);
        if (decodeWidth > 0 && img.PixelWidth > decodeWidth)
        {
            var scaled = new TransformedBitmap(img, new ScaleTransform(
                decodeWidth / (double)img.PixelWidth,
                decodeWidth / (double)img.PixelWidth));
            scaled.Freeze();
            return scaled;
        }

        return img;
    }

    private static BitmapImage DecodeBitmap(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = ms;
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
