using System.Diagnostics;
using System.Text.Json;
using CaveAiProForWindows.Models;
using StationCoords = CaveAiProForWindows.Services.SurveyStationGeometry.StationPlanCoords;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Android section / "extended elevation": horizontal axis = developed distance (tape chainage from graph root),
/// vertical axis = station elevation Z (metres). This is not plan (X,Y).
/// </summary>
public static class ExtendedElevationSceneBuilder
{
    public static PlanScene? TryBuild(CaveProjectDocument p, SurveyVisualizationMode visualization)
    {
        var planCoords = SurveyStationGeometry.CalculatePlanCoordinates(p);
        if (planCoords.Count == 0)
            return null;

        var graph = SurveyTraverseGraph.BuildAdjacency(p.Shots);
        if (graph.Count == 0)
            return null;

        var root = SurveyTraverseGraph.FirstRootStation(graph);
        var chainage = SurveyTraverseGraph.DijkstraChainage(graph, root);

        var elev = new Dictionary<string, StationCoords>(StringComparer.Ordinal);
        foreach (var kv in planCoords)
        {
            if (!chainage.TryGetValue(kv.Key, out var s))
                continue;
            elev[kv.Key] = new StationCoords(kv.Key, s, kv.Value.Z, 0f);
        }

        if (elev.Count == 0)
            return null;

        var traverseLegs = p.Shots.Where(s => s.IsTraverseLeg).ToList();

        var wallPolys = new List<SurveyStationGeometry.PlanVectorPolyline>();
        foreach (var sk in SurveyStationGeometry.ParseSectionSketchesForSectionView(p))
        {
            var t = TransformPolylinePlanToElevationPublic(sk, traverseLegs, planCoords, chainage);
            if (t != null)
                wallPolys.Add(t);
        }

        var vectorPolys = new List<SurveyStationGeometry.PlanVectorPolyline>();
        foreach (var v in SurveyStationGeometry.ParseVectorLinesForViewMode(
                     p.VectorLines,
                     SurveyStationGeometry.AndroidViewModeSection))
        {
            var t = TransformPolylinePlanToElevationPublic(v, traverseLegs, planCoords, chainage);
            if (t != null)
                vectorPolys.Add(t);
        }

        if (!visualization.ShowSplayXRayGeometry())
            wallPolys.AddRange(SurveyLrudWallGeometry.BuildLongProfileLrudRibbonPolylines(p.Shots, elev));

        var splaySegs = new List<(float x1, float y1, float x2, float y2)>();
        if (visualization == SurveyVisualizationMode.Plan2Tone)
        {
            // Filled LRUD profile only — no splay web.
        }
        else if (visualization.ShowSplayXRayGeometry())
        {
            splaySegs.AddRange(SurveyLrudWallGeometry.BuildProfileLrudRadialSplays(p.Shots, elev));
            AppendProfileUdSplays(p.Shots, elev, splaySegs);
        }
        else
        {
            foreach (var seg in SurveySplayGeometry.BuildPlanSplaySegments(p.Shots, planCoords))
            {
                if (TryProjectPlanXYToChainageZ(
                        seg.x1,
                        seg.y1,
                        traverseLegs,
                        planCoords,
                        chainage,
                        out var s1,
                        out var z1)
                    && TryProjectPlanXYToChainageZ(
                        seg.x2,
                        seg.y2,
                        traverseLegs,
                        planCoords,
                        chainage,
                        out var s2,
                        out var z2))
                    splaySegs.Add((s1, z1, s2, z2));
            }
        }

        var minX = 0f;
        var maxX = 0f;
        var minY = 0f;
        var maxY = 0f;
        var has = false;
        void Consider(float x, float y)
        {
            if (!has)
            {
                minX = maxX = x;
                minY = maxY = y;
                has = true;
            }
            else
            {
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        foreach (var c in elev.Values)
            Consider(c.X, c.Y);
        foreach (var pl in vectorPolys)
        {
            foreach (var pt in pl.Points)
                Consider(pt.x, pt.y);
        }

        foreach (var pl in wallPolys)
        {
            foreach (var pt in pl.Points)
                Consider(pt.x, pt.y);
        }

        foreach (var s in traverseLegs)
        {
            if (elev.TryGetValue(s.FromStation, out var fa))
                Consider(fa.X, fa.Y);
            if (elev.TryGetValue(s.ToStation, out var tb))
                Consider(tb.X, tb.Y);
        }

        foreach (var (x1, y1, x2, y2) in splaySegs)
        {
            Consider(x1, y1);
            Consider(x2, y2);
        }

        var sectionSymbols = ProjectPlanSymbolsToProfile(p, traverseLegs, planCoords, chainage);
        foreach (var sym in sectionSymbols)
            Consider(sym.X, sym.Y);

        if (!has)
            return null;

        var segs = new List<(float, float, float, float)>();
        foreach (var s in traverseLegs)
        {
            if (!elev.TryGetValue(s.FromStation, out var a) || !elev.TryGetValue(s.ToStation, out var b))
                continue;
            segs.Add((a.X, a.Y, b.X, b.Y));
        }

        var stationAttached = StationAttachedImageCollector.Collect(p, SurveyStationGeometry.AndroidViewModeSection)
            .Where(r => elev.ContainsKey(r.StationName))
            .ToList();

        var vectorLinesRootKind = p.VectorLines.HasValue ? p.VectorLines.Value.ValueKind.ToString() : "null";
        var vectorLinesArrayLen = 0;
        if (p.VectorLines is { ValueKind: JsonValueKind.Array } vlArr)
            vectorLinesArrayLen = vlArr.GetArrayLength();

        Debug.WriteLine(
            $"[ExtendedElevation] stations={elev.Count}, legs={segs.Count}, walls={wallPolys.Count}, vectors={vectorPolys.Count}, " +
            $"splays={splaySegs.Count}, VectorLines JSON: {vectorLinesRootKind}, arrayLen≈{vectorLinesArrayLen}");

        return new PlanScene
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            Stations = elev,
            TraverseSegments = segs,
            WallPolylines = wallPolys,
            VectorPolylines = vectorPolys,
            Symbols = sectionSymbols,
            StationAttachedImages = stationAttached,
            SplaySegments = splaySegs,
        };
    }

    /// <summary>
    /// Maps Android plan-frame symbol stamps into extended-elevation (chainage × Z) using the same edge-projection as wall strokes.
    /// </summary>
    private static IReadOnlyList<SurveyStationGeometry.PlanMapSymbol> ProjectPlanSymbolsToProfile(
        CaveProjectDocument p,
        IReadOnlyList<ShotRecord> traverseLegs,
        IReadOnlyDictionary<string, StationCoords> planCoords,
        IReadOnlyDictionary<string, float> chainage)
    {
        var list = new List<SurveyStationGeometry.PlanMapSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void TryAdd(SurveyStationGeometry.PlanMapSymbol sym)
        {
            var key = $"{sym.SymbolId ?? sym.Label ?? sym.IconKey ?? ""}\u001f{sym.X:G9}\u001f{sym.Y:G9}";
            if (!seen.Add(key))
                return;
            if (!TryProjectPlanXYToChainageZ(
                    sym.X,
                    sym.Y,
                    traverseLegs,
                    planCoords,
                    chainage,
                    out var s,
                    out var z))
                return;
            list.Add(sym with { X = s, Y = z });
        }

        foreach (var sym in SurveyStationGeometry.ParsePlanMapSymbols(p))
            TryAdd(sym);
        foreach (var sym in SurveyStationGeometry.ParseSectionMapSymbols(p))
            TryAdd(sym);

        return list;
    }

    /// <summary>Project a plan-metre sketch/vector polyline into (chainage, Z) by snapping each vertex to the nearest traverse leg in plan.</summary>
    public static SurveyStationGeometry.PlanVectorPolyline? TransformPolylinePlanToElevationPublic(
        SurveyStationGeometry.PlanVectorPolyline pl,
        IReadOnlyList<ShotRecord> traverseLegs,
        IReadOnlyDictionary<string, StationCoords> planCoords,
        IReadOnlyDictionary<string, float> chainage)
    {
        var pts = new List<(float x, float y)>();
        foreach (var (x, y) in pl.Points)
        {
            if (TryProjectPlanXYToChainageZ(x, y, traverseLegs, planCoords, chainage, out var s, out var z))
                pts.Add((s, z));
        }

        if (pts.Count < 2)
            return null;
        return new SurveyStationGeometry.PlanVectorPolyline(pl.Type, pts, pl.Closed, pl.PreferSharpPolyline);
    }

    /// <summary>Closest point on traverse legs in plan (X,Y); chainage and Z are linearly interpolated along that leg.</summary>
    public static bool TryProjectPlanXYToChainageZ(
        float px,
        float py,
        IReadOnlyList<ShotRecord> traverseLegs,
        IReadOnlyDictionary<string, StationCoords> planCoords,
        IReadOnlyDictionary<string, float> chainage,
        out float s,
        out float z)
    {
        s = z = 0;
        var best = float.MaxValue;
        var found = false;
        foreach (var shot in traverseLegs)
        {
            if (!planCoords.TryGetValue(shot.FromStation, out var a) || !planCoords.TryGetValue(shot.ToStation, out var b))
                continue;
            if (!chainage.TryGetValue(shot.FromStation, out var sa) || !chainage.TryGetValue(shot.ToStation, out var sb))
                continue;
            var ax = a.X;
            var ay = a.Y;
            var bx = b.X;
            var by = b.Y;
            var vx = bx - ax;
            var vy = by - ay;
            var lenSq = vx * vx + vy * vy;
            float u;
            if (lenSq < 1e-12f)
                u = 0f;
            else
                u = Math.Clamp(((px - ax) * vx + (py - ay) * vy) / lenSq, 0f, 1f);
            var qx = ax + u * vx;
            var qy = ay + u * vy;
            var dx = px - qx;
            var dy = py - qy;
            var dSq = dx * dx + dy * dy;
            if (dSq < best)
            {
                best = dSq;
                s = sa + u * (sb - sa);
                z = a.Z + u * (b.Z - a.Z);
                found = true;
            }
        }

        return found;
    }

    private static void AppendProfileUdSplays(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, StationCoords> elev,
        List<(float x1, float y1, float x2, float y2)> splaySegs)
    {
        const float Eps = 1e-4f;
        foreach (var shot in shots.Where(s => s.IsTraverseLeg))
        {
            var (_, _, u, d) = shot.EffectivePlanLrud();
            if (u < Eps && d < Eps)
                continue;

            void AddUd(string st)
            {
                if (!elev.TryGetValue(st, out var c))
                    return;
                var sc = c.X;
                var zz = c.Y;
                if (u > Eps)
                    splaySegs.Add((sc, zz, sc, zz + u));
                if (d > Eps)
                    splaySegs.Add((sc, zz, sc, zz - d));
            }

            AddUd(shot.FromStation);
            AddUd(shot.ToStation);
        }
    }
}
