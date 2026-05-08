using System.Diagnostics;
using System.Text.Json;
using CaveAiProForWindows.Models;
using StationCoords = CaveAiProForWindows.Services.SurveyStationGeometry.StationPlanCoords;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Builds a <see cref="PlanScene"/> for plan (viewMode 0) or section vectors (viewMode 1) + shared traverse.
/// Section-only entry point: <see cref="SectionSceneBuilder"/>.
/// Advanced projections: <see cref="LongProfileSceneBuilder"/>. Pseudo-3D uses <see cref="CaveAiProForWindows.Views.CaveViewport3DPresenter"/>, not <see cref="PlanScene"/>.
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
            SurveyVisualizationMode.Pseudo3D => null,
            _ => vectorViewMode == SurveyStationGeometry.AndroidViewModeSection
                ? ExtendedElevationSceneBuilder.TryBuild(p, visualization)
                : TryBuildPlan(p, visualization),
        };
    }

    /// <summary>Plan view (X,Y survey metres). Section uses <see cref="ExtendedElevationSceneBuilder"/>.</summary>
    private static PlanScene? TryBuildPlan(CaveProjectDocument p, SurveyVisualizationMode visualization)
    {
        const int vectorViewMode = SurveyStationGeometry.AndroidViewModePlan;
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(p);
        var vectorPolys = SurveyStationGeometry.ParseVectorLinesForViewMode(p.VectorLines, vectorViewMode);
        var sketch = SurveyStationGeometry.ParsePlanSketches(p);
        var secPlan = SurveyStationGeometry.ParsePlanSectionSketchesInPlan(p);
        var wallPolys = sketch.Concat(secPlan).ToList();

        // LRUD passage: one closed outline per traverse component = left-wall chain + right-wall chain (reversed),
        // not per-shot boxes — omitted in X-ray (radials only, no passage hull).
        if (!visualization.ShowSplayXRayGeometry())
        {
            foreach (var ribbon in SurveyLrudWallGeometry.BuildPlanLrudRibbonPolylines(p.Shots, coords))
                wallPolys = new[] { ribbon }.Concat(wallPolys).ToList();
        }

        var symbols = SurveyStationGeometry.ParsePlanMapSymbols(p);

        var splaySegs = new List<(float x1, float y1, float x2, float y2)>();
        if (visualization == SurveyVisualizationMode.Plan2Tone)
        {
            // Filled passage only — no internal splay web.
        }
        else if (visualization.ShowSplayXRayGeometry())
        {
            splaySegs.AddRange(SurveyLrudWallGeometry.BuildPlanLrudRadialSplays(p.Shots, coords));
            AppendTraverseLrudUdSplaySegments(p.Shots, coords, splaySegs);
        }
        else
        {
            splaySegs.AddRange(SurveySplayGeometry.BuildPlanSplaySegments(p.Shots, coords));
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
            $"[PlanScene] scene OK bounds x=[{minX:0.##},{maxX:0.##}] y=[{minY:0.##},{maxY:0.##}] walls={wallPolys.Count} vectors={vectorPolys.Count} symbols={symbols.Count} splays={splaySegs.Count} stationImages={stationAttached.Count} (LRUD ribbon when not X-ray)");

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
