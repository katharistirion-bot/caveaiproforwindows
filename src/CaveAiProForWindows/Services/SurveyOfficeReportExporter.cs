using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Single text report for archive / peer review: project summary plus traverse QC block.</summary>
public static class SurveyOfficeReportExporter
{
    public static byte[] BuildUtf8Bom(CaveProjectDocument project)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CAVE AI PRO — survey & QC report");
        sb.AppendLine($"Generated (local): {DateTimeOffset.Now:O}");
        sb.AppendLine();
        sb.AppendLine(BuildSummaryTextOnly(project).TrimEnd());
        sb.AppendLine();
        sb.AppendLine("=== End of report ===");

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var outBytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, outBytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, outBytes, preamble.Length, body.Length);
        return outBytes;
    }

    /// <summary>Core sections shared with <see cref="UnifiedQcReportExporter"/>.</summary>
    public static string BuildSummaryTextOnly(CaveProjectDocument project)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Project summary ===");
        sb.AppendLine(ExplorationAnalytics.BuildSummaryText(project).TrimEnd());
        sb.AppendLine();
        sb.AppendLine("=== Traverse statistics & QC ===");
        sb.AppendLine(TraverseQcStats.BuildSummaryText(project).TrimEnd());
        sb.AppendLine();
        sb.AppendLine("=== Map symbol inventory ===");
        sb.AppendLine(MapSymbolInventoryFormatter.BuildInventoryText(project).TrimEnd());
        sb.AppendLine();
        sb.AppendLine("=== Field catalog (Geo / Bio / organisms) ===");
        sb.AppendLine(FieldCatalogAnalyticsFormatter.BuildSummaryLine(GeoBioRecordsService.Build(project)).TrimEnd());
        sb.AppendLine();
        sb.AppendLine("=== Map survey overlays ===");
        sb.AppendLine(SurveyAnnotationReportFormatter.BuildOverlaySummary(project).TrimEnd());
        return sb.ToString();
    }
}
