using System.IO;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Batch PNG/SVG export for all map views of loaded projects.</summary>
public static class SurveyBatchMapExporter
{
    public sealed record BatchMapExportResult(
        int ProjectCount,
        int FilesWritten,
        IReadOnlyList<string> WrittenPaths,
        IReadOnlyList<string> Errors);

    public static BatchMapExportResult ExportAllMapsToFolder(
        IReadOnlyList<CaveProjectDocument> projects,
        string folderPath,
        bool includePlanSvg = true,
        bool includeSectionSvg = true,
        bool includeLongProfileSvg = true,
        bool include3DPng = true)
    {
        var written = new List<string>();
        var errors = new List<string>();
        Directory.CreateDirectory(folderPath);
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        for (var i = 0; i < projects.Count; i++)
        {
            var p = projects[i];
            var prefix = string.Format(inv, "{0:00}_{1}", i + 1, SafeSegment(p.Name));

            if (includePlanSvg)
            {
                var path = Path.Combine(folderPath, prefix + "_plan.svg");
                TryWrite(() =>
                {
                    using var fs = File.Create(path);
                    SurveySvgExporter.WritePlanSvg(
                        p,
                        fs,
                        SurveyStationGeometry.AndroidViewModePlan,
                        SurveyVisualizationMode.Standard,
                        PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan));
                }, path, written, errors);
            }

            if (includeSectionSvg)
            {
                var path = Path.Combine(folderPath, prefix + "_section.svg");
                TryWrite(() =>
                {
                    using var fs = File.Create(path);
                    SurveySvgExporter.WritePlanSvg(
                        p,
                        fs,
                        SurveyStationGeometry.AndroidViewModeSection,
                        SurveyVisualizationMode.Standard,
                        PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Section));
                }, path, written, errors);
            }

            if (includeLongProfileSvg)
            {
                var path = Path.Combine(folderPath, prefix + "_longprofile.svg");
                TryWrite(() =>
                {
                    using var fs = File.Create(path);
                    SurveySvgExporter.WritePlanSvg(
                        p,
                        fs,
                        SurveyStationGeometry.AndroidViewModePlan,
                        SurveyVisualizationMode.LongProfile,
                        PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan, SurveyVisualizationMode.LongProfile));
                }, path, written, errors);
            }

            if (include3DPng)
            {
                var path = Path.Combine(folderPath, prefix + "_3d.png");
                TryWrite(() =>
                {
                    var png = PlanMapRasterExporter.TryCapture3DPng(p, 1600, 1200);
                    if (png == null)
                        throw new InvalidOperationException("Could not render 3D.");
                    File.WriteAllBytes(path, png);
                }, path, written, errors);
            }
        }

        return new BatchMapExportResult(projects.Count, written.Count, written, errors);
    }

    private static void TryWrite(Action write, string path, List<string> written, List<string> errors)
    {
        try
        {
            write();
            written.Add(path);
        }
        catch (Exception ex)
        {
            errors.Add($"{path}: {ex.Message}");
        }
    }

    private static string SafeSegment(string? name)
    {
        var t = (name ?? "").Trim();
        if (t.Length == 0)
            return "unnamed";
        var parts = t.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join("_", parts).Trim('_');
        if (joined.Length == 0)
            return "unnamed";
        return joined.Length > 80 ? joined[..80].TrimEnd('_') : joined;
    }
}
