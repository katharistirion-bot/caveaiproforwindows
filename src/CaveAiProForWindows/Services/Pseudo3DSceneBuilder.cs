using System.Diagnostics;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Simple dimetric-style projection: collapse plan (X,Y) with elevation Z into 2D for pseudo-3D traverse display.
/// </summary>
public static class Pseudo3DSceneBuilder
{
    private static (float x, float y) Project(float X, float Y, float Z)
    {
        const float c = 0.70710677f;
        var xp = (X - Y) * c;
        var yp = Z + (X + Y) * 0.5f * c;
        return (xp, yp);
    }

    public static PlanScene? TryBuild(CaveProjectDocument p, int vectorViewMode)
    {
        var coords3 = SurveyStationGeometry.CalculatePlanCoordinates(p.Shots, (float)p.Alt);
        if (coords3.Count == 0)
            return null;

        var projected = new Dictionary<string, SurveyStationGeometry.StationPlanCoords>(StringComparer.Ordinal);
        foreach (var kv in coords3)
        {
            var (xp, yp) = Project(kv.Value.X, kv.Value.Y, kv.Value.Z);
            projected[kv.Key] = new SurveyStationGeometry.StationPlanCoords(kv.Key, xp, yp, kv.Value.Z);
        }

        var segs = new List<(float, float, float, float)>();
        foreach (var shot in p.Shots.Where(s => s.IsTraverseLeg))
        {
            if (!projected.TryGetValue(shot.FromStation, out var a) || !projected.TryGetValue(shot.ToStation, out var b))
                continue;
            segs.Add((a.X, a.Y, b.X, b.Y));
        }

        var z0 = coords3.Values.Average(c => c.Z);
        IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> wallSource;
        if (vectorViewMode == SurveyStationGeometry.AndroidViewModeSection)
            wallSource = SurveyStationGeometry.ParseSectionSketchesForSectionView(p.ExtensionData);
        else
        {
            var sketch = SurveyStationGeometry.ParsePlanSketches(p.ExtensionData);
            var secPlan = SurveyStationGeometry.ParsePlanSectionSketchesInPlan(p.ExtensionData);
            wallSource = sketch.Concat(secPlan).ToList();
        }

        var wallPolys = ProjectPolylines(wallSource, z0);
        // LRUD corners are already projected with per-point Z; do not re-project at z0.
        var lrudWire = SurveyLrudWallGeometry.BuildPseudo3dLrudWireframe(p.Shots, coords3, Project);
        wallPolys = wallPolys.Concat(lrudWire).ToList();

        var vectorPolys = ProjectPolylines(
            SurveyStationGeometry.ParseVectorLinesForViewMode(p.VectorLines, vectorViewMode),
            z0);

        var symbols = vectorViewMode == SurveyStationGeometry.AndroidViewModePlan
            ? SurveyStationGeometry.ParsePlanMapSymbols(p.ExtensionData)
                .Select(s =>
                {
                    var (xp, yp) = Project(s.X, s.Y, z0);
                    return new SurveyStationGeometry.PlanMapSymbol(xp, yp, s.Label);
                })
                .ToList()
            : new List<SurveyStationGeometry.PlanMapSymbol>();

        var minX = projected.Values.Min(c => c.X);
        var maxX = projected.Values.Max(c => c.X);
        var minY = projected.Values.Min(c => c.Y);
        var maxY = projected.Values.Max(c => c.Y);
        ExpandBoundsFromPolys(wallPolys, vectorPolys, ref minX, ref maxX, ref minY, ref maxY);
        foreach (var sym in symbols)
        {
            minX = Math.Min(minX, sym.X);
            maxX = Math.Max(maxX, sym.X);
            minY = Math.Min(minY, sym.Y);
            maxY = Math.Max(maxY, sym.Y);
        }

        Debug.WriteLine($"[Pseudo3D] stations={projected.Count}, legs={segs.Count}, walls={wallPolys.Count}, vectors={vectorPolys.Count}");

        return new PlanScene
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            Stations = projected,
            TraverseSegments = segs,
            WallPolylines = wallPolys,
            VectorPolylines = vectorPolys,
            Symbols = symbols,
            StationAttachedImages = Array.Empty<StationAttachedImageRef>(),
            SplaySegments = Array.Empty<(float, float, float, float)>(),
        };
    }

    private static List<SurveyStationGeometry.PlanVectorPolyline> ProjectPolylines(
        IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> src,
        float z0)
    {
        var list = new List<SurveyStationGeometry.PlanVectorPolyline>();
        foreach (var pl in src)
        {
            var pts = new List<(float x, float y)>();
            foreach (var (x, y) in pl.Points)
            {
                var (xp, yp) = Project(x, y, z0);
                pts.Add((xp, yp));
            }

            if (pts.Count >= 2)
                list.Add(new SurveyStationGeometry.PlanVectorPolyline(pl.Type, pts, pl.Closed));
        }

        return list;
    }

    private static void ExpandBoundsFromPolys(
        IEnumerable<SurveyStationGeometry.PlanVectorPolyline> polys,
        IEnumerable<SurveyStationGeometry.PlanVectorPolyline> polys2,
        ref float minX,
        ref float maxX,
        ref float minY,
        ref float maxY)
    {
        foreach (var pl in polys.Concat(polys2))
        {
            foreach (var (x, y) in pl.Points)
            {
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }
    }
}
