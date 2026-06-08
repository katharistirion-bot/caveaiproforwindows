using System.IO;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Builds a survey booklet PDF (plan, section, long profile, 3D) for one project.</summary>
public static class CaveSurveyBookletExportService
{
    public static void ExportBooklet(CaveProjectDocument project, string pdfPath)
    {
        var pages = new List<byte[]>();

        var plan = PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            SurveyVisualizationMode.Standard,
            highContrast: false,
            PlanCanvasDrawOptionsFactory.ForExportPrint(SurveyCanvasKind.Plan, project: project),
            Array.Empty<PlanRasterUnderlay>(),
            zipPath: null,
            quality: MapExportQuality.Print);
        if (plan != null)
            pages.Add(plan);

        var section = PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            SurveyVisualizationMode.Standard,
            highContrast: false,
            PlanCanvasDrawOptionsFactory.ForExportPrint(SurveyCanvasKind.Section, project: project),
            Array.Empty<PlanRasterUnderlay>(),
            zipPath: null,
            vectorViewMode: SurveyStationGeometry.AndroidViewModeSection,
            quality: MapExportQuality.Print);
        if (section != null)
            pages.Add(section);

        var profile = PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            SurveyVisualizationMode.LongProfile,
            highContrast: false,
            PlanCanvasDrawOptionsFactory.ForExportPrint(SurveyCanvasKind.Plan, SurveyVisualizationMode.LongProfile, project),
            Array.Empty<PlanRasterUnderlay>(),
            zipPath: null,
            quality: MapExportQuality.Print);
        if (profile != null)
            pages.Add(profile);

        var d3 = PlanMapRasterExporter.TryCapture3DPng(project, 1600, 1200);
        if (d3 != null)
            pages.Add(d3);

        if (pages.Count == 0)
            throw new InvalidOperationException("No map pages could be rendered for this project.");

        SavePagesAsPdf(pages, pdfPath);
    }

    public static void SavePagesAsPdf(IReadOnlyList<byte[]> pngPages, string pdfPath)
    {
        if (pngPages.Count == 0)
            throw new InvalidOperationException("No pages to export.");

        using var stream = File.Create(pdfPath);
        using var document = SkiaSharp.SKDocument.CreatePdf(stream);
        const double pointsPerPixel = 72.0 / 96.0;

        foreach (var png in pngPages)
        {
            using var bmp = SkiaSharp.SKBitmap.Decode(png);
            if (bmp == null)
                continue;
            var pw = (float)(bmp.Width * pointsPerPixel);
            var ph = (float)(bmp.Height * pointsPerPixel);
            var canvas = document.BeginPage(pw, ph);
            using var paint = new SkiaSharp.SKPaint { IsAntialias = true };
            canvas.DrawBitmap(bmp, new SkiaSharp.SKRect(0, 0, pw, ph), paint);
            document.EndPage();
        }

        document.Close();
    }
}
