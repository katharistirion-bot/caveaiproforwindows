using System.Diagnostics;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Long profile: horizontal axis = developed distance along traverse (shortest-path chainage from a root station),
/// vertical axis = station Z (metres, same as <see cref="SurveyStationGeometry.CalculatePlanCoordinates"/>).
/// </summary>
public static class LongProfileSceneBuilder
{
    public static PlanScene? TryBuild(CaveProjectDocument p)
    {
        var coords3 = SurveyStationGeometry.CalculatePlanCoordinates(p.Shots, (float)p.Alt);
        if (coords3.Count == 0)
            return null;

        var graph = BuildTraverseAdjacency(p.Shots);
        if (graph.Count == 0)
            return null;

        var root = graph.Keys.OrderBy(s => s, StringComparer.Ordinal).First();
        var chainage = DijkstraChainage(graph, root);
        var projected = new Dictionary<string, SurveyStationGeometry.StationPlanCoords>(StringComparer.Ordinal);
        foreach (var kv in coords3)
        {
            if (!chainage.TryGetValue(kv.Key, out var s))
                continue;
            var z = kv.Value.Z;
            projected[kv.Key] = new SurveyStationGeometry.StationPlanCoords(kv.Key, s, z, 0f);
        }

        if (projected.Count == 0)
            return null;

        var segs = new List<(float, float, float, float)>();
        foreach (var shot in p.Shots.Where(s => s.IsTraverseLeg))
        {
            if (!projected.TryGetValue(shot.FromStation, out var a) || !projected.TryGetValue(shot.ToStation, out var b))
                continue;
            segs.Add((a.X, a.Y, b.X, b.Y));
        }

        var minX = projected.Values.Min(c => c.X);
        var maxX = projected.Values.Max(c => c.X);
        var minY = projected.Values.Min(c => c.Y);
        var maxY = projected.Values.Max(c => c.Y);

        var wallPolys = SurveyLrudWallGeometry.BuildLongProfileLrudQuads(p.Shots, projected).ToList();
        foreach (var pl in wallPolys)
        {
            foreach (var (x, y) in pl.Points)
            {
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        Debug.WriteLine(
            $"[LongProfile] stations={projected.Count}, legs={segs.Count}, chainage≈{maxX - minX:0.#} m, z≈{maxY - minY:0.#} m, lrudPanels={wallPolys.Count}");

        return new PlanScene
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            Stations = projected,
            TraverseSegments = segs,
            WallPolylines = wallPolys,
            VectorPolylines = Array.Empty<SurveyStationGeometry.PlanVectorPolyline>(),
            Symbols = Array.Empty<SurveyStationGeometry.PlanMapSymbol>(),
            StationAttachedImages = Array.Empty<StationAttachedImageRef>(),
            SplaySegments = Array.Empty<(float, float, float, float)>(),
        };
    }

    private static Dictionary<string, List<(string to, float dist)>> BuildTraverseAdjacency(IReadOnlyList<ShotRecord> shots)
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

    private static Dictionary<string, float> DijkstraChainage(
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
}
