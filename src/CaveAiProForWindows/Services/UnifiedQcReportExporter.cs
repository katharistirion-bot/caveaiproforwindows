using System.Globalization;
using System.IO;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurveyAnalysis;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Single export action: office summary TXT, anomaly CSV, loop CSV, and optional integrity/manifest text.
/// </summary>
public static class UnifiedQcReportExporter
{
    public static int ExportToFolder(
        CaveProjectDocument project,
        string folderPath,
        IntegrityReport? integrityReport = null,
        string? backupManifestSummary = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        Directory.CreateDirectory(folderPath);

        var safe = string.Join("_", project.Name.Split(Path.GetInvalidFileNameChars()));
        var written = 0;

        var summaryPath = Path.Combine(folderPath, $"{safe}_qc_summary.txt");
        File.WriteAllBytes(summaryPath, BuildSummaryUtf8Bom(project, integrityReport, backupManifestSummary));
        written++;

        var anomaliesPath = Path.Combine(folderPath, $"{safe}_anomalies.csv");
        File.WriteAllBytes(anomaliesPath, BuildAnomaliesCsvUtf8Bom(project));
        written++;

        var loopsPath = Path.Combine(folderPath, $"{safe}_loops.csv");
        File.WriteAllBytes(loopsPath, BuildLoopsCsvUtf8Bom(project));
        written++;

        if (integrityReport != null || !string.IsNullOrWhiteSpace(backupManifestSummary))
        {
            var integrityPath = Path.Combine(folderPath, $"{safe}_integrity.txt");
            File.WriteAllText(integrityPath, BuildIntegrityText(integrityReport, backupManifestSummary), Encoding.UTF8);
            written++;
        }

        return written;
    }

    internal static byte[] BuildSummaryUtf8Bom(
        CaveProjectDocument project,
        IntegrityReport? integrityReport,
        string? backupManifestSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CAVE AI PRO — unified QC report packet");
        sb.AppendLine($"Generated (local): {DateTimeOffset.Now:O}");
        sb.AppendLine();
        sb.AppendLine(SurveyOfficeReportExporter.BuildSummaryTextOnly(project));
        sb.AppendLine();
        sb.AppendLine("=== Statistical anomalies (MAD scan) ===");
        var anomalies = SurveyAnomalyScanner.Scan(project);
        if (anomalies.Count == 0)
            sb.AppendLine("(none)");
        else
        {
            foreach (var f in anomalies)
                sb.AppendLine(FormatAnomalyLine(f));
        }

        sb.AppendLine();
        sb.AppendLine("=== Loop closure summary ===");
        var loops = SurveyLoopClosureAdjuster.DetectLoops(project);
        if (loops.Count == 0)
            sb.AppendLine("(no closed loops detected)");
        else
        {
            var inv = CultureInfo.InvariantCulture;
            foreach (var loop in loops.OrderByDescending(l => l.MisclosureMeters))
            {
                sb.AppendLine(
                    $"{loop.ClosingLeg}: misclosure {loop.MisclosureMeters.ToString("0.###", inv)} m, " +
                    $"{loop.StationCount} stations, perimeter {loop.TotalLegLength.ToString("0.##", inv)} m");
            }
        }

        if (integrityReport != null || !string.IsNullOrWhiteSpace(backupManifestSummary))
        {
            sb.AppendLine();
            sb.AppendLine("=== Archive integrity ===");
            sb.AppendLine(BuildIntegrityText(integrityReport, backupManifestSummary).TrimEnd());
        }

        sb.AppendLine();
        sb.AppendLine("=== End of unified QC report ===");
        return Utf8Bom(sb.ToString());
    }

    internal static byte[] BuildAnomaliesCsvUtf8Bom(CaveProjectDocument project)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("Kind,StationOrLeg,Severity,ObservedValue,ExpectedOrMedian,RobustSigma,ZScore,Detail");
        foreach (var f in SurveyAnomalyScanner.Scan(project))
        {
            sb.Append(Csv(f.Kind.ToString()));
            sb.Append(',');
            sb.Append(Csv(f.StationOrLeg));
            sb.Append(',');
            sb.Append(Csv(f.Severity.ToString()));
            sb.Append(',');
            sb.Append(f.ObservedValue.ToString("0.####", inv));
            sb.Append(',');
            sb.Append(f.ExpectedOrMedian.ToString("0.####", inv));
            sb.Append(',');
            sb.Append(f.RobustSigma.ToString("0.####", inv));
            sb.Append(',');
            sb.Append(f.ZScore.ToString("0.####", inv));
            sb.Append(',');
            sb.AppendLine(Csv(f.Detail));
        }

        return Utf8Bom(sb.ToString());
    }

    internal static byte[] BuildLoopsCsvUtf8Bom(CaveProjectDocument project)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("ClosingLeg,MisclosureMeters,StationCount,TotalLegLengthM,MeanLegLengthM,MisclosureX,MisclosureY,MisclosureZ,Severity");
        foreach (var loop in SurveyLoopClosureAdjuster.DetectLoops(project).OrderByDescending(l => l.MisclosureMeters))
        {
            var severity = LoopClosureSeverityClassifier.Classify(loop.MisclosureMeters);
            sb.Append(Csv(loop.ClosingLeg));
            sb.Append(',');
            sb.Append(loop.MisclosureMeters.ToString("0.####", inv));
            sb.Append(',');
            sb.Append(loop.StationCount.ToString(inv));
            sb.Append(',');
            sb.Append(loop.TotalLegLength.ToString("0.####", inv));
            sb.Append(',');
            sb.Append(loop.MeanLegLength.ToString("0.####", inv));
            sb.Append(',');
            sb.Append(loop.MisclosureX.ToString("0.####", inv));
            sb.Append(',');
            sb.Append(loop.MisclosureY.ToString("0.####", inv));
            sb.Append(',');
            sb.Append(loop.MisclosureZ.ToString("0.####", inv));
            sb.Append(',');
            sb.AppendLine(Csv(LoopClosureSeverityClassifier.SeverityCaption(severity)));
        }

        return Utf8Bom(sb.ToString());
    }

    internal static string BuildIntegrityText(IntegrityReport? integrityReport, string? backupManifestSummary)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(backupManifestSummary))
        {
            sb.AppendLine(backupManifestSummary.TrimEnd());
            sb.AppendLine();
        }

        if (integrityReport == null)
            return sb.ToString().TrimEnd();

        if (integrityReport.SkippedReason != null)
        {
            sb.AppendLine(integrityReport.SkippedReason);
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine(
            $"SHA-256 manifest: {integrityReport.FilesMatched}/{integrityReport.FilesChecked} matched, " +
            $"{integrityReport.FilesMissingInZip} missing, {integrityReport.Mismatches.Count} mismatch(es).");
        foreach (var line in integrityReport.Mismatches)
            sb.AppendLine("  " + line);

        return sb.ToString().TrimEnd();
    }

    private static string FormatAnomalyLine(SurveyAnomalyFinding f)
    {
        var sev = f.Severity switch
        {
            SurveyAnomalySeverity.Critical => "CRITICAL",
            SurveyAnomalySeverity.Warning => "Warning",
            _ => "Info",
        };
        return $"[{sev}] {f.StationOrLeg}: {f.Detail}";
    }

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static byte[] Utf8Bom(string text)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(text);
        var outBytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, outBytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, outBytes, preamble.Length, body.Length);
        return outBytes;
    }
}
