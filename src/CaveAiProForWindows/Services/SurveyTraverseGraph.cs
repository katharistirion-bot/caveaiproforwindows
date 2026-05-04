using System.Linq;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Undirected traverse graph utilities (developed distance / chainage along tape legs).
/// </summary>
public static class SurveyTraverseGraph
{
    public static Dictionary<string, List<(string to, float dist)>> BuildAdjacency(IReadOnlyList<ShotRecord> shots)
    {
        var g = new Dictionary<string, List<(string, float)>>(StringComparer.Ordinal);
        void Add(string a, string b, float d)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || d <= 0)
                return;
            if (!g.TryGetValue(a, out var la))
            {
                la = new List<(string, float)>();
                g[a] = la;
            }

            la.Add((b, d));
        }

        foreach (var s in shots.Where(x => x.IsTraverseLeg))
        {
            Add(s.FromStation, s.ToStation, s.Distance);
            Add(s.ToStation, s.FromStation, s.Distance);
        }

        return g;
    }

    public static Dictionary<string, float> DijkstraChainage(
        Dictionary<string, List<(string to, float dist)>> graph,
        string root)
    {
        const float inf = float.MaxValue * 0.5f;
        var dist = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var k in graph.Keys)
            dist[k] = inf;
        dist[root] = 0;
        var pq = new PriorityQueue<string, float>();
        pq.Enqueue(root, 0);
        while (pq.TryDequeue(out var u, out var du))
        {
            if (du > dist[u] + 1e-4f)
                continue;
            if (!graph.TryGetValue(u, out var nbrs))
                continue;
            foreach (var (v, w) in nbrs)
            {
                var nd = du + w;
                var cur = dist.TryGetValue(v, out var cv) ? cv : inf;
                if (nd < cur - 1e-6f)
                {
                    dist[v] = nd;
                    pq.Enqueue(v, nd);
                }
            }
        }

        return dist;
    }

    public static string FirstRootStation(Dictionary<string, List<(string to, float dist)>> graph) =>
        graph.Keys.OrderBy(s => s, StringComparer.Ordinal).First();
}
