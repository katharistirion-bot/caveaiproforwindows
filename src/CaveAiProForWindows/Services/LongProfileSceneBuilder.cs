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
        var coords3 = SurveyStationGeometry.CalculatePlanCoordinates(p);
        if (coords3.Count == 0)
            return null;

        var graph = SurveyTraverseGraph.BuildAdjacency(p.Shots);
        if (graph.Count == 0)
            return null;

        var root = SurveyTraverseGraph.FirstRootStation(graph);
        var chainage = SurveyTraverseGraph.DijkstraChainage(graph, root);
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

        var wallPolys = SurveyLrudWallGeometry.BuildLongProfileLrudRibbonPolylines(p.Shots, projected).ToList();
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
            $"[LongProfile] stations={projected.Count}, legs={segs.Count}, chainage≈{maxX - minX:0.#} m, z≈{maxY - minY:0.#} m, lrudRibbons={wallPolys.Count}");

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
}
