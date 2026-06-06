using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Mini plan raster for backup comparison dialog.</summary>
public static class CompareBackupPlanPreview
{
    public static ImageSource? TryRenderMiniPlan(CaveProjectDocument? project, int decodeWidth = 360)
    {
        if (project == null)
            return null;

        if (PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan) == null)
            return null;

        var bytes = PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            SurveyVisualizationMode.Standard,
            highContrast: false,
            PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan),
            Array.Empty<PlanRasterUnderlay>(),
            zipPath: null);
        if (bytes == null || bytes.Length == 0)
            return null;

        using var ms = new MemoryStream(bytes);
        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = ms;
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.DecodePixelWidth = decodeWidth;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
