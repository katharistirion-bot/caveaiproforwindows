using System.Globalization;
using System.IO;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurveyAnalysis;

namespace CaveAiProForWindows.Services;

public static class SurveyIntelligenceEngine
{
    public static SurveyHealthSnapshot BuildHealth(CaveProjectDocument? project)
    {
        if (project == null)
            return new SurveyHealthSnapshot { HealthSummary = "Open a CaveAI backup and select a project." };

        var inv = CultureInfo.InvariantCulture;
        var shots = project.Shots;
        var mains = shots.Where(s => s.IsTraverseLeg).ToList();
        var splays = shots.Count - mains.Count;
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var traverseM = mains.Sum(s => (double)s.Distance);
        var maxDepth = ComputeMaxDepthBelowEntranceMeters(project);
        var longestBranch = ComputeLongestBranchMeters(mains);
        var loops = SurveyLoopClosureAdjuster.DetectLoops(project);
        var worstLoop = loops.OrderByDescending(l => l.MisclosureMeters).FirstOrDefault();
        var missingLrud = mains.Count(s => { var (l, r, u, d) = s.EffectivePlanLrud(); return l + r + u + d <= 0; });
        var qcWarnings = TraverseQcStats.BuildSurveyQcIssueRows(project).Count(r => r.Category is "Topology QC" or "Instrument QC");
        var anomalies = SurveyAnomalyScanner.Scan(project).Count;
        var entranceLocked = project.Lat.HasValue && project.Lon.HasValue;
        var worstMisclosure = worstLoop?.MisclosureMeters ?? 0;
        var qcPass = qcWarnings == 0 && coords.Count > 0 && worstMisclosure < SurveyAnomalyThresholds.LoopMisclosureCriticalMetres;

        var summary = new StringBuilder();
        summary.Append($"{coords.Count} stations · {mains.Count} legs · depth {maxDepth.ToString("0.#", inv)} m · traverse {traverseM.ToString("0.#", inv)} m");
        if (loops.Count > 0 && worstLoop != null)
            summary.Append($" · worst loop {LoopClosureSeverityClassifier.FormatMisclosureLabel(worstMisclosure)}");
        if (missingLrud > 0) summary.Append($" · {missingLrud} leg(s) missing LRUD");
        if (qcWarnings > 0) summary.Append($" · {qcWarnings} QC warning(s)");

        return new SurveyHealthSnapshot
        {
            ProjectName = project.Name.Trim(),
            StationCount = coords.Count,
            TraverseLegCount = mains.Count,
            SplayCount = splays,
            MaxDepthM = maxDepth,
            TraverseLengthM = traverseM,
            LongestBranchM = longestBranch,
            LoopCount = loops.Count,
            WorstLoopMisclosureM = worstMisclosure,
            WorstLoopLabel = worstLoop?.ClosingLeg ?? "",
            MissingLrudLegCount = missingLrud,
            QcWarningCount = qcWarnings,
            AnomalyCount = anomalies,
            EntranceGpsLocked = entranceLocked,
            QcPassReadyForSketch = qcPass,
            HealthSummary = summary.ToString(),
        };
    }

    public static IReadOnlyList<SurveyIntelligenceInsight> BuildInsights(CaveProjectDocument? project, AndroidSurveyAnalyticsContext? androidContext = null)
    {
        if (project == null)
            return [new SurveyIntelligenceInsight { Category = "Setup", Message = "Load a CaveAI backup ZIP or JSON to begin survey intelligence.", Severity = AiAnalyticsAlertLevel.Info }];

        var insights = new List<SurveyIntelligenceInsight>();
        var health = BuildHealth(project);
        insights.Add(new SurveyIntelligenceInsight { Category = "Overview", Message = health.HealthSummary, Severity = AiAnalyticsAlertLevel.Info });

        if (!health.EntranceGpsLocked)
            insights.Add(new SurveyIntelligenceInsight { Category = "GPS", Message = "No entrance GPS locked — return distance and geo-calibrated maps will be limited.", Severity = AiAnalyticsAlertLevel.Priority });

        if (health.WorstLoopMisclosureM >= SurveyAnomalyThresholds.LoopMisclosureCriticalMetres)
            insights.Add(new SurveyIntelligenceInsight { Category = "Loop", Message = $"Loop at {health.WorstLoopLabel} needs attention — misclosure {LoopClosureSeverityClassifier.FormatMisclosureLabel(health.WorstLoopMisclosureM)} (critical).", Severity = AiAnalyticsAlertLevel.Critical });
        else if (health.WorstLoopMisclosureM >= SurveyAnomalyThresholds.LoopMisclosureWarningMetres)
            insights.Add(new SurveyIntelligenceInsight { Category = "Loop", Message = $"Loop {health.WorstLoopLabel}: misclosure {LoopClosureSeverityClassifier.FormatMisclosureLabel(health.WorstLoopMisclosureM)} — review before publishing.", Severity = AiAnalyticsAlertLevel.Priority });

        if (health.MissingLrudLegCount > 0)
            insights.Add(new SurveyIntelligenceInsight { Category = "LRUD", Message = $"{health.MissingLrudLegCount} traverse leg(s) have no LRUD — volume and sketch walls will be incomplete.", Severity = AiAnalyticsAlertLevel.Priority });

        foreach (var row in TraverseQcStats.BuildSurveyQcIssueRows(project).Where(r => r.Category is "Topology QC" or "Instrument QC").Take(4))
            insights.Add(new SurveyIntelligenceInsight { Category = "QC", Message = row.Message, Severity = AiAnalyticsAlertLevel.Priority });

        var pendingGeo = CaveAiOfflineBrain.CountPendingGeoSamples(project);
        if (pendingGeo > 0)
            insights.Add(new SurveyIntelligenceInsight { Category = "Geo/Bio", Message = $"{pendingGeo} field sample(s) pending geo/bio analysis — open GEO & BIO tab.", Severity = AiAnalyticsAlertLevel.Priority });

        var nextStep = CaveAiOfflineBrain.Answer(project, "what should I do next");
        if (!string.IsNullOrWhiteSpace(nextStep))
            insights.Add(new SurveyIntelligenceInsight { Category = "Action", Message = nextStep.Replace("**", "", StringComparison.Ordinal), Severity = AiAnalyticsAlertLevel.Info });

        if (health.QcPassReadyForSketch)
            insights.Add(new SurveyIntelligenceInsight { Category = "Design", Message = "QC looks clean — open Sketch Editor to draw plan symbols or run procedural LRUD assist.", Severity = AiAnalyticsAlertLevel.Info });

        if (androidContext is { HasObservations: true })
        {
            var leadHints = androidContext.Observations.Count(o => o.Category.Equals("LeadPrediction", StringComparison.OrdinalIgnoreCase));
            if (leadHints > 0)
                insights.Add(new SurveyIntelligenceInsight { Category = "Android", Message = $"{leadHints} Android lead prediction(s) synced — cross-check with open endpoints in findings grid.", Severity = AiAnalyticsAlertLevel.Info });
        }

        return insights.OrderByDescending(i => i.Severity).ThenBy(i => i.Category, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static SurveyBackupDiff CompareWithPreviousBackup(CaveProjectDocument? current, string? syncFolder, string? currentZipPath = null)
    {
        if (current == null || string.IsNullOrWhiteSpace(syncFolder) || !Directory.Exists(syncFolder))
            return new SurveyBackupDiff { Summary = "Configure Desktop Sync folder to compare with previous Android backup." };

        var zips = Directory.EnumerateFiles(syncFolder, "CaveAI_Backup_*.zip", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        if (zips.Count < 2)
            return new SurveyBackupDiff { Summary = zips.Count == 0 ? "No CaveAI_Backup_*.zip in sync folder yet." : "Only one backup ZIP found — comparison available after the next Android export.", CurrentBackupName = zips.FirstOrDefault() != null ? Path.GetFileName(zips[0]) : "" };

        var latestPath = !string.IsNullOrWhiteSpace(currentZipPath) && File.Exists(currentZipPath) ? currentZipPath : zips[0];
        var previousPath = zips.FirstOrDefault(z => !string.Equals(z, latestPath, StringComparison.OrdinalIgnoreCase)) ?? zips[1];

        try
        {
            var previousProjects = ExplorationDataLoader.LoadFromCaveAiBackupZip(previousPath).ToList();
            var prev = previousProjects.FirstOrDefault(p => string.Equals(p.Name.Trim(), current.Name.Trim(), StringComparison.OrdinalIgnoreCase)) ?? previousProjects.FirstOrDefault();
            if (prev == null)
                return new SurveyBackupDiff { HasComparison = false, PreviousBackupName = Path.GetFileName(previousPath), CurrentBackupName = Path.GetFileName(latestPath), Summary = $"Previous backup {Path.GetFileName(previousPath)} has no matching project \"{current.Name}\"." };

            var addedStations = CollectStationNames(current).Except(CollectStationNames(prev), StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var addedLegs = CollectLegKeys(current).Except(CollectLegKeys(prev), StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Select(k => k.Replace('\x1f', '→')).ToList();

            var summary = new StringBuilder($"Since {Path.GetFileName(previousPath)}: ");
            if (addedStations.Count == 0 && addedLegs.Count == 0) summary.Append("no new stations or legs detected (same survey extent).");
            else { if (addedStations.Count > 0) summary.Append($"{addedStations.Count} new station(s)"); if (addedLegs.Count > 0) { if (addedStations.Count > 0) summary.Append(", "); summary.Append($"{addedLegs.Count} new leg(s)"); } summary.Append('.'); }

            return new SurveyBackupDiff { HasComparison = true, PreviousBackupName = Path.GetFileName(previousPath), CurrentBackupName = Path.GetFileName(latestPath), AddedStationCount = addedStations.Count, AddedLegCount = addedLegs.Count, AddedStationNames = addedStations, AddedLegLabels = addedLegs, Summary = summary.ToString() };
        }
        catch (Exception ex)
        {
            return new SurveyBackupDiff { PreviousBackupName = Path.GetFileName(previousPath), Summary = $"Backup comparison failed: {ex.Message}" };
        }
    }

    public static IReadOnlyList<SurveyBatchQcRow> ScanSyncFolderBatchQc(string? syncFolder)
    {
        if (string.IsNullOrWhiteSpace(syncFolder) || !Directory.Exists(syncFolder)) return [];
        return BatchSurveyQcService.ScanFolder(syncFolder).Select(r => new SurveyBatchQcRow { ProjectName = r.ProjectName, SourceFile = Path.GetFileName(r.SourcePath), Shots = r.ShotCount, Anomalies = r.AnomalyCount, Loops = r.LoopCount, Status = !string.IsNullOrWhiteSpace(r.Error) ? $"Error: {r.Error}" : r.IntegritySummary ?? (r.AnomalyCount > 0 ? "Review anomalies" : "OK") }).OrderBy(r => r.ProjectName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<AiAnalyticsMetricRow> BuildCombinedFindings(CaveProjectDocument? project, AndroidSurveyAnalyticsContext? androidContext = null)
    {
        if (project == null) return [new AiAnalyticsMetricRow { Metric = "Status", Value = "No cave project loaded — open a backup and select a project." }];
        var rows = new List<AiAnalyticsMetricRow>();
        var health = BuildHealth(project);
        rows.Add(new AiAnalyticsMetricRow { Metric = "— Project health —", Value = health.HealthSummary });
        var volTotal = AiLocalSurveyAnalytics.BuildResultRows("Volume", project, androidContext).FirstOrDefault(r => r.Metric.StartsWith("Total passage", StringComparison.Ordinal));
        if (volTotal != null) rows.Add(new AiAnalyticsMetricRow { Metric = "Passage volume (LRUD prism)", Value = volTotal.Value, AlertLevel = AiAnalyticsAlertLevel.Info });
        rows.AddRange(AiLocalSurveyAnalytics.BuildResultRows("Qc", project, androidContext).Where(r => r.IsPriorityAlert || r.AlertLevel == AiAnalyticsAlertLevel.Critical).Take(40));
        rows.AddRange(AiLocalSurveyAnalytics.BuildResultRows("Lead", project, androidContext).Where(r => r.AlertLevel >= AiAnalyticsAlertLevel.Priority).Take(15));
        return rows;
    }

    private static HashSet<string> CollectStationNames(CaveProjectDocument project)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in project.Shots.Where(x => x.IsTraverseLeg))
        { if (!string.IsNullOrWhiteSpace(s.FromStation)) set.Add(s.FromStation.Trim()); if (!string.IsNullOrWhiteSpace(s.ToStation)) set.Add(s.ToStation.Trim()); }
        return set;
    }

    private static HashSet<string> CollectLegKeys(CaveProjectDocument project)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in project.Shots.Where(x => x.IsTraverseLeg))
        { var a = s.FromStation.Trim(); var b = s.ToStation.Trim(); if (a.Length > 0 && b.Length > 0) set.Add($"{a}\x1f{b}"); }
        return set;
    }

    private static double ComputeMaxDepthBelowEntranceMeters(CaveProjectDocument project)
    {
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        if (coords.Count == 0) return 0;
        var entranceAlt = project.Alt; var maxBelow = 0.0;
        foreach (var (_, c) in coords) { var below = entranceAlt - c.Z; if (below > maxBelow) maxBelow = below; }
        return maxBelow;
    }

    private static double ComputeLongestBranchMeters(IReadOnlyList<ShotRecord> mains)
    {
        if (mains.Count == 0) return 0;
        var adj = new Dictionary<string, List<(string Neighbor, double Dist)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in mains)
        {
            var a = s.FromStation.Trim(); var b = s.ToStation.Trim(); if (a.Length == 0 || b.Length == 0) continue; var d = (double)s.Distance;
            if (!adj.TryGetValue(a, out var listA)) { listA = new List<(string, double)>(); adj[a] = listA; }
            if (!adj.TryGetValue(b, out var listB)) { listB = new List<(string, double)>(); adj[b] = listB; }
            listA.Add((b, d)); listB.Add((a, d));
        }
        if (adj.Count == 0) return 0;
        var root = mains[0].FromStation.Trim(); if (!adj.ContainsKey(root)) root = adj.Keys.First();
        var best = 0.0; var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); DfsLongest(root, 0, adj, visited, ref best); return best;
    }

    private static void DfsLongest(string node, double dist, Dictionary<string, List<(string Neighbor, double Dist)>> adj, HashSet<string> visited, ref double best)
    {
        if (dist > best) best = dist;
        if (!visited.Add(node)) return;
        if (adj.TryGetValue(node, out var neigh)) foreach (var (n, d) in neigh) DfsLongest(n, dist + d, adj, visited, ref best);
        visited.Remove(node);
    }
}
