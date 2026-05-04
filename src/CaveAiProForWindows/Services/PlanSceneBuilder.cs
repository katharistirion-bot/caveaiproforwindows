using System.Diagnostics;
using System.Text.Json;
using CaveAiProForWindows.Models;
using StationCoords = CaveAiProForWindows.Services.SurveyStationGeometry.StationPlanCoords;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Builds a <see cref="PlanScene"/> for plan (viewMode 0) or section vectors (viewMode 1) + shared traverse.
/// Section-only entry point: <see cref="SectionSceneBuilder"/>.
/// Advanced projections: <see cref="LongProfileSceneBuilder"/>, <see cref="Pseudo3DSceneBuilder"/>.
/// Raster basemaps are not part of the scene graph; they are loaded separately by <see cref="PlanMapUnderlayLoader"/>.
/// </summary>
public static class PlanSceneBuilder
{
    public static PlanScene? TryBuild(
        CaveProjectDocument p,
        int vectorViewMode,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard)
    {
        Debug.WriteLine(
            $"[PlanScene] TryBuild name={p.Name}, viewMode={vectorViewMode}, viz={visualization}, shots={p.Shots.Count}, traverseLegs={p.Shots.Count(s => s.IsTraverseLeg)}");

        return visualization switch
        {
            SurveyVisualizationMode.LongProfile => LongProfileSceneBuilder.TryBuild(p),
            SurveyVisualizationMode.Pseudo3D => Pseudo3DSceneBuilder.TryBuild(p, vectorViewMode),
            _ => TryBuildPlanOrSection(p, vectorViewMode, visualization),
        };
    }

    private static PlanScene? TryBuildPlanOrSection(
        CaveProjectDocument p,
        int vectorViewMode,
        SurveyVisualizationMode visualization)
    {
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(p.Shots, (float)p.Alt);
        var vectorPolys = SurveyStationGeometry.ParseVectorLinesForViewMode(p.VectorLines, vectorViewMode);
        IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> wallPolys;
        if (vectorViewMode == SurveyStationGeometry.AndroidViewModePlan)
        {
            var sketch = SurveyStationGeometry.ParsePlanSketches(p.ExtensionData);
            var secPlan = SurveyStationGeometry.ParsePlanSectionSketchesInPlan(p.ExtensionData);
            wallPolys = sketch.Concat(secPlan).ToList();
        }
        else
        {
            wallPolys = SurveyStationGeometry.ParseSectionSketchesForSectionView(p.ExtensionData);
        }

        // LRUD passage: one continuous ribbon polygon (plan frame) + X-ray radial splays — not centerline-only.
        if (vectorViewMode == SurveyStationGeometry.AndroidViewModePlan ||
            vectorViewMode == SurveyStationGeometry.AndroidViewModeSection)
        {
            var ribbon = BuildLrudPassageRibbonPolyline(p.Shots, coords);
            if (ribbon != null)
                wallPolys = new[] { ribbon }.Concat(wallPolys).ToList();
        }

        var symbols = vectorViewMode == SurveyStationGeometry.AndroidViewModePlan
            ? SurveyStationGeometry.ParsePlanMapSymbols(p.ExtensionData)
            : Array.Empty<SurveyStationGeometry.PlanMapSymbol>();

        var splaySegs = SurveySplayGeometry.BuildPlanSplaySegments(p.Shots, coords).ToList();
        if (visualization.ShowSplayXRayGeometry())
        {
            splaySegs.AddRange(SurveyLrudWallGeometry.BuildPlanLrudRadialSplays(p.Shots, coords));
            AppendTraverseLrudUdSplaySegments(p.Shots, coords, splaySegs);
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

        foreach (var c in coords.Values)
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

        foreach (var sym in symbols)
            Consider(sym.X, sym.Y);

        foreach (var s in p.Shots.Where(x => x.IsTraverseLeg))
        {
            if (coords.TryGetValue(s.FromStation, out var fa))
                Consider(fa.X, fa.Y);
            if (coords.TryGetValue(s.ToStation, out var tb))
                Consider(tb.X, tb.Y);
        }

        foreach (var (x1, y1, x2, y2) in splaySegs)
        {
            Consider(x1, y1);
            Consider(x2, y2);
        }

        var vectorLinesRootKind = p.VectorLines.HasValue ? p.VectorLines.Value.ValueKind.ToString() : "null";
        var vectorLinesArrayLen = 0;
        if (p.VectorLines is { ValueKind: JsonValueKind.Array } vlArr)
            vectorLinesArrayLen = vlArr.GetArrayLength();

        var traverseLegCount = p.Shots.Count(s => s.IsTraverseLeg);
        Debug.WriteLine(
            $"[Vectors] Parsed {vectorPolys.Count} vector polylines (VectorLines JSON: {vectorLinesRootKind}, top-level array length≈{vectorLinesArrayLen}) and {traverseLegCount} traverse legs.");

        if (!has)
        {
            Debug.WriteLine("[PlanScene] no vector geometry (stations/sketch/symbol bounds empty). Raster TIFF underlay is still resolved independently.");
            return null;
        }

        var segs = new List<(float, float, float, float)>();
        foreach (var s in p.Shots.Where(x => x.IsTraverseLeg))
        {
            if (!coords.TryGetValue(s.FromStation, out var a) || !coords.TryGetValue(s.ToStation, out var b))
                continue;
            segs.Add((a.X, a.Y, b.X, b.Y));
        }

        var stationAttached = StationAttachedImageCollector.Collect(p, vectorViewMode)
            .Where(r => coords.ContainsKey(r.StationName))
            .ToList();
        Debug.WriteLine(
            $"[PlanScene] scene OK bounds x=[{minX:0.##},{maxX:0.##}] y=[{minY:0.##},{maxY:0.##}] walls={wallPolys.Count} vectors={vectorPolys.Count} symbols={symbols.Count} splays={splaySegs.Count} stationImages={stationAttached.Count} (LRUD plan tubes prepended when in plan view)");

        return new PlanScene
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            Stations = coords,
            TraverseSegments = segs,
            WallPolylines = wallPolys,
            VectorPolylines = vectorPolys,
            Symbols = symbols,
            StationAttachedImages = stationAttached,
            SplaySegments = splaySegs,
        };
    }

    /// <summary>
    /// Builds one closed polygon in plan (x,y) metres: left wall chain along traverse order, then right wall chain reversed,
    /// using each traverse shot's <see cref="ShotRecord.EffectivePlanLrud"/> and perpendicular offsets (same math as legacy per-leg quads).
    /// </summary>
    private static SurveyStationGeometry.PlanVectorPolyline? BuildLrudPassageRibbonPolyline(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, StationCoords> coords)
    {
        var legs = shots.Where(s => s.IsTraverseLeg).ToList();
        if (legs.Count == 0)
            return null;

        var left = new List<(float x, float y)>();
        var right = new List<(float x, float y)>();
        const float eps = 2e-3f;
        static bool Near((float x, float y) p, (float x, float y) q) =>
            Math.Abs(p.x - q.x) <= eps && Math.Abs(p.y - q.y) <= eps;

        void Append(List<(float x, float y)> chain, (float x, float y) p)
        {
            if (chain.Count > 0 && Near(chain[^1], p))
                return;
            chain.Add(p);
        }

        foreach (var shot in legs)
        {
            if (!coords.TryGetValue(shot.FromStation, out var a) || !coords.TryGetValue(shot.ToStation, out var b))
                continue;
            if (!TryLrudQuadCorners(shot, a, b, out var fl, out var tl, out var tr, out var fr))
                continue;
            Append(left, fl);
            Append(left, tl);
            Append(right, fr);
            Append(right, tr);
        }

        if (left.Count + right.Count < 3)
            return null;

        var ring = new List<(float x, float y)>(left.Count + right.Count);
        ring.AddRange(left);
        for (var i = right.Count - 1; i >= 0; i--)
            ring.Add(right[i]);
        if (ring.Count < 3)
            return null;

        return new SurveyStationGeometry.PlanVectorPolyline("lrudPlanRibbon", ring, Closed: true);
    }

    /// <summary>Same perpendicular corridor corners as <see cref="SurveyLrudWallGeometry.BuildPlanLrudCorridorQuads"/> (single leg).</summary>
    private static bool TryLrudQuadCorners(
        ShotRecord shot,
        StationCoords a,
        StationCoords b,
        out (float x, float y) fl,
        out (float x, float y) tl,
        out (float x, float y) tr,
        out (float x, float y) fr)
    {
        fl = tl = tr = fr = default;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        if (len < 1e-4)
            return false;
        var fx = (float)(dx / len);
        var fy = (float)(dy / len);
        var plx = -fy;
        var ply = fx;
        const float Eps = 1e-4f;
        const float MinHalfWidth = 0.18f;
        var (lrL, lrR, _, _) = shot.EffectivePlanLrud();
        var L = lrL > Eps ? lrL : MinHalfWidth;
        var R = lrR > Eps ? lrR : MinHalfWidth;
        var flx = a.X + plx * L;
        var fly = a.Y + ply * L;
        var frx = a.X - plx * R;
        var fry = a.Y - ply * R;
        var tlx = b.X + plx * L;
        var tly = b.Y + ply * L;
        var trx = b.X - plx * R;
        var trY = b.Y - ply * R;
        fl = (flx, fly);
        tl = (tlx, tly);
        tr = (trx, trY);
        fr = (frx, fry);
        return true;
    }

    /// <summary>X-ray: from each traverse station, draw plan segments for Up/Down along horizontal survey direction (tape × cos(clino)).</summary>
    private static void AppendTraverseLrudUdSplaySegments(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, StationCoords> coords,
        List<(float x1, float y1, float x2, float y2)> splaySegs)
    {
        const float Eps = 1e-4f;
        foreach (var shot in shots.Where(s => s.IsTraverseLeg))
        {
            if (!coords.TryGetValue(shot.FromStation, out var a) || !coords.TryGetValue(shot.ToStation, out var b))
                continue;
            var (_, _, u, d) = shot.EffectivePlanLrud();
            if (u < Eps && d < Eps)
                continue;
            var azRad = shot.Azimuth * (Math.PI / 180.0);
            var clRad = shot.Clino * (Math.PI / 180.0);
            var sinA = (float)Math.Sin(azRad);
            var cosA = (float)Math.Cos(azRad);
            var cosCl = (float)Math.Cos(clRad);
            var fhx = sinA * cosCl;
            var fhy = cosA * cosCl;

            void AddUd(StationCoords c)
            {
                if (u > Eps)
                    splaySegs.Add((c.X, c.Y, c.X + fhx * u, c.Y + fhy * u));
                if (d > Eps)
                    splaySegs.Add((c.X, c.Y, c.X - fhx * d, c.Y - fhy * d));
            }

            AddUd(a);
            AddUd(b);
        }
    }
}
