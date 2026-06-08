using System.IO;
using CaveAiProForWindows.Models;
using SkiaSharp;

namespace CaveAiProForWindows.Services;

/// <summary>Cave report PDF: plan, section, 3D, symbol inventory, and traverse QC summary.</summary>
public static class CaveSurveyReportPdfExporter
{
    public static void Export(CaveProjectDocument project, string pdfPath, string? zipPath = null)
    {
        var pages = new List<byte[]>();

        var plan = PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            SurveyVisualizationMode.Standard,
            highContrast: false,
            PlanCanvasDrawOptionsFactory.ForExportPrint(SurveyCanvasKind.Plan, project: project),
            Array.Empty<PlanRasterUnderlay>(),
            zipPath,
            quality: MapExportQuality.Print);
        if (plan != null)
            pages.Add(plan);

        var section = PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            SurveyVisualizationMode.Standard,
            highContrast: false,
            PlanCanvasDrawOptionsFactory.ForExportPrint(SurveyCanvasKind.Section, project: project),
            Array.Empty<PlanRasterUnderlay>(),
            zipPath,
            vectorViewMode: SurveyStationGeometry.AndroidViewModeSection,
            quality: MapExportQuality.Print);
        if (section != null)
            pages.Add(section);

        var d3 = PlanMapRasterExporter.TryCapture3DPng(project, 1600, 1200);
        if (d3 != null)
            pages.Add(d3);

        pages.Add(RenderTextPage(BuildSymbolInventoryPage(project)));
        pages.Add(RenderTextPage(BuildQcSummaryPage(project)));

        if (pages.Count == 0)
            throw new InvalidOperationException("No report pages could be rendered.");

        CaveSurveyBookletExportService.SavePagesAsPdf(pages, pdfPath);
    }

    private static string BuildSymbolInventoryPage(CaveProjectDocument project)
    {
        var title = CaveProjectDisplayNames.GetDisplayName(project);
        return $"{title}\n\n{MapSymbolInventoryFormatter.BuildInventoryText(project)}";
    }

    private static string BuildQcSummaryPage(CaveProjectDocument project)
    {
        var title = CaveProjectDisplayNames.GetDisplayName(project);
        return $"{title}\n\n{TraverseQcStats.BuildSummaryText(project)}";
    }

    private static byte[] RenderTextPage(string text)
    {
        const int width = 1200;
        const int height = 1600;
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 28);
        using var bodyFont = new SKFont(SKTypeface.FromFamilyName("Consolas"), 18);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };

        var y = 48f;
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var font = y < 80 ? titleFont : bodyFont;
            paint.Color = y < 80 ? SKColors.Black : new SKColor(0x33, 0x33, 0x33);
            canvas.DrawText(rawLine, 40, y, SKTextAlign.Left, font, paint);
            y += font.Size + 8;
            if (y > height - 40)
                break;
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        return data.ToArray();
    }
}
