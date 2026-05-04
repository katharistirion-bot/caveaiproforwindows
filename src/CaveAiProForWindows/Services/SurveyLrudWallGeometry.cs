using System.Linq;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;
/// <summary>
/// Builds passage outline polygons from traverse shot LRUD (Left, Right, Up, Down metres) in data.json,
/// matching the compass/clino plan frame used by <see cref="SurveyStationGeometry.CalculatePlanCoordinates"/>.
/// </summary>
public static class SurveyLrudWallGeometry
{
    private const float Eps = 1e-4f;
    private const float MinHalfWidth = 0.18f;

    /// <summary>Closed quads in plan (x,y survey metres) — one corridor segment per traverse shot.</summary>
    public static IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> BuildPlanLrudCorridorQuads(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        var list = new List<SurveyStationGeometry.PlanVectorPolyline>();
        foreach (var shot in shots.Where(s => s.IsTraverseLeg))
        {
            if (!coords.TryGetValue(shot.FromStation, out var a) || !coords.TryGetValue(shot.ToStation, out var b))
                continue;
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * (double)dx + dy * (double)dy);
            if (len < 1e-4)
                continue;
            var fx = (float)(dx / len);
            var fy = (float)(dy / len);
            var plx = -fy;
            var ply = fx;
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
            var pts = new List<(float x, float y)> { (flx, fly), (tlx, tly), (trx, trY), (frx, fry) };
            list.Add(new SurveyStationGeometry.PlanVectorPolyline("lrudPlan", pts, Closed: true));
        }

        return list;
    }

    /// <summary>
    /// Plan X-ray / section X-ray: short segments from each traverse leg endpoint to the LRUD left/right wall points
    /// (same frame as <see cref="BuildPlanLrudCorridorQuads"/>).
    /// </summary>
    public static IReadOnlyList<(float x1, float y1, float x2, float y2)> BuildPlanLrudRadialSplays(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        var list = new List<(float, float, float, float)>();
        foreach (var shot in shots.Where(s => s.IsTraverseLeg))
        {
            if (!coords.TryGetValue(shot.FromStation, out var a) || !coords.TryGetValue(shot.ToStation, out var b))
                continue;
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * (double)dx + dy * (double)dy);
            if (len < 1e-4)
                continue;
            var fx = (float)(dx / len);
            var fy = (float)(dy / len);
            var plx = -fy;
            var ply = fx;
            var (lrL, lrR, _, _) = shot.EffectivePlanLrud();
            var L = lrL > Eps ? lrL : MinHalfWidth;
            var R = lrR > Eps ? lrR : MinHalfWidth;

            void Rad(string station)
            {
                if (!coords.TryGetValue(station, out var c))
                    return;
                list.Add((c.X, c.Y, c.X + plx * L, c.Y + ply * L));
                list.Add((c.X, c.Y, c.X - plx * R, c.Y - ply * R));
            }

            Rad(shot.FromStation);
            Rad(shot.ToStation);
        }

        return list;
    }

    /// <summary>Closed quads in long-profile plane (x = chainage m, y = Z m).</summary>
    public static IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> BuildLongProfileLrudQuads(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> projectedChainageZ)
    {
        var list = new List<SurveyStationGeometry.PlanVectorPolyline>();
        foreach (var shot in shots.Where(s => s.IsTraverseLeg))
        {
            if (!projectedChainageZ.TryGetValue(shot.FromStation, out var a) ||
                !projectedChainageZ.TryGetValue(shot.ToStation, out var b))
                continue;
            var s0 = a.X;
            var s1 = b.X;
            var z0 = a.Y;
            var z1 = b.Y;
            var (_, _, lrU, lrD) = shot.EffectivePlanLrud();
            var d = lrD > Eps ? lrD : MinHalfWidth;
            var u = lrU > Eps ? lrU : MinHalfWidth;
            var pts = new List<(float x, float y)>
            {
                (s0, z0 - d),
                (s1, z1 - d),
                (s1, z1 + u),
                (s0, z0 + u),
            };
            list.Add(new SurveyStationGeometry.PlanVectorPolyline("lrudProfile", pts, Closed: true));
        }

        return list;
    }

    /// <summary>Projected wireframe faces/edges for pseudo-3D (dimetric) view.</summary>
    public static IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> BuildPseudo3dLrudWireframe(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        Func<float, float, float, (float xp, float yp)> project)
    {
        var list = new List<SurveyStationGeometry.PlanVectorPolyline>();
        foreach (var shot in shots.Where(s => s.IsTraverseLeg))
        {
            if (!coords3.TryGetValue(shot.FromStation, out var a) || !coords3.TryGetValue(shot.ToStation, out var b))
                continue;
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * (double)dx + dy * (double)dy);
            if (len < 1e-4)
                continue;
            var fx = (float)(dx / len);
            var fy = (float)(dy / len);
            var plx = -fy;
            var ply = fx;
            var (lrL, lrR, lrU, lrD) = shot.EffectivePlanLrud();
            var L = lrL > Eps ? lrL : MinHalfWidth;
            var R = lrR > Eps ? lrR : MinHalfWidth;
            var d = lrD > Eps ? lrD : MinHalfWidth;
            var u = lrU > Eps ? lrU : MinHalfWidth;

            (float xp, float yp) P(float X, float Y, float Z) => project(X, Y, Z);

            var flD = P(a.X + plx * L, a.Y + ply * L, a.Z - d);
            var frD = P(a.X - plx * R, a.Y - ply * R, a.Z - d);
            var frU = P(a.X - plx * R, a.Y - ply * R, a.Z + u);
            var flU = P(a.X + plx * L, a.Y + ply * L, a.Z + u);

            var tlD = P(b.X + plx * L, b.Y + ply * L, b.Z - d);
            var trD = P(b.X - plx * R, b.Y - ply * R, b.Z - d);
            var trU = P(b.X - plx * R, b.Y - ply * R, b.Z + u);
            var tlU = P(b.X + plx * L, b.Y + ply * L, b.Z + u);

            var bottom = new List<(float x, float y)> { flD, frD, trD, tlD };
            list.Add(new SurveyStationGeometry.PlanVectorPolyline("lrud3dFace", bottom, Closed: true));
            var top = new List<(float x, float y)> { flU, frU, trU, tlU };
            list.Add(new SurveyStationGeometry.PlanVectorPolyline("lrud3dFace", top, Closed: true));

            void Edge((float x, float y) p0, (float x, float y) p1) =>
                list.Add(new SurveyStationGeometry.PlanVectorPolyline("lrud3dEdge", new[] { p0, p1 }, Closed: false));

            Edge(flD, flU);
            Edge(frD, frU);
            Edge(tlD, tlU);
            Edge(trD, trU);
            Edge(flD, tlD);
            Edge(frD, trD);
            Edge(flU, tlU);
            Edge(frU, trU);
        }

        return list;
    }
}
