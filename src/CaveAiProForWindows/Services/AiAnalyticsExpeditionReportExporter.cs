using System.Globalization;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public static class AiAnalyticsExpeditionReportExporter
{
    public static byte[] BuildMarkdownUtf8(
        SurveyHealthSnapshot health,
        IReadOnlyList<SurveyIntelligenceInsight> insights,
        SurveyBackupDiff backupDiff,
        IReadOnlyList<SurveyBatchQcRow> batchQc,
        IReadOnlyList<AiAnalyticsMetricRow> findings,
        string? projectName = null)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(projectName) ? health.ProjectName : projectName!.Trim();
        sb.AppendLine($"# CaveAI Survey Intelligence - {title}");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm} (local, offline)");
        sb.AppendLine();
        sb.AppendLine("## Project health");
        sb.AppendLine();
        sb.AppendLine($"- Stations: {health.StationCount}");
        sb.AppendLine($"- Traverse legs: {health.TraverseLegCount} / Splays: {health.SplayCount}");
        sb.AppendLine($"- Max depth: {health.MaxDepthM.ToString("0.#", inv)} m");
        sb.AppendLine($"- Traverse length: {health.TraverseLengthM.ToString("0.#", inv)} m");
        sb.AppendLine($"- Longest branch: {health.LongestBranchM.ToString("0.#", inv)} m");
        sb.AppendLine($"- Loops: {health.LoopCount} / Worst misclosure: {LoopClosureSeverityClassifier.FormatMisclosureLabel(health.WorstLoopMisclosureM)}");
        sb.AppendLine($"- Missing LRUD legs: {health.MissingLrudLegCount}");
        sb.AppendLine($"- QC warnings: {health.QcWarningCount} / Anomalies: {health.AnomalyCount}");
        sb.AppendLine($"- Entrance GPS: {(health.EntranceGpsLocked ? "locked" : "not locked")}");
        sb.AppendLine($"- Sketch-ready QC: {(health.QcPassReadyForSketch ? "yes" : "no")}");
        sb.AppendLine();
        sb.AppendLine("## Offline insights");
        sb.AppendLine();
        foreach (var i in insights)
            sb.AppendLine($"- **{i.Category}** ({i.Severity}): {i.Message}");
        sb.AppendLine();
        sb.AppendLine("## Backup comparison");
        sb.AppendLine();
        sb.AppendLine(backupDiff.Summary);
        sb.AppendLine();
        if (batchQc.Count > 0)
        {
            sb.AppendLine("## Sync folder batch QC");
            sb.AppendLine();
            foreach (var r in batchQc)
                sb.AppendLine($"- {r.ProjectName} ({r.SourceFile}): shots={r.Shots}, anomalies={r.Anomalies}, loops={r.Loops}, status={r.Status}");
            sb.AppendLine();
        }
        sb.AppendLine("## Priority findings");
        sb.AppendLine();
        foreach (var f in findings.Where(r => r.IsPriorityAlert || r.AlertLevel == AiAnalyticsAlertLevel.Critical))
            sb.AppendLine($"- {f.Station}: {f.Metric} - {f.Value}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Computed locally - no cloud AI or network calls.*");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}