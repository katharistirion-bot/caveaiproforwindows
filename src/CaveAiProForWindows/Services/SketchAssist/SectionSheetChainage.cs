using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>
/// Maps the Windows section sheet (Dijkstra tape chainage) to the Android / web long profile
/// (horizontal extended distance from <c>calculateExtendedCoordinates</c>).
/// </summary>
public static class SectionSheetChainage
{
    private readonly record struct Axis(float Slope, float Extended);

    public static float ToExtendedX(CaveProjectDocument project, float slopeX) =>
        Map(project, slopeX, toExtended: true);

    public static float ToSlopeX(CaveProjectDocument project, float extendedX) =>
        Map(project, extendedX, toExtended: false);

    private static float Map(CaveProjectDocument project, float x, bool toExtended)
    {
        var axes = Build(project);
        if (axes.Count == 0)
            return x;
        return Interpolate(axes, x, toExtended);
    }

    private static List<Axis> Build(CaveProjectDocument project)
    {
        var slope = SlopeChainage(project);
        var extended = ExtendedChainage(project);
        var axes = new List<Axis>();
        foreach (var (name, slopeX) in slope)
        {
            if (!extended.TryGetValue(name, out var extendedX))
                continue;
            if (float.IsInfinity(slopeX) || float.IsNaN(slopeX) || float.IsNaN(extendedX))
                continue;
            axes.Add(new Axis(slopeX, extendedX));
        }

        return axes;
    }

    private static Dictionary<string, float> SlopeChainage(CaveProjectDocument project)
    {
        var graph = SurveyTraverseGraph.BuildAdjacency(project.Shots);
        if (graph.Count == 0)
            return new Dictionary<string, float>(StringComparer.Ordinal);
        var root = SurveyTraverseGraph.FirstRootStation(graph);
        return SurveyTraverseGraph.DijkstraChainage(graph, root);
    }

    /// <summary>Android <c>calculateExtendedCoordinates</c> horizontal axis.</summary>
    private static Dictionary<string, float> ExtendedChainage(CaveProjectDocument project)
    {
        var coords = new Dictionary<string, (float X, int Cid)>(StringComparer.Ordinal);
        var shots = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        var unresolved = new List<ShotRecord>(shots);
        var nextComponentId = 0;
        var componentIndex = 0;
        const float componentSpacing = 80f;
        var guard = 0;

        void Merge(int cidAnchor, int cidMoved, float expectedX, float movedX)
        {
            var shift = expectedX - movedX;
            foreach (var name in coords.Keys.ToList())
            {
                var c = coords[name];
                if (c.Cid != cidMoved)
                    continue;
                coords[name] = (c.X + shift, cidAnchor);
            }
        }

        while (unresolved.Count > 0 && guard++ < shots.Count * 8 + 4)
        {
            var seed = unresolved[0];
            if (!coords.ContainsKey(seed.FromStation))
            {
                coords[seed.FromStation] = (componentIndex * componentSpacing, nextComponentId++);
                componentIndex++;
            }

            var progressed = true;
            while (progressed)
            {
                progressed = false;
                for (var i = unresolved.Count - 1; i >= 0; i--)
                {
                    var shot = unresolved[i];
                    var hasFrom = coords.TryGetValue(shot.FromStation, out var from);
                    var hasTo = coords.TryGetValue(shot.ToStation, out var to);
                    var cl = shot.Clino * (Math.PI / 180.0);
                    var hDist = (float)(shot.Distance * Math.Cos(cl));

                    if (hasFrom && !hasTo)
                    {
                        coords[shot.ToStation] = (from.X + hDist, from.Cid);
                        unresolved.RemoveAt(i);
                        progressed = true;
                    }
                    else if (!hasFrom && hasTo)
                    {
                        coords[shot.FromStation] = (to.X - hDist, to.Cid);
                        unresolved.RemoveAt(i);
                        progressed = true;
                    }
                    else if (hasFrom && hasTo)
                    {
                        if (from.Cid != to.Cid)
                            Merge(from.Cid, to.Cid, from.X + hDist, to.X);
                        unresolved.RemoveAt(i);
                        progressed = true;
                    }
                }
            }

            if (unresolved.Count > 0 && !coords.ContainsKey(unresolved[0].FromStation))
            {
                coords[unresolved[0].FromStation] = (componentIndex * componentSpacing, nextComponentId++);
                componentIndex++;
            }
        }

        return coords.ToDictionary(kv => kv.Key, kv => kv.Value.X, StringComparer.Ordinal);
    }

    private static float Interpolate(List<Axis> axes, float x, bool toExtended)
    {
        var pts = axes
            .Select(a => (Key: toExtended ? a.Slope : a.Extended, Value: toExtended ? a.Extended : a.Slope))
            .GroupBy(p => p.Key)
            .Select(g => g.First())
            .OrderBy(p => p.Key)
            .ToList();
        if (pts.Count == 0)
            return x;
        if (pts.Count == 1 || x <= pts[0].Key)
            return pts.Count == 1 ? pts[0].Value : Extrapolate(pts[0], pts[1], x);
        if (x >= pts[^1].Key)
            return Extrapolate(pts[^2], pts[^1], x);

        for (var i = 1; i < pts.Count; i++)
        {
            if (x > pts[i].Key)
                continue;
            var a = pts[i - 1];
            var b = pts[i];
            var span = b.Key - a.Key;
            if (Math.Abs(span) < 1e-6f)
                return a.Value;
            var u = (x - a.Key) / span;
            return a.Value + u * (b.Value - a.Value);
        }

        return pts[^1].Value;
    }

    private static float Extrapolate((float Key, float Value) a, (float Key, float Value) b, float x)
    {
        var span = b.Key - a.Key;
        if (Math.Abs(span) < 1e-6f)
            return a.Value;
        var u = (x - a.Key) / span;
        return a.Value + u * (b.Value - a.Value);
    }
}
