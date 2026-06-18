using System.IO;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurveyAnalysis;

namespace CaveAiProForWindows.Services;

public sealed class BatchSurveyQcResult
{
    public required string SourcePath { get; init; }
    public required string ProjectName { get; init; }
    public int ShotCount { get; init; }
    public int AnomalyCount { get; init; }
    public int LoopCount { get; init; }
    public string? IntegritySummary { get; init; }
    public string? Error { get; init; }
}

/// <summary>Scans a folder of ZIP/JSON backups and produces a unified QC report.</summary>
public static class BatchSurveyQcService
{
    public static IReadOnlyList<BatchSurveyQcResult> ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return [];

        var results = new List<BatchSurveyQcResult>();
        foreach (var path in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".json" && ext != ".zip")
                continue;

            try
            {
                results.AddRange(ScanFile(path));
            }
            catch (Exception ex)
            {
                results.Add(new BatchSurveyQcResult
                {
                    SourcePath = path,
                    ProjectName = Path.GetFileName(path),
                    Error = ex.Message,
                });
            }
        }

        return results;
    }

    public static int ExportReport(IReadOnlyList<BatchSurveyQcResult> results, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var csvPath = Path.Combine(outputFolder, "batch-qc-report.csv");
        var txtPath = Path.Combine(outputFolder, "batch-qc-summary.txt");
        var htmlPath = Path.Combine(outputFolder, "batch-qc-summary.html");

        var sbTxt = new StringBuilder();
        sbTxt.AppendLine("CAVE AI PRO — batch survey QC");
        sbTxt.AppendLine($"Generated: {DateTimeOffset.Now:O}");
        sbTxt.AppendLine($"Projects scanned: {results.Count}");
        sbTxt.AppendLine();

        var sbHtml = new StringBuilder();
        sbHtml.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/>");
        sbHtml.AppendLine("<title>CAVE AI PRO — batch survey QC</title>");
        sbHtml.AppendLine("<style>body{font-family:Segoe UI,sans-serif;margin:24px;color:#111}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:6px 10px;text-align:left}th{background:#f3f4f6}.err{color:#b91c1c}</style>");
        sbHtml.AppendLine("</head><body>");
        sbHtml.AppendLine("<h1>CAVE AI PRO — batch survey QC</h1>");
        sbHtml.AppendLine($"<p>Generated: {DateTimeOffset.Now:O}<br/>Projects scanned: {results.Count}</p>");
        sbHtml.AppendLine("<table><thead><tr><th>Project</th><th>Source</th><th>Shots</th><th>Anomalies</th><th>Loops</th><th>Integrity</th><th>Error</th></tr></thead><tbody>");

        var csv = new StringBuilder();
        csv.AppendLine("source,project,shots,anomalies,loops,integrity,error");

        foreach (var r in results)
        {
            csv.AppendLine(string.Join(",",
                Csv(r.SourcePath),
                Csv(r.ProjectName),
                r.ShotCount.ToString(),
                r.AnomalyCount.ToString(),
                r.LoopCount.ToString(),
                Csv(r.IntegritySummary ?? ""),
                Csv(r.Error ?? "")));

            sbTxt.AppendLine($"• {r.ProjectName} ({Path.GetFileName(r.SourcePath)})");
            if (!string.IsNullOrWhiteSpace(r.Error))
                sbTxt.AppendLine($"  ERROR: {r.Error}");
            else
                sbTxt.AppendLine($"  shots={r.ShotCount}, anomalies={r.AnomalyCount}, loops={r.LoopCount}, integrity={r.IntegritySummary}");

            var errClass = string.IsNullOrWhiteSpace(r.Error) ? "" : " class=\"err\"";
            sbHtml.AppendLine("<tr>");
            sbHtml.AppendLine($"<td{errClass}>{Html(r.ProjectName)}</td>");
            sbHtml.AppendLine($"<td>{Html(Path.GetFileName(r.SourcePath))}</td>");
            sbHtml.AppendLine($"<td>{r.ShotCount}</td>");
            sbHtml.AppendLine($"<td>{r.AnomalyCount}</td>");
            sbHtml.AppendLine($"<td>{r.LoopCount}</td>");
            sbHtml.AppendLine($"<td>{Html(r.IntegritySummary ?? "")}</td>");
            sbHtml.AppendLine($"<td{errClass}>{Html(r.Error ?? "")}</td>");
            sbHtml.AppendLine("</tr>");
        }

        sbHtml.AppendLine("</tbody></table></body></html>");

        File.WriteAllText(csvPath, csv.ToString(), Encoding.UTF8);
        File.WriteAllText(txtPath, sbTxt.ToString(), Encoding.UTF8);
        File.WriteAllText(htmlPath, sbHtml.ToString(), Encoding.UTF8);
        return 3;
    }

    private static IEnumerable<BatchSurveyQcResult> ScanFile(string path)
    {
        List<CaveProjectDocument> projects;
        IntegrityReport? integrity = null;

        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            projects = ExplorationDataLoader.LoadFromCaveAiBackupZip(path).ToList();
            integrity = IntegrityVerifier.VerifyZip(path);
        }
        else
        {
            var json = File.ReadAllText(path);
            projects = ExplorationDataLoader.DeserializeProjectsFromText(json).ToList();
        }

        if (projects.Count == 0)
        {
            yield return new BatchSurveyQcResult
            {
                SourcePath = path,
                ProjectName = Path.GetFileName(path),
                Error = "No projects found",
            };
            yield break;
        }

        foreach (var p in projects)
        {
            var anomalies = SurveyAnomalyScanner.Scan(p);
            var loops = SurveyLoopClosureAdjuster.DetectLoops(p);
            yield return new BatchSurveyQcResult
            {
                SourcePath = path,
                ProjectName = p.Name,
                ShotCount = p.Shots.Count,
                AnomalyCount = anomalies.Count,
                LoopCount = loops.Count,
                IntegritySummary = integrity == null
                    ? null
                    : integrity.IsCompleteSuccess
                        ? "OK"
                        : $"{integrity.Mismatches.Count} mismatch(es), {integrity.FilesMissingInZip} missing",
            };
        }
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string Html(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
