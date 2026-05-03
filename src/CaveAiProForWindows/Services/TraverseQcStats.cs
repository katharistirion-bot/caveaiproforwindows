using System.Globalization;
using System.Linq;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Traverse statistics and light QC hints for the QC / stats tab.</summary>
public static class TraverseQcStats
{
    public static IReadOnlyList<StationQcRow> BuildStationRows(CaveProjectDocument? p)
    {
        if (p == null)
            return Array.Empty<StationQcRow>();
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(p.Shots, (float)p.Alt);
        if (coords.Count == 0)
            return Array.Empty<StationQcRow>();

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
            .Select(c => new StationQcRow(
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
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(shots, (float)p.Alt);
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
                $"Plan projection ratio (Σ distance·cos(clino) / Σ tape): {ratio.ToString("0.###", inv)}  (→1 if flat legs, lower if steep).");
        }

        AppendGraphQc(sb, trav, inv);

        return sb.ToString().TrimEnd();
    }

    /// <summary>Undirected traverse graph: duplicate directed legs, tiny distances, disconnected pieces.</summary>
    private static void AppendGraphQc(StringBuilder sb, IReadOnlyList<ShotRecord> trav, CultureInfo inv)
    {
        if (trav.Count == 0)
            return;

        var dupGroups = trav
            .Where(s => !string.IsNullOrWhiteSpace(s.FromStation) && !string.IsNullOrWhiteSpace(s.ToStation))
            .GroupBy(s => (s.FromStation, s.ToStation), ValueTupleComparer.Instance)
            .Where(g => g.Count() > 1)
            .ToList();
        if (dupGroups.Count > 0)
        {
            var extra = dupGroups.Sum(g => g.Count() - 1);
            sb.AppendLine(
                $"QC: {dupGroups.Count} station pair(s) with duplicate traverse legs (same from→to); {extra} redundant leg(s).");
        }

        const float eps = 1e-4f;
        var zeroLen = trav.Count(s => s.Distance <= eps);
        if (zeroLen > 0)
            sb.AppendLine($"QC: {zeroLen} traverse shot(s) with zero or near-zero distance (≤ {eps.ToString(inv)} m).");

        var missingAz = trav.Count(s => s.Azimuth is < 0 or > 360);
        if (missingAz > 0)
            sb.AppendLine($"QC: {missingAz} traverse shot(s) with azimuth outside 0…360° (check data).");

        var missingCl = trav.Count(s => s.Clino is < -90 or > 90);
        if (missingCl > 0)
            sb.AppendLine($"QC: {missingCl} traverse shot(s) with clino outside ±90° (check data).");

        var components = CountUndirectedTraverseComponents(trav);
        if (components > 1)
            sb.AppendLine(
                $"QC: traverse graph has {components} disconnected component(s) — plan coordinates may place separate chains side-by-side until linked by shots.");

        var bidir = CountBidirectionalLegPairs(trav);
        if (bidir > 0)
            sb.AppendLine(
                $"QC: {bidir} unordered station pair(s) have traverse legs in both directions (A→B and B→A) — check for duplicate or reversed shots.");
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
