using System.IO;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Writes standard survey exports for every loaded project into one folder (office / archive workflow).</summary>
public static class SurveyBatchExporter
{
    /// <summary>
    /// Exports each project with files <c>{index:00}_{safeName}_*.csv|.svx|.th</c>. Returns counts and any per-file errors.
    /// </summary>
    public static BatchExportResult ExportAllToFolder(
        IReadOnlyList<CaveProjectDocument> projects,
        string folderPath,
        bool includeShotsCsv = true,
        bool includeSurvexTherionStations = true)
    {
        var written = new List<string>();
        var errors = new List<string>();
        if (projects.Count == 0)
            return new BatchExportResult(0, 0, written, errors);

        Directory.CreateDirectory(folderPath);
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        for (var i = 0; i < projects.Count; i++)
        {
            var p = projects[i];
            var ord = i + 1;
            var seg = SafeFileSegment(p.Name);
            var prefix = string.Format(inv, "{0:00}_{1}", ord, seg);
            var projectDir = Path.Combine(folderPath, prefix);
            Directory.CreateDirectory(projectDir);

            if (includeShotsCsv && p.Shots.Count > 0)
            {
                var path = Path.Combine(projectDir, prefix + "_shots.csv");
                TryWrite(() => File.WriteAllBytes(path, ExplorationAnalytics.ExportShotsToCsvUtf8Bom(p)), path, written, errors);
            }

            if (!includeSurvexTherionStations || !p.Shots.Any(s => s.IsTraverseLeg))
                continue;

            var svx = Path.Combine(projectDir, prefix + "_traverse.svx");
            TryWrite(() => File.WriteAllBytes(svx, SurvexExporter.BuildSvxUtf8Bom(p)), svx, written, errors);

            var th = Path.Combine(projectDir, prefix + "_traverse.th");
            TryWrite(() => File.WriteAllBytes(th, TherionExporter.BuildCenterlineThUtf8Bom(p)), th, written, errors);

            var st = Path.Combine(projectDir, prefix + "_stations_xyz.csv");
            TryWrite(() => File.WriteAllBytes(st, StationCoordinatesCsvExporter.BuildUtf8Bom(p)), st, written, errors);
        }

        return new BatchExportResult(projects.Count, written.Count, written, errors);
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

    private static string SafeFileSegment(string? name)
    {
        var t = (name ?? "").Trim();
        if (t.Length == 0)
            return "unnamed";
        var parts = t.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join("_", parts).Trim('_');
        if (joined.Length == 0)
            return "unnamed";
        if (joined.Length > 80)
            joined = joined[..80].TrimEnd('_');
        return joined;
    }
}

public sealed record BatchExportResult(
    int ProjectCount,
    int FilesWritten,
    IReadOnlyList<string> WrittenPaths,
    IReadOnlyList<string> Errors);
