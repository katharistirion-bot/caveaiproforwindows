using System.Globalization;
using System.Linq;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Traverse statistics and light QC hints for the QC / stats tab.</summary>
public static class TraverseQcStats
{
    /// <summary>Station rows for the QC grid (coordinates include any plan overrides).</summary>
    public static IReadOnlyList<(string Name, float X, float Y, float Z, int TraverseLegsFrom, int TraverseLegsTo)>
        BuildStationCoordinateSpecs(CaveProjectDocument? p)
    {
        if (p == null)
            return Array.Empty<(string, float, float, float, int, int)>();
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(p);
        if (coords.Count == 0)
            return Array.Empty<(string, float, float, float, int, int)>();

        var fromCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var toCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in p.Shots.Where(x => x.IsTraverseLeg))
        {
            if (!string.IsNullOrEmpty(s.FromStation))
                fromCount[s.FromStation] = fromCount.GetValueOrDefault(s.FromStation) + 1;
            if (!string.IsNullOrEmpty(s.ToStation))
                toCount[s.ToStation] = toCount.GetValueOrDefault(s.ToStation) + 1;
        }

        return coords.Values
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => (
                c.Name,
                c.X,
                c.Y,
                c.Z,
                fromCount.GetValueOrDefault(c.Name),
                toCount.GetValueOrDefault(c.Name)))
            .ToList();
    }

    public static string BuildSummaryText(CaveProjectDocument? p)
    {
        if (p == null)
            return "Select a project.";
        var shots = p.Shots;
        var trav = shots.Where(s => s.IsTraverseLeg).ToList();
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(p);
        if (coords.Count == 0)
            return "No traverse shots (to ≠ \"-\") for statistics.";

        var sumH = 0.0;
        var sumTape = 0.0;
        var maxDepth = double.MinValue;
        var minDepth = double.MaxValue;
        foreach (var s in trav)
        {
            var cl = s.Clino * (Math.PI / 180.0);
            sumTape += s.Distance;
            sumH += s.Distance * Math.Cos(cl);
            maxDepth = Math.Max(maxDepth, s.Depth);
            minDepth = Math.Min(minDepth, s.Depth);
        }

        var spanZ = coords.Values.Max(c => c.Z) - coords.Values.Min(c => c.Z);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"Project: {p.Name}");
        sb.AppendLine($"Stations (traverse graph): {coords.Count}");
        sb.AppendLine($"Traverse shots: {trav.Count}  ·  Other shots: {shots.Count - trav.Count}");
        sb.AppendLine($"Sum tape (as recorded): {sumTape.ToString("0.##", inv)} m");
        sb.AppendLine($"Sum horizontal leg projections (sum of distance·cos(clino)): {sumH.ToString("0.##", inv)} m");
        sb.AppendLine($"Shot depth (min…max from depth field): {minDepth.ToString("0.##", inv)} … {maxDepth.ToString("0.##", inv)} m");
        sb.AppendLine($"Station Z span: {spanZ.ToString("0.##", inv)} m");
        if (sumTape > 0.001)
        {
            var ratio = sumH / sumTape;
            sb.AppendLine(
                $"Plan projection ratio (sum distance·cos(clino) / sum tape): {ratio.ToString("0.###", inv)}  (→1 if flat legs, lower if steep).");
        }

        AppendGraphQc(sb, trav, inv);
        AppendInstrumentQcSummary(sb, p, trav, inv);

        return sb.ToString().TrimEnd();
    }

    /// <summary>Topology / graph QC lines for the main SURVEY QC tab (same rules as the QC section of <see cref="BuildSummaryText"/>).</summary>
    public static IReadOnlyList<SurveyQcIssueRow> BuildSurveyQcIssueRows(CaveProjectDocument? p)
    {
        var rows = new List<SurveyQcIssueRow>();
        if (p == null)
        {
            rows.Add(new SurveyQcIssueRow("Info", "Select a project in the list."));
            return rows;
        }

        var trav = p.Shots.Where(s => s.IsTraverseLeg).ToList();
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(p);
        if (coords.Count == 0)
        {
            rows.Add(new SurveyQcIssueRow(
                "Info",
                "No traverse graph yet (need traverse legs with to ≠ \"-\") — topology checks run once a centerline exists."));
            return rows;
        }

        var inv = CultureInfo.InvariantCulture;
        foreach (var line in EnumerateGraphQcLines(trav, inv))
            rows.Add(new SurveyQcIssueRow("Topology QC", line));

        AppendInstrumentQcRows(rows, p, trav, inv);

        if (!rows.Any(r => r.Category is "Topology QC" or "Instrument QC"))
            rows.Add(new SurveyQcIssueRow("OK", "No traverse topology warnings — duplicate pairs, zero-length legs, az/clino range, disconnected components, and bidirectional pairs all look clear."));

        return rows;
    }

    /// <summary>Compact QC badge for the main project list (topology warnings vs OK vs no centerline).</summary>
    public static ProjectQcBadgeInfo ComputeProjectListBadge(CaveProjectDocument? p)
    {
        if (p == null)
            return ProjectQcBadgeInfo.Empty;

        var rows = BuildSurveyQcIssueRows(p);
        if (rows.Count == 1 && rows[0].Category == "Info")
        {
            var m = rows[0].Message;
            if (m.Contains("Select a project", StringComparison.OrdinalIgnoreCase))
                return ProjectQcBadgeInfo.Empty;
            if (m.Contains("No traverse graph", StringComparison.OrdinalIgnoreCase)
                || m.Contains("No traverse", StringComparison.OrdinalIgnoreCase))
            {
                return new ProjectQcBadgeInfo
                {
                    Glyph = "—",
                    Summary = m,
                    AccentBrushHex = "#8A9099",
                };
            }
        }

        var warnMsgs = rows
            .Where(r => r.Category is "Topology QC" or "Instrument QC")
            .Select(r => r.Message)
            .Take(8)
            .ToList();
        if (warnMsgs.Count > 0)
        {
            return new ProjectQcBadgeInfo
            {
                Glyph = "!",
                Summary = string.Join("\n", warnMsgs),
                AccentBrushHex = "#C45C26",
            };
        }

        var ok = rows.FirstOrDefault(r => r.Category == "OK");
        if (ok != null)
        {
            return new ProjectQcBadgeInfo
            {
                Glyph = "\u2713",
                Summary = ok.Message,
                AccentBrushHex = "#2A8A6E",
            };
        }

        return new ProjectQcBadgeInfo
        {
            Glyph = "?",
            Summary = string.Join("\n", rows.Select(r => $"{r.Category}: {r.Message}").Take(6)),
            AccentBrushHex = "#7A7F88",
        };
    }

    /// <summary>Undirected traverse graph: duplicate directed legs, tiny distances, disconnected pieces.</summary>
    private static void AppendGraphQc(StringBuilder sb, IReadOnlyList<ShotRecord> trav, CultureInfo inv)
    {
        foreach (var line in EnumerateGraphQcLines(trav, inv))
            sb.AppendLine(line);
    }

    private static void AppendInstrumentQcSummary(StringBuilder sb, CaveProjectDocument p, IReadOnlyList<ShotRecord> trav, CultureInfo inv)
    {
        var tmp = new List<SurveyQcIssueRow>();
        AppendInstrumentQcRows(tmp, p, trav, inv);
        foreach (var r in tmp)
            sb.AppendLine(r.Message);
    }

    private static void AppendInstrumentQcRows(
        List<SurveyQcIssueRow> rows,
        CaveProjectDocument p,
        IReadOnlyList<ShotRecord> trav,
        CultureInfo inv)
    {
        if (p.ExportDeviceContext != null || p.SurveyCalibrationProfile != null)
        {
            var bits = new List<string>();
            if (p.ExportDeviceContext is { } d)
            {
                if (!string.IsNullOrWhiteSpace(d.Manufacturer))
                    bits.Add(d.Manufacturer.Trim());
                if (!string.IsNullOrWhiteSpace(d.Model))
                    bits.Add(d.Model.Trim());
            }

            if (p.SurveyCalibrationProfile is { } c &&
                (!string.IsNullOrWhiteSpace(c.ProfileName) || !string.IsNullOrWhiteSpace(c.ProfileId)))
                bits.Add($"cal:{(c.ProfileName ?? c.ProfileId)!.Trim()}");

            if (bits.Count > 0)
                rows.Add(new SurveyQcIssueRow("Export", $"Instrument context: {string.Join(" · ", bits)}"));
        }

        if (p.StationEnvironmentSnapshots.Count > 0)
            rows.Add(new SurveyQcIssueRow(
                "Export",
                $"Station environment snapshots in JSON: {p.StationEnvironmentSnapshots.Count}"));

        if (p.SurveyAiClassifications.Count > 0)
            rows.Add(new SurveyQcIssueRow(
                "Export",
                $"On-device AI classification rows: {p.SurveyAiClassifications.Count}"));

        if (trav.Count == 0)
            return;

        const float varWarnDeg2 = 25f;
        var hiCompass = trav.Count(s => s.CompassSampleVarianceDeg2 is { } v && v > varWarnDeg2);
        var hiClino = trav.Count(s => s.ClinoSampleVarianceDeg2 is { } v && v > varWarnDeg2);
        if (hiCompass > 0)
            rows.Add(new SurveyQcIssueRow(
                "Instrument QC",
                $"{hiCompass} traverse leg(s) report compassSampleVarianceDeg2 > {varWarnDeg2.ToString(inv)} — review noisy compass holds."));
        if (hiClino > 0)
            rows.Add(new SurveyQcIssueRow(
                "Instrument QC",
                $"{hiClino} traverse leg(s) report clinoSampleVarianceDeg2 > {varWarnDeg2.ToString(inv)} — review clinometer stability."));

        var withWindow = trav.Count(s => s.MeasurementStartedUtcMs is not null || s.MeasurementCompletedUtcMs is not null);
        if (withWindow > 0)
            rows.Add(new SurveyQcIssueRow(
                "Export",
                $"{withWindow} traverse leg(s) include measurement start/end timestamps (UTC ms)."));
    }

    private static IEnumerable<string> EnumerateGraphQcLines(IReadOnlyList<ShotRecord> trav, CultureInfo inv)
    {
        if (trav.Count == 0)
            yield break;

        var dupGroups = trav
            .Where(s => !string.IsNullOrWhiteSpace(s.FromStation) && !string.IsNullOrWhiteSpace(s.ToStation))
            .GroupBy(s => (s.FromStation, s.ToStation), ValueTupleComparer.Instance)
            .Where(g => g.Count() > 1)
            .ToList();
        if (dupGroups.Count > 0)
        {
            var extra = dupGroups.Sum(g => g.Count() - 1);
            yield return
                $"QC: {dupGroups.Count} station pair(s) with duplicate traverse legs (same from→to); {extra} redundant leg(s).";
        }

        const float eps = 1e-4f;
        var zeroLen = trav.Count(s => s.Distance <= eps);
        if (zeroLen > 0)
            yield return $"QC: {zeroLen} traverse shot(s) with zero or near-zero distance (≤ {eps.ToString(inv)} m).";

        var missingAz = trav.Count(s => s.Azimuth < 0f || s.Azimuth > 360f);
        if (missingAz > 0)
            yield return $"QC: {missingAz} traverse shot(s) with azimuth outside 0…360° (check data).";

        var missingCl = trav.Count(s => s.Clino < -90f || s.Clino > 90f);
        if (missingCl > 0)
            yield return $"QC: {missingCl} traverse shot(s) with clino outside ±90° (check data).";

        var components = CountUndirectedTraverseComponents(trav);
        if (components > 1)
            yield return
                $"QC: traverse graph has {components} disconnected component(s) — plan coordinates may place separate chains side-by-side until linked by shots.";

        var bidir = CountBidirectionalLegPairs(trav);
        if (bidir > 0)
            yield return
                $"QC: {bidir} unordered station pair(s) have traverse legs in both directions (A→B and B→A) — check for duplicate or reversed shots.";
    }

    private static int CountBidirectionalLegPairs(IReadOnlyList<ShotRecord> trav)
    {
        static string LegKey(string a, string b) => a + "\x1f" + b;

        var forward = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in trav)
        {
            var a = s.FromStation?.Trim() ?? "";
            var b = s.ToStation?.Trim() ?? "";
            if (a.Length == 0 || b.Length == 0)
                continue;
            forward.Add(LegKey(a, b));
        }

        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in trav)
        {
            var a = s.FromStation?.Trim() ?? "";
            var b = s.ToStation?.Trim() ?? "";
            if (a.Length == 0 || b.Length == 0 || string.Equals(a, b, StringComparison.Ordinal))
                continue;
            if (!forward.Contains(LegKey(b, a)))
                continue;
            var lo = string.CompareOrdinal(a, b) <= 0 ? a : b;
            var hi = string.CompareOrdinal(a, b) <= 0 ? b : a;
            pairs.Add(lo + "\0" + hi);
        }

        return pairs.Count;
    }

    private static int CountUndirectedTraverseComponents(IReadOnlyList<ShotRecord> trav)
    {
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var s in trav)
        {
            var a = s.FromStation?.Trim() ?? "";
            var b = s.ToStation?.Trim() ?? "";
            if (a.Length == 0 || b.Length == 0)
                continue;
            if (!adj.TryGetValue(a, out var setA))
            {
                setA = new HashSet<string>(StringComparer.Ordinal);
                adj[a] = setA;
            }

            if (!adj.TryGetValue(b, out var setB))
            {
                setB = new HashSet<string>(StringComparer.Ordinal);
                adj[b] = setB;
            }

            setA.Add(b);
            setB.Add(a);
        }

        if (adj.Count == 0)
            return 0;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var components = 0;
        foreach (var start in adj.Keys)
        {
            if (!visited.Add(start))
                continue;
            components++;
            var stack = new Stack<string>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var u = stack.Pop();
                if (!adj.TryGetValue(u, out var neigh))
                    continue;
                foreach (var v in neigh)
                {
                    if (visited.Add(v))
                        stack.Push(v);
                }
            }
        }

        return components;
    }

    private sealed class ValueTupleComparer : IEqualityComparer<(string From, string To)>
    {
        public static readonly ValueTupleComparer Instance = new();

        public bool Equals((string From, string To) x, (string From, string To) y) =>
            string.Equals(x.From, y.From, StringComparison.Ordinal)
            && string.Equals(x.To, y.To, StringComparison.Ordinal);

        public int GetHashCode((string From, string To) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.From), StringComparer.Ordinal.GetHashCode(obj.To));
    }
}
