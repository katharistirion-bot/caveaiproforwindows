using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Cumulative distance from a survey “origin” station along the <b>shortest path</b> in the traverse graph
/// (edge weight = tape length). Origin is the <c>fromStation</c> of the first traverse leg in project shot order.
/// </summary>
public sealed class TraverseChainageResult
{
    public TraverseChainageResult(string rootStation, IReadOnlyDictionary<string, double> cumulativeMetresFromRoot)
    {
        RootStation = rootStation;
        CumulativeMetresFromRoot = cumulativeMetresFromRoot;
    }

    public string RootStation { get; }

    /// <summary>Trimmed station name → cumulative tape metres from <see cref="RootStation"/>.</summary>
    public IReadOnlyDictionary<string, double> CumulativeMetresFromRoot { get; }

    public bool TryGetChainage(string stationName, out double metres)
    {
        metres = 0;
        var key = (stationName ?? "").Trim();
        if (key.Length == 0)
            return false;
        if (CumulativeMetresFromRoot.TryGetValue(key, out metres))
            return true;
        var match = CumulativeMetresFromRoot.Keys.FirstOrDefault(
            k => string.Equals(k.Trim(), key, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return false;
        metres = CumulativeMetresFromRoot[match];
        return true;
    }
}

public static class SurveyTraverseChainage
{
    public static TraverseChainageResult? TryCompute(CaveProjectDocument? project)
    {
        if (project?.Shots == null)
            return null;

        var legs = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        if (legs.Count == 0)
            return null;

        var root = (legs[0].FromStation ?? "").Trim();
        if (root.Length == 0)
            return null;

        var adj = new Dictionary<string, List<(string To, double W)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var leg in legs)
        {
            var a = (leg.FromStation ?? "").Trim();
            var b = (leg.ToStation ?? "").Trim();
            if (a.Length == 0 || b.Length == 0)
                continue;
            var w = (double)leg.Distance;
            if (w < 0 || double.IsNaN(w) || double.IsInfinity(w))
                w = 0;
            AddEdge(adj, a, b, w);
            AddEdge(adj, b, a, w);
        }

        if (!adj.ContainsKey(root))
            return null;

        var dist = Dijkstra(adj, root);
        return new TraverseChainageResult(root, dist);
    }

    private static void AddEdge(Dictionary<string, List<(string To, double W)>> adj, string a, string b, double w)
    {
        if (!adj.TryGetValue(a, out var list))
        {
            list = new List<(string, double)>();
            adj[a] = list;
        }

        list.Add((b, w));
    }

    private static Dictionary<string, double> Dijkstra(
        Dictionary<string, List<(string To, double W)>> adj,
        string root)
    {
        var dist = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var pq = new PriorityQueue<string, double>();
        dist[root] = 0;
        pq.Enqueue(root, 0);

        while (pq.TryDequeue(out var u, out var d))
        {
            if (d > dist.GetValueOrDefault(u, double.PositiveInfinity) + 1e-9)
                continue;
            if (!adj.TryGetValue(u, out var outs))
                continue;
            foreach (var (v, w) in outs)
            {
                var nd = d + w;
                if (!dist.TryGetValue(v, out var old) || nd < old - 1e-12)
                {
                    dist[v] = nd;
                    pq.Enqueue(v, nd);
                }
            }
        }

        return dist;
    }

    public static string FormatChainageLine(TraverseChainageResult? chainage, string stationName, CultureInfo inv)
    {
        if (chainage == null)
            return "";
        if (!chainage.TryGetChainage(stationName, out var m))
            return $"Chainage: not reachable from origin «{chainage.RootStation}» along traverse graph.";
        return $"Chainage from «{chainage.RootStation}» (shortest path): {m.ToString("0.###", inv)} m";
    }

    public static string FormatLegChainageLines(
        TraverseChainageResult? chainage,
        string fromStation,
        string toStation,
        CultureInfo inv)
    {
        if (chainage == null)
            return "";
        var sb = new System.Text.StringBuilder();
        if (chainage.TryGetChainage(fromStation, out var cf))
            sb.AppendLine($"Chainage at «{fromStation.Trim()}»: {cf.ToString("0.###", inv)} m");
        else
            sb.AppendLine($"Chainage at «{fromStation.Trim()}»: not reachable from «{chainage.RootStation}».");
        if (chainage.TryGetChainage(toStation, out var ct))
            sb.AppendLine($"Chainage at «{toStation.Trim()}»: {ct.ToString("0.###", inv)} m");
        else
            sb.AppendLine($"Chainage at «{toStation.Trim()}»: not reachable from «{chainage.RootStation}».");
        return sb.ToString().TrimEnd();
    }
}
