using System.Globalization;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.SurveyLoopQc;

public sealed record SurveyLoopInfo(
    IReadOnlyList<string> StationCycle,
    double MisclosureMeters,
    double PathLengthMeters);

public static class SurveyLoopQcAnalyzer
{
    public const int MaxLoopsReported = 80;

    public static IReadOnlyList<SurveyLoopInfo> AnalyzeLoops(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var legs = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        if (legs.Count < 3)
            return Array.Empty<SurveyLoopInfo>();

        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        string Find(string x)
        {
            if (!parent.ContainsKey(x))
                parent[x] = x;
            var root = x;
            while (!string.Equals(parent[root], root, StringComparison.Ordinal))
                root = parent[root];
            var i = x;
            while (!string.Equals(i, root, StringComparison.Ordinal))
            {
                var next = parent[i];
                parent[i] = root;
                i = next;
            }
            return root;
        }

        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (!string.Equals(ra, rb, StringComparison.Ordinal))
                parent[ra] = rb;
        }

        bool Same(string a, string b) => string.Equals(Find(a), Find(b), StringComparison.Ordinal);

        var treeAdj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var chords = new List<ShotRecord>();
        foreach (var shot in legs)
        {
            var u = shot.FromStation;
            var v = shot.ToStation;
            if (!Same(u, v))
            {
                Union(u, v);
                if (!treeAdj.TryGetValue(u, out var setU))
                {
                    setU = new HashSet<string>(StringComparer.Ordinal);
                    treeAdj[u] = setU;
                }
                if (!treeAdj.TryGetValue(v, out var setV))
                {
                    setV = new HashSet<string>(StringComparer.Ordinal);
                    treeAdj[v] = setV;
                }
                setU.Add(v);
                setV.Add(u);
            }
            else
            {
                chords.Add(shot);
            }
        }

        var outLoops = new List<SurveyLoopInfo>();
        var seenCycles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chord in chords)
        {
            if (outLoops.Count >= MaxLoopsReported)
                break;
            var u = chord.FromStation;
            var v = chord.ToStation;
            var treePath = PathOnSpanningTree(treeAdj, u, v);
            if (treePath == null)
                continue;

            double se = 0, sn = 0, sz = 0, plen = 0;
            var pathComplete = true;
            for (var i = 0; i < treePath.Count - 1; i++)
            {
                var a = treePath[i];
                var b = treePath[i + 1];
                var vec = VectorAlongLeg(legs, a, b);
                if (vec == null)
                {
                    pathComplete = false;
                    break;
                }
                se += vec.Value.Dx;
                sn += vec.Value.Dy;
                sz += vec.Value.Dz;
                var sh = legs.Find(s =>
                    (string.Equals(s.FromStation, a, StringComparison.Ordinal) &&
                     string.Equals(s.ToStation, b, StringComparison.Ordinal)) ||
                    (string.Equals(s.FromStation, b, StringComparison.Ordinal) &&
                     string.Equals(s.ToStation, a, StringComparison.Ordinal)));
                if (sh != null)
                    plen += sh.Distance;
            }
            if (!pathComplete)
                continue;

            var chordVec = VectorAlongLeg(legs, u, v);
            if (chordVec == null)
                continue;
            se -= chordVec.Value.Dx;
            sn -= chordVec.Value.Dy;
            sz -= chordVec.Value.Dz;
            plen += chord.Distance;

            var mag = Math.Sqrt(se * se + sn * sn + sz * sz);
            var cycle = treePath.Count > 0 ? treePath.Append(treePath[0]).ToList() : new List<string>();
            var cycleKey = string.Join("→", cycle);
            if (!seenCycles.Add(cycleKey))
                continue;
            outLoops.Add(new SurveyLoopInfo(cycle, mag, plen));
        }

        return outLoops;
    }

    private static (double Dx, double Dy, double Dz)? VectorAlongLeg(
        IReadOnlyList<ShotRecord> shots,
        string from,
        string to)
    {
        var shot = shots.FirstOrDefault(s =>
            (string.Equals(s.FromStation, from, StringComparison.Ordinal) &&
             string.Equals(s.ToStation, to, StringComparison.Ordinal)) ||
            (string.Equals(s.FromStation, to, StringComparison.Ordinal) &&
             string.Equals(s.ToStation, from, StringComparison.Ordinal)));
        if (shot == null)
            return null;
        var (dx, dy, dz) = ShotVectorEnu(shot);
        return string.Equals(shot.FromStation, from, StringComparison.Ordinal)
            ? (dx, dy, dz)
            : (-dx, -dy, -dz);
    }

    private static (double Dx, double Dy, double Dz) ShotVectorEnu(ShotRecord shot)
    {
        var azRad = shot.Azimuth * (Math.PI / 180.0);
        var clRad = shot.Clino * (Math.PI / 180.0);
        var distance = shot.Distance;
        var hDist = distance * Math.Cos(clRad);
        return (
            hDist * Math.Sin(azRad),
            hDist * Math.Cos(azRad),
            distance * Math.Sin(clRad));
    }

    private static List<string>? PathOnSpanningTree(
        Dictionary<string, HashSet<string>> treeAdj,
        string start,
        string goal)
    {
        if (string.Equals(start, goal, StringComparison.Ordinal))
            return [start];
        var queue = new Queue<List<string>>();
        queue.Enqueue([start]);
        var visited = new HashSet<string>(StringComparer.Ordinal) { start };
        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var cur = path[^1];
            if (!treeAdj.TryGetValue(cur, out var neigh))
                continue;
            foreach (var n in neigh)
            {
                if (!visited.Add(n))
                    continue;
                if (string.Equals(n, goal, StringComparison.Ordinal))
                    return [..path, n];
                queue.Enqueue([..path, n]);
            }
        }
        return null;
    }
}

public static class SurveyLoopQcSummary
{
    public static string SeverityLabel(double misclosureM, double pathLengthM)
    {
        var ppm = pathLengthM > 0 ? (misclosureM / pathLengthM) * 1_000_000 : double.PositiveInfinity;
        if (misclosureM < 0.05)
            return "excellent";
        if (misclosureM < 0.25 && ppm < 500)
            return "good";
        if (misclosureM < 1.0)
            return "review";
        return "large";
    }

    public static string BuildMisclosureSummary(IReadOnlyList<SurveyLoopInfo> loops)
    {
        if (loops.Count == 0)
            return "Loops: none detected — log closing shots to known stations to measure misclosure.";

        var worst = loops.OrderByDescending(l => l.MisclosureMeters).First();
        var label = SeverityLabel(worst.MisclosureMeters, worst.PathLengthMeters);
        var inv = CultureInfo.InvariantCulture;
        var ppm = worst.PathLengthMeters > 0
            ? (worst.MisclosureMeters / worst.PathLengthMeters) * 1_000_000
            : 0;
        var parts = new List<string>
        {
            $"Loops: {loops.Count} detected — worst |Δ|={worst.MisclosureMeters.ToString("0.##", inv)} m ({label})",
            $"cycle {string.Join("→", worst.StationCycle)}",
            $"path ~{Math.Round(worst.PathLengthMeters).ToString(inv)} m",
        };
        if (ppm > 0)
            parts.Add($"~{Math.Round(ppm).ToString(inv)} ppm");
        if (loops.Count > 1)
        {
            var total = loops.Sum(l => l.MisclosureMeters);
            parts.Add($"total |Δ| ~{total.ToString("0.##", inv)} m across {loops.Count} loops");
        }
        return string.Join("; ", parts) + ".";
    }

    public static void AppendLoopQcSummary(StringBuilder sb, CaveProjectDocument project)
    {
        var loops = SurveyLoopQcAnalyzer.AnalyzeLoops(project);
        sb.AppendLine(BuildMisclosureSummary(loops));
    }
}
