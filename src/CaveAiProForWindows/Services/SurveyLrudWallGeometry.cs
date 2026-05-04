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

    /// <summary>
    /// Extended elevation / long-profile X-ray: horizontal segments at station elevation from chainage
    /// toward ±L / ±R (metres along developed distance), matching <see cref="BuildLongProfileLrudQuads"/> frame.
    /// </summary>
    public static IReadOnlyList<(float x1, float y1, float x2, float y2)> BuildProfileLrudRadialSplays(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> profileChainageZ)
    {
        var list = new List<(float, float, float, float)>();
        foreach (var shot in shots.Where(s => s.IsTraverseLeg))
        {
            if (!profileChainageZ.TryGetValue(shot.FromStation, out _) ||
                !profileChainageZ.TryGetValue(shot.ToStation, out _))
                continue;
            var (lrL, lrR, _, _) = shot.EffectivePlanLrud();
            var L = lrL > Eps ? lrL : MinHalfWidth;
            var R = lrR > Eps ? lrR : MinHalfWidth;

            void Rad(string station)
            {
                if (!profileChainageZ.TryGetValue(station, out var c))
                    return;
                var s = c.X;
                var z = c.Y;
                list.Add((s, z, s + L, z));
                list.Add((s, z, s - R, z));
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

    /// <summary>
    /// Closed plan passage hull(s): each connected traverse component becomes one polygon whose boundary follows
    /// left/right LRUD offsets along a graph walk (not raw JSON shot order), with densified vertices for smooth spline fit.
    /// </summary>
    public static IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> BuildPlanLrudRibbonPolylines(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        var legs = shots.Where(s => s.IsTraverseLeg).ToList();
        if (legs.Count == 0)
            return Array.Empty<SurveyStationGeometry.PlanVectorPolyline>();

        var legCount = legs.Count;
        var adj = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        void AddAdj(string? node, int idx)
        {
            if (string.IsNullOrEmpty(node))
                return;
            if (!adj.TryGetValue(node, out var list))
            {
                list = new List<int>();
                adj[node] = list;
            }

            list.Add(idx);
        }

        for (var i = 0; i < legCount; i++)
        {
            var s = legs[i];
            AddAdj(s.FromStation, i);
            AddAdj(s.ToStation, i);
        }

        var used = new HashSet<int>();
        var ribbons = new List<SurveyStationGeometry.PlanVectorPolyline>();

        for (var seed = 0; seed < legCount; seed++)
        {
            if (used.Contains(seed))
                continue;

            var walk = new List<(string wf, string wt, ShotRecord sh)>();
            void Dfs(string u)
            {
                if (!adj.TryGetValue(u, out var incident))
                    return;
                foreach (var li in incident)
                {
                    if (!used.Add(li))
                        continue;
                    var sh = legs[li];
                    var v = string.Equals(sh.FromStation, u, StringComparison.Ordinal)
                        ? sh.ToStation!
                        : sh.FromStation!;
                    walk.Add((u, v, sh));
                    Dfs(v);
                }
            }

            Dfs(legs[seed].FromStation);
            var ribbon = RibbonFromOrderedWalk(walk, coords);
            if (ribbon != null)
                ribbons.Add(ribbon);
        }

        return ribbons;
    }

    /// <summary>
    /// LRUD corners for a single traverse step from <paramref name="walkFrom"/> to <paramref name="walkTo"/>
    /// (swap L/R when walking opposite the shot's From→To direction).
    /// </summary>
    public static bool TryQuadCornersForWalk(
        string walkFrom,
        string walkTo,
        ShotRecord shot,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        out (float x, float y) leftAtFrom,
        out (float x, float y) leftAtTo,
        out (float x, float y) rightAtFrom,
        out (float x, float y) rightAtTo)
    {
        leftAtFrom = leftAtTo = rightAtFrom = rightAtTo = default;
        if (!coords.TryGetValue(walkFrom, out var ca) || !coords.TryGetValue(walkTo, out var cb))
            return false;
        var dx = cb.X - ca.X;
        var dy = cb.Y - ca.Y;
        var len = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        if (len < 1e-4)
            return false;
        var fx = (float)(dx / len);
        var fy = (float)(dy / len);
        var plx = -fy;
        var ply = fx;

        var (lrL, lrR, _, _) = shot.EffectivePlanLrud();
        var reverse = string.Equals(walkFrom, shot.ToStation, StringComparison.Ordinal)
                      && string.Equals(walkTo, shot.FromStation, StringComparison.Ordinal);
        if (reverse)
            (lrL, lrR) = (lrR, lrL);

        var L = lrL > Eps ? lrL : MinHalfWidth;
        var R = lrR > Eps ? lrR : MinHalfWidth;
        leftAtFrom = (ca.X + plx * L, ca.Y + ply * L);
        leftAtTo = (cb.X + plx * L, cb.Y + ply * L);
        rightAtFrom = (ca.X - plx * R, ca.Y - ply * R);
        rightAtTo = (cb.X - plx * R, cb.Y - ply * R);
        return true;
    }

    private static SurveyStationGeometry.PlanVectorPolyline? RibbonFromOrderedWalk(
        IReadOnlyList<(string wf, string wt, ShotRecord sh)> walk,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        if (walk.Count == 0)
            return null;
        const float eps = 2.5e-3f;
        const float densifyStepM = 0.22f;

        static bool Near((float x, float y) p, (float x, float y) q) =>
            Math.Abs(p.x - q.x) <= eps && Math.Abs(p.y - q.y) <= eps;

        void AppendPt(List<(float x, float y)> chain, (float x, float y) p)
        {
            if (chain.Count > 0 && Near(chain[^1], p))
                return;
            chain.Add(p);
        }

        var left = new List<(float x, float y)>();
        var right = new List<(float x, float y)>();
        foreach (var (wf, wt, sh) in walk)
        {
            if (!TryQuadCornersForWalk(wf, wt, sh, coords, out var l0, out var l1, out var r0, out var r1))
                continue;
            AppendPt(left, l0);
            AppendPt(left, l1);
            AppendPt(right, r0);
            AppendPt(right, r1);
        }

        if (left.Count < 2 || right.Count < 2)
            return null;

        var leftD = DensifyPlanChain(left, densifyStepM);
        var rightD = DensifyPlanChain(right, densifyStepM);
        var ring = new List<(float x, float y)>(leftD.Count + rightD.Count);
        ring.AddRange(leftD);
        for (var i = rightD.Count - 1; i >= 0; i--)
            ring.Add(rightD[i]);
        return ring.Count < 3
            ? null
            : new SurveyStationGeometry.PlanVectorPolyline("lrudPlanRibbon", ring, Closed: true);
    }

    private static List<(float x, float y)> DensifyPlanChain(IReadOnlyList<(float x, float y)> chain, float stepM)
    {
        if (chain.Count <= 1)
            return new List<(float x, float y)>(chain);
        var res = new List<(float x, float y)>(chain.Count * 3) { chain[0] };
        for (var i = 0; i < chain.Count - 1; i++)
        {
            var p0 = chain[i];
            var p1 = chain[i + 1];
            var dx = p1.x - p0.x;
            var dy = p1.y - p0.y;
            var d = Math.Sqrt(dx * (double)dx + dy * (double)dy);
            if (d < 1e-4)
            {
                res.Add(p1);
                continue;
            }

            var n = Math.Min(64, Math.Max(1, (int)Math.Ceiling(d / stepM)));
            for (var k = 1; k < n; k++)
            {
                var t = k / (float)n;
                res.Add((p0.x + t * dx, p0.y + t * dy));
            }

            res.Add(p1);
        }

        return res;
    }
}
