using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Builds a <see cref="PlanScene"/> for plan (viewMode 0) or section vectors (viewMode 1) + shared traverse.</summary>
public static class PlanSceneBuilder
{
    public static PlanScene? TryBuild(CaveProjectDocument p, int vectorViewMode)
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

        var symbols = vectorViewMode == SurveyStationGeometry.AndroidViewModePlan
            ? SurveyStationGeometry.ParsePlanMapSymbols(p.ExtensionData)
            : Array.Empty<SurveyStationGeometry.PlanMapSymbol>();

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

        if (!has)
            return null;

        var segs = new List<(float, float, float, float)>();
        foreach (var s in p.Shots.Where(x => x.IsTraverseLeg))
        {
            if (!coords.TryGetValue(s.FromStation, out var a) || !coords.TryGetValue(s.ToStation, out var b))
                continue;
            segs.Add((a.X, a.Y, b.X, b.Y));
        }

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
        };
    }
}
