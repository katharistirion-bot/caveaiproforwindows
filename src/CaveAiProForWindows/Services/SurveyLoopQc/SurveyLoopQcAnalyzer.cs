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
        return string.Join("; ", parts)
            + ". Office adjust: Loop closure assistant (Compass / WLS plan overrides; raw shots unchanged). "
            + "Web workspace Survey QC → Loop closure offers the same.";
    }

    public static void AppendLoopQcSummary(StringBuilder sb, CaveProjectDocument project)
    {
        var loops = SurveyLoopQcAnalyzer.AnalyzeLoops(project);
        sb.AppendLine(BuildMisclosureSummary(loops));
    }

    /// <summary>
    /// Full Survey QC co-pilot narrative — mirrors Android <c>buildSurveyQcCopilotAnswer</c>
    /// and web <c>buildSurveyQcSummary</c> (shared severity + next-action rules).
    /// </summary>
    public static string BuildCopilotAnswer(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var mains = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        var splays = project.Shots.Count - mains.Count;
        var stations = project.Shots
            .SelectMany(s => new[] { s.FromStation, s.ToStation })
            .Where(id => !string.IsNullOrWhiteSpace(id) && !ShotRecord.IsSplayDestination(id))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var loops = SurveyLoopQcAnalyzer.AnalyzeLoops(project);
        var dup = FindDuplicateMainLegPair(mains);
        var name = string.IsNullOrWhiteSpace(project.Name) ? "this project" : project.Name.Trim();
        var sb = new StringBuilder();
        sb.Append("Survey QC co-pilot for ").Append(name).AppendLine(":");
        sb.Append("• ").Append(mains.Count).Append(" main legs, ")
            .Append(splays).Append(" splays, ")
            .Append(stations).AppendLine(" stations.");
        if (dup is { } pair)
        {
            sb.Append("• Duplicate main ").Append(pair.From).Append('→')
                .Append(pair.To).AppendLine(" — check for a typo before trusting loops.");
        }
        sb.Append("• ").AppendLine(BuildCopilotLoopLine(loops));
        var actions = BuildNextActions(
            mains.Count, stations, splays, loops, hasDuplicateMain: dup != null);
        sb.AppendLine(FormatNextActions(actions));
        sb.Append("Rules-based QC — same thresholds on Android, Windows, and web. Ask Cave AI online for a deeper narrative when entitled.");
        return sb.ToString();
    }

    public static IReadOnlyList<string> BuildNextActions(
        int mainCount,
        int stationCount,
        int splayCount,
        IReadOnlyList<SurveyLoopInfo> loops,
        bool hasDuplicateMain)
    {
        var outList = new List<string>();
        if (mainCount < 1)
        {
            outList.Add("Open the HUD and log your first main leg (From→To, not a splay).");
            return outList.Take(3).ToList();
        }
        if (hasDuplicateMain)
            outList.Add("Resolve the duplicate From→To main before trusting loop QC.");
        if (loops.Count == 0)
        {
            outList.Add("Close a loop: shoot back to a known station so misclosure can be measured.");
            if (stationCount < 3)
                outList.Add("Add at least one more unique station before a meaningful loop.");
            if (splayCount >= 4 && mainCount > 0)
                outList.Add("Tie trailing splays into a main — traverse QC ignores pure splay tails.");
            return outList.Distinct().Take(3).ToList();
        }

        var worst = loops.OrderByDescending(l => l.MisclosureMeters).First();
        var severity = SeverityLabel(worst.MisclosureMeters, worst.PathLengthMeters);
        outList.AddRange(BuildLoopFocusedNextActions(worst, severity));
        if (splayCount >= 6 && outList.Count < 3)
            outList.Add("Review LRUD/splay density near junctions before publication.");
        return outList.Distinct().Take(3).ToList();
    }

    private static IEnumerable<string> BuildLoopFocusedNextActions(SurveyLoopInfo worst, string severity)
    {
        var cycle = worst.StationCycle.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        var focus = cycle.Count >= 2
            ? $"{cycle[0]}↔{(cycle.Count >= 2 ? cycle[^2] : cycle[^1])}"
            : "the closing leg";
        return severity switch
        {
            "excellent" or "good" =>
            [
                $"Keep logging; re-check {focus} only if you add long chords.",
                "Export a QC packet before the next office adjust session.",
            ],
            "review" =>
            [
                $"Re-measure the longest or steepest legs on cycle {string.Join("→", worst.StationCycle)}.",
                "Verify station IDs match the sketch before another closing shot.",
                "Preview Bowditch on Android; finish multi-loop Compass/WLS on Windows or web.",
            ],
            _ =>
            [
                $"Do not publish yet — re-shoot foresight/backsight around {string.Join("→", worst.StationCycle.Take(3))}.",
                $"Check clino sign and tape stretch on the worst loop path (~{Math.Round(worst.PathLengthMeters).ToString(CultureInfo.InvariantCulture)} m).",
                "Use Windows Loop closure / web Survey QC after field re-measurement.",
            ],
        };
    }

    private static string FormatNextActions(IReadOnlyList<string> actions)
    {
        if (actions.Count == 0)
            return "Next actions: none — survey looks healthy for field use.";
        var sb = new StringBuilder("Next actions:");
        for (var i = 0; i < actions.Count; i++)
            sb.Append('\n').Append(i + 1).Append(". ").Append(actions[i]);
        return sb.ToString();
    }

    private static string BuildCopilotLoopLine(IReadOnlyList<SurveyLoopInfo> loops)
    {
        if (loops.Count == 0)
            return "No traverse loops detected yet — log closing shots to known stations to measure misclosure.";
        var worst = loops.OrderByDescending(l => l.MisclosureMeters).First();
        var label = SeverityLabel(worst.MisclosureMeters, worst.PathLengthMeters);
        var inv = CultureInfo.InvariantCulture;
        var ppm = worst.PathLengthMeters > 0
            ? (worst.MisclosureMeters / worst.PathLengthMeters) * 1_000_000
            : 0;
        var line =
            $"{loops.Count} loop{(loops.Count == 1 ? "" : "s")} — worst |Δ|={worst.MisclosureMeters.ToString("0.##", inv)} m ({label}). Worst cycle: {string.Join("→", worst.StationCycle)}";
        if (ppm > 0)
            line += $" (~{Math.Round(ppm).ToString(inv)} ppm)";
        return line + ".";
    }

    private static (string From, string To)? FindDuplicateMainLegPair(IReadOnlyList<ShotRecord> mains)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in mains)
        {
            var key = $"{s.FromStation}\0{s.ToStation}";
            if (!seen.Add(key))
                return (s.FromStation, s.ToStation);
        }
        return null;
    }
}
