using System.Globalization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Offline-only analytics for <see cref="Views.AiAnalyticsView"/> — geometry fused with Android survey context.</summary>
public static class AiLocalSurveyAnalytics
{
    /// <summary>Standard deviation threshold in degrees (variance threshold is this squared).</summary>
    public const float CompassClinoStdThresholdDeg = 2f;

    private static readonly string[] QcNoteKeywords =
        ["blunder", "misread", "redo", "re-shoot", "reshoot", "loop error", "suspect", "bad shot", "outlier"];

    private static readonly string[] LeadNoteKeywords =
        ["lead", "draft", "opening", "open", "continue", "passage", "blow", "going", "promising"];

    private static readonly string[] LeadConfirmationKeywords =
        ["draft", "opening", "open passage", "continues", "continue", "blows", "promising"];

    public static IReadOnlyList<AiAnalyticsMetricRow> BuildResultRows(
        string toolTag,
        CaveProjectDocument? project,
        AndroidSurveyAnalyticsContext? androidContext = null)
    {
        var inv = CultureInfo.InvariantCulture;
        if (project == null)
        {
            return
            [
                DiagnosticRow("", "", "", "Status", "No cave project loaded — open a backup and select a project."),
            ];
        }

        return toolTag switch
        {
            "Volume" => BuildVolumeDiagnostic(project, androidContext, inv),
            "Lead" => BuildLeadDiagnostic(project, androidContext, inv),
            _ => BuildQcDiagnostic(project, androidContext, inv),
        };
    }

    private static IReadOnlyList<AiAnalyticsMetricRow> BuildVolumeDiagnostic(
        CaveProjectDocument project,
        AndroidSurveyAnalyticsContext? androidContext,
        CultureInfo inv)
    {
        var rows = new List<AiAnalyticsMetricRow>();
        AppendAndroidPrimaryRows(rows, androidContext, includeFilter: o =>
            o.Category.Equals("SurveyNote", StringComparison.OrdinalIgnoreCase) ||
            o.Category.Equals("Environment", StringComparison.OrdinalIgnoreCase));

        rows.Add(SeparatorRow("— Volumetric geometry —"));
        rows.AddRange(BuildVolumeRows(project, inv));
        return rows;
    }

    private static IReadOnlyList<AiAnalyticsMetricRow> BuildQcDiagnostic(
        CaveProjectDocument project,
        AndroidSurveyAnalyticsContext? androidContext,
        CultureInfo inv)
    {
        var rows = new List<AiAnalyticsMetricRow>();
        var geometryByStation = BuildQcGeometryByStation(project);
        var emittedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (androidContext is { HasObservations: true })
        {
            rows.Add(SeparatorRow($"— Android survey context ({androidContext.Observations.Count} observations) —"));

            foreach (var obs in androidContext.Observations.OrderBy(o => o.StationName, StringComparer.OrdinalIgnoreCase))
            {
                var st = obs.StationName;
                geometryByStation.TryGetValue(st, out var geom);
                var geomText = geom?.Summary ?? "";
                var isQcNote = ContainsAnyKeyword(obs.Text, QcNoteKeywords);
                var corroborated = geom != null && isQcNote;

                var alert = corroborated
                    ? AiAnalyticsAlertLevel.Critical
                    : isQcNote
                        ? AiAnalyticsAlertLevel.Priority
                        : AiAnalyticsAlertLevel.Info;

                var metric = FormatAndroidMetric(obs);
                rows.Add(DiagnosticRow(
                    st,
                    obs.Text,
                    geomText,
                    metric,
                    corroborated ? "Geometry QC warning corroborated by field note" : "",
                    alert));

                emittedKeys.Add(RowKey(st, metric, obs.Text));
            }
        }

        foreach (var (station, geom) in geometryByStation.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (androidContext?.ForStation(station).Count > 0)
                continue;

            rows.Add(DiagnosticRow(
                station,
                "",
                geom.Summary,
                "QC geometry warning",
                geom.Detail,
                AiAnalyticsAlertLevel.Priority));
        }

        rows.Add(SeparatorRow("— QC summary —"));
        rows.AddRange(BuildQcSummaryRows(project, androidContext, geometryByStation, inv));
        return SortFindingsPriorityFirst(rows);
    }

    private static IReadOnlyList<AiAnalyticsMetricRow> BuildLeadDiagnostic(
        CaveProjectDocument project,
        AndroidSurveyAnalyticsContext? androidContext,
        CultureInfo inv)
    {
        var rows = new List<AiAnalyticsMetricRow>();
        var openEndpoints = GetOpenEndpointStations(project);
        var emittedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (androidContext is { HasObservations: true })
        {
            rows.Add(SeparatorRow($"— Android survey context ({androidContext.Observations.Count} observations) —"));

            foreach (var obs in androidContext.Observations
                         .OrderBy(o => o.StationName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(o => o.Category, StringComparer.OrdinalIgnoreCase))
            {
                var st = obs.StationName;
                var isOpen = openEndpoints.Contains(st);
                var matchesLead = IsLeadObservation(obs);
                var geomText = isOpen ? "Open endpoint (degree-1 traverse node)" : "";

                AiAnalyticsAlertLevel alert;
                string metric;
                string detail;

                if (isOpen && matchesLead && ContainsAnyKeyword(obs.Text, LeadConfirmationKeywords))
                {
                    alert = AiAnalyticsAlertLevel.Critical;
                    metric = "★ PRIORITY — Confirmed lead (geometry + Android annotation)";
                    detail = "Open traverse endpoint cross-matched with field annotation (draft/opening/continuation).";
                }
                else if (isOpen && matchesLead)
                {
                    alert = AiAnalyticsAlertLevel.Priority;
                    metric = "Confirmed lead (geometry + Android hint)";
                    detail = "Open endpoint aligned with Android lead prediction or annotation.";
                }
                else if (isOpen)
                {
                    alert = AiAnalyticsAlertLevel.Info;
                    metric = "Open endpoint (geometry)";
                    detail = "Degree-1 traverse node — no matching Android lead annotation.";
                }
                else if (matchesLead)
                {
                    alert = AiAnalyticsAlertLevel.Priority;
                    metric = "Android lead hint (no open endpoint)";
                    detail = "Field annotation suggests continuation; geometry does not show an open endpoint.";
                }
                else
                {
                    alert = AiAnalyticsAlertLevel.Info;
                    metric = FormatAndroidMetric(obs);
                    detail = "";
                }

                rows.Add(DiagnosticRow(st, obs.Text, geomText, metric, detail, alert));
                emittedKeys.Add(RowKey(st, metric, obs.Text));
            }
        }

        foreach (var st in openEndpoints.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            if (androidContext?.ForStation(st).Count > 0)
                continue;

            var key = RowKey(st, "Open endpoint (geometry only)", st);
            if (emittedKeys.Contains(key))
                continue;

            rows.Add(DiagnosticRow(
                st,
                "",
                "Open endpoint (degree-1 traverse node)",
                "Lead candidate (geometry only)",
                "No Android annotation at this station.",
                AiAnalyticsAlertLevel.Info));
        }

        rows.Add(SeparatorRow("— Lead summary —"));
        rows.AddRange(BuildLeadSummaryRows(project, androidContext, openEndpoints, inv));
        return SortFindingsPriorityFirst(rows);
    }

    private static void AppendAndroidPrimaryRows(
        List<AiAnalyticsMetricRow> rows,
        AndroidSurveyAnalyticsContext? androidContext,
        Func<AndroidSurveyStationObservation, bool>? includeFilter = null)
    {
        if (androidContext is not { HasObservations: true })
            return;

        rows.Add(SeparatorRow($"— Android survey context ({androidContext.Observations.Count} observations) —"));
        foreach (var obs in androidContext.Observations.OrderBy(o => o.StationName, StringComparer.OrdinalIgnoreCase))
        {
            if (includeFilter != null && !includeFilter(obs))
                continue;

            rows.Add(DiagnosticRow(
                obs.StationName,
                obs.Text,
                "",
                FormatAndroidMetric(obs),
                "",
                AiAnalyticsAlertLevel.Info));
        }
    }

    private static Dictionary<string, QcGeometryFinding> BuildQcGeometryByStation(CaveProjectDocument project)
    {
        var map = new Dictionary<string, QcGeometryFinding>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in project.Shots)
        {
            if (!s.IsTraverseLeg)
                continue;

            var compassStd = EffectiveStdDeg(s.CompassStdDeg, s.CompassSampleVarianceDeg2);
            var clinoStd = EffectiveStdDeg(s.ClinoStdDeg, s.ClinoSampleVarianceDeg2);
            if (compassStd == null && clinoStd == null)
                continue;

            var badCompass = compassStd is > CompassClinoStdThresholdDeg;
            var badClino = clinoStd is > CompassClinoStdThresholdDeg;
            if (!badClino && !badCompass)
                continue;

            var leg = $"{s.FromStation.Trim()} → {s.ToStation.Trim()}";
            var parts = new List<string>();
            if (badCompass && compassStd.HasValue)
                parts.Add($"compass std={compassStd.Value.ToString("0.##", CultureInfo.InvariantCulture)}°");
            if (badClino && clinoStd.HasValue)
                parts.Add($"clino std={clinoStd.Value.ToString("0.##", CultureInfo.InvariantCulture)}°");

            var summary = $"Leg {leg}: {string.Join(", ", parts)}";
            var detail = "Standard deviation exceeds local threshold.";

            foreach (var st in new[] { s.FromStation.Trim(), s.ToStation.Trim() }
                         .Where(n => n.Length > 0 && n != "-")
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!map.TryGetValue(st, out var existing))
                    map[st] = new QcGeometryFinding(summary, detail);
                else
                    map[st] = new QcGeometryFinding($"{existing.Summary} · {summary}", detail);
            }
        }

        return map;
    }

    private static IReadOnlyList<AiAnalyticsMetricRow> BuildQcSummaryRows(
        CaveProjectDocument project,
        AndroidSurveyAnalyticsContext? androidContext,
        Dictionary<string, QcGeometryFinding> geometryByStation,
        CultureInfo inv)
    {
        var critical = geometryByStation.Count;
        var withSensor = project.Shots.Count(s =>
            s.IsTraverseLeg &&
            (EffectiveStdDeg(s.CompassStdDeg, s.CompassSampleVarianceDeg2) != null ||
             EffectiveStdDeg(s.ClinoStdDeg, s.ClinoSampleVarianceDeg2) != null));

        var androidQcFlags = androidContext?.Observations.Count(o => ContainsAnyKeyword(o.Text, QcNoteKeywords)) ?? 0;
        var corroborated = 0;
        if (androidContext != null)
        {
            foreach (var st in geometryByStation.Keys)
            {
                if (androidContext.ForStation(st).Any(o => ContainsAnyKeyword(o.Text, QcNoteKeywords)))
                    corroborated++;
            }
        }

        var summary = new List<AiAnalyticsMetricRow>
        {
            DiagnosticRow("", "", "", "Stations with geometry QC warnings", critical.ToString(inv)),
            DiagnosticRow("", "", "", "Traverse legs with compass/clino QC fields", withSensor.ToString(inv)),
            DiagnosticRow("", "", "", "Threshold",
                $"Standard deviation > {CompassClinoStdThresholdDeg.ToString(inv)}°."),
        };

        if (androidContext is { HasObservations: true })
        {
            summary.Add(DiagnosticRow("", "", "", "Android QC note flags", androidQcFlags.ToString(inv)));
            summary.Add(DiagnosticRow("", "", "", "Geometry warnings corroborated by Android notes",
                corroborated.ToString(inv)));
        }

        return summary;
    }

    private static IReadOnlyList<AiAnalyticsMetricRow> BuildLeadSummaryRows(
        CaveProjectDocument project,
        AndroidSurveyAnalyticsContext? androidContext,
        HashSet<string> openEndpoints,
        CultureInfo inv)
    {
        var androidLeadStations = androidContext == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : androidContext.Observations
                .Where(IsLeadObservation)
                .Select(o => o.StationName)
                .Where(s => !string.IsNullOrWhiteSpace(s) && s != "—")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var confirmed = openEndpoints.Where(androidLeadStations.Contains).ToList();
        var geometryOnly = openEndpoints.Except(confirmed, StringComparer.OrdinalIgnoreCase).ToList();
        var androidOnly = androidLeadStations.Except(openEndpoints, StringComparer.OrdinalIgnoreCase).ToList();

        return
        [
            DiagnosticRow("", "", "", "Open endpoint leads (degree-1)", openEndpoints.Count.ToString(inv)),
            DiagnosticRow("", "", "", "Confirmed with Android annotations", confirmed.Count.ToString(inv)),
            DiagnosticRow("", "", "", "Geometry-only endpoints", geometryOnly.Count.ToString(inv)),
            DiagnosticRow("", "", "", "Android-only lead hints", androidOnly.Count.ToString(inv)),
            DiagnosticRow("", "", "", "Station names (geometry)",
                openEndpoints.Count == 0 ? "—" : TruncateJoin(openEndpoints.OrderBy(s => s), ", ", 2000)),
            DiagnosticRow("", "", "", "Method",
                "Traverse graph degree-1 nodes (excluding symbol \"x\"), cross-referenced with Android station annotations and lead predictions."),
        ];
    }

    private static HashSet<string> GetOpenEndpointStations(CaveProjectDocument project)
    {
        var adj = BuildTraverseAdjacency(project);
        return adj
            .Where(kv => kv.Value.Count == 1 && !IsMarkedClosedStation(kv.Key, project))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsLeadObservation(AndroidSurveyStationObservation obs) =>
        obs.Category.Equals("LeadPrediction", StringComparison.OrdinalIgnoreCase) ||
        obs.Category.Equals("StationAnnotation", StringComparison.OrdinalIgnoreCase) ||
        ContainsAnyKeyword(obs.Text, LeadNoteKeywords) ||
        ContainsAnyKeyword(obs.Category, LeadNoteKeywords);

    private static string FormatAndroidMetric(AndroidSurveyStationObservation obs)
    {
        var metric = obs.Category;
        if (!string.IsNullOrWhiteSpace(obs.Source))
            metric += $" ({obs.Source})";
        return metric;
    }

    private static IReadOnlyList<AiAnalyticsMetricRow> BuildVolumeRows(CaveProjectDocument project, CultureInfo inv)
    {
        double sumM3 = 0;
        var used = 0;
        var skipped = 0;
        foreach (var s in project.Shots)
        {
            if (!s.IsTraverseLeg)
                continue;
            var d = (double)s.Distance;
            if (d <= 0)
            {
                skipped++;
                continue;
            }

            var (l, r, u, dd) = s.EffectivePlanLrud();
            var w = (double)l + (double)r;
            var h = (double)u + (double)dd;
            if (w <= 0 || h <= 0)
            {
                skipped++;
                continue;
            }

            sumM3 += w * h * d;
            used++;
        }

        return
        [
            DiagnosticRow("", "", "", "Total passage estimate (LRUD × leg)", $"{sumM3.ToString("N1", inv)} m³"),
            DiagnosticRow("", "", "", "Traverse legs included", used.ToString(inv)),
            DiagnosticRow("", "", "", "Legs skipped (zero length or zero LRUD)", skipped.ToString(inv)),
            DiagnosticRow("", "", "", "Method",
                "Per-leg prism volume = (L+R)×(U+D)×distance using EffectivePlanLrud."),
        ];
    }

    private static Dictionary<string, HashSet<string>> BuildTraverseAdjacency(CaveProjectDocument project)
    {
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void AddEdge(string a, string b)
        {
            a = a.Trim();
            b = b.Trim();
            if (a.Length == 0 || b.Length == 0)
                return;
            if (!adj.TryGetValue(a, out var setA))
            {
                setA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adj[a] = setA;
            }

            if (!adj.TryGetValue(b, out var setB))
            {
                setB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adj[b] = setB;
            }

            setA.Add(b);
            setB.Add(a);
        }

        foreach (var s in project.Shots)
        {
            if (!s.IsTraverseLeg || s.Distance <= 0)
                continue;
            AddEdge(s.FromStation, s.ToStation);
        }

        return adj;
    }

    private static bool IsMarkedClosedStation(string station, CaveProjectDocument project)
    {
        foreach (var s in project.Shots)
        {
            if (!s.IsTraverseLeg)
                continue;
            var from = s.FromStation.Trim();
            var to = s.ToStation.Trim();
            if (!string.Equals(from, station, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(to, station, StringComparison.OrdinalIgnoreCase))
                continue;

            if ((s.Symbol ?? "").Trim().Equals("x", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static float? EffectiveStdDeg(float? explicitStdDeg, float? sampleVarianceDeg2)
    {
        if (explicitStdDeg is > 0)
            return explicitStdDeg;
        if (sampleVarianceDeg2 is > 0)
            return (float)Math.Sqrt((double)sampleVarianceDeg2.Value);
        return null;
    }

    private static bool ContainsAnyKeyword(string text, string[] keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static string TruncateJoin(IEnumerable<string> parts, string sep, int maxLen)
    {
        var joined = string.Join(sep, parts);
        if (joined.Length <= maxLen)
            return joined;
        return joined[..(maxLen - 3)] + "...";
    }

    private static string RowKey(string station, string metric, string text) =>
        $"{station}|{metric}|{text}";

    private static AiAnalyticsMetricRow DiagnosticRow(
        string station,
        string androidContext,
        string geometryContext,
        string metric,
        string value,
        AiAnalyticsAlertLevel alert = AiAnalyticsAlertLevel.None) => new()
    {
        Station = station,
        AndroidContext = androidContext,
        GeometryContext = geometryContext,
        Metric = metric,
        Value = value,
        AlertLevel = alert,
    };

    private static AiAnalyticsMetricRow SeparatorRow(string label) =>
        DiagnosticRow("", "", "", label, "");

    private static List<AiAnalyticsMetricRow> SortFindingsPriorityFirst(List<AiAnalyticsMetricRow> rows)
    {
        var output = new List<AiAnalyticsMetricRow>(rows.Count);
        var buffer = new List<AiAnalyticsMetricRow>();

        foreach (var row in rows)
        {
            var isSeparator = row.Metric.StartsWith('—');
            var isSummary = isSeparator || (string.IsNullOrEmpty(row.Station) &&
                                            string.IsNullOrEmpty(row.AndroidContext) &&
                                            string.IsNullOrEmpty(row.GeometryContext));

            if (isSummary)
            {
                if (buffer.Count > 0)
                {
                    buffer.Sort((a, b) =>
                    {
                        var alert = b.AlertLevel.CompareTo(a.AlertLevel);
                        return alert != 0
                            ? alert
                            : string.Compare(a.Station, b.Station, StringComparison.OrdinalIgnoreCase);
                    });
                    output.AddRange(buffer);
                    buffer.Clear();
                }

                output.Add(row);
            }
            else
                buffer.Add(row);
        }

        if (buffer.Count > 0)
        {
            buffer.Sort((a, b) =>
            {
                var alert = b.AlertLevel.CompareTo(a.AlertLevel);
                return alert != 0
                    ? alert
                    : string.Compare(a.Station, b.Station, StringComparison.OrdinalIgnoreCase);
            });
            output.AddRange(buffer);
        }

        return output;
    }

    private sealed record QcGeometryFinding(string Summary, string Detail);
}
