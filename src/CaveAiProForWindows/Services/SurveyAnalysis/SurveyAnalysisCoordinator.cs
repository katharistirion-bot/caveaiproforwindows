using System.Globalization;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SurveyAnalysis;

/// <summary>Facade for offline survey analysis modules (anomalies + loop adjustment).</summary>
public static class SurveyAnalysisCoordinator
{
    public static IReadOnlyList<SurveyQcIssueRow> BuildExtendedQcRows(CaveProjectDocument? project)
    {
        var rows = TraverseQcStats.BuildSurveyQcIssueRows(project).ToList();
        if (project == null)
            return rows;

        var anomalies = SurveyAnomalyScanner.Scan(project);
        foreach (var f in anomalies.Take(12))
        {
            var cat = f.Kind == SurveyAnomalyKind.LoopMisclosure ? "Loop closure" : "Statistical anomaly";
            rows.Add(new SurveyQcIssueRow(cat, FormatFinding(f)));
        }

        var loops = SurveyLoopClosureAdjuster.DetectLoops(project);
        if (loops.Count > 0)
        {
            var inv = CultureInfo.InvariantCulture;
            var totalMis = loops.Sum(l => l.MisclosureMeters);
            rows.Add(new SurveyQcIssueRow(
                "Loop closure",
                $"{loops.Count} loop(s) detected — total misclosure {totalMis.ToString("0.###", inv)} m " +
                $"(local Bowditch/LS adjustment available via SurveyAnalysisCoordinator.AdjustLoops)."));
        }

        return rows;
    }

    public static SurveyLoopAdjustmentResult AdjustLoops(
        CaveProjectDocument project,
        LoopAdjustmentMethod method = LoopAdjustmentMethod.CompassRule) =>
        SurveyLoopClosureAdjuster.Adjust(project, method);

    private static string FormatFinding(SurveyAnomalyFinding f)
    {
        var sev = f.Severity switch
        {
            SurveyAnomalySeverity.Critical => "CRITICAL",
            SurveyAnomalySeverity.Warning => "Warning",
            _ => "Info",
        };
        return $"[{sev}] {f.StationOrLeg}: {f.Detail}";
    }
}
