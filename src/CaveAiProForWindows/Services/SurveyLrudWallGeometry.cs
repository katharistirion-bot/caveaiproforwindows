using System;
using System.Collections.Generic;
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
    /// toward ±L / ±R (metres along developed distance), matching long-profile (chainage × Z) frame.
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

    /// <summary>
    /// Extended elevation / long profile: one closed passage outline per connected traverse component.
    /// X = developed distance (chainage m), Y = station Z (m). Boundary = ceiling chain (Z + Up) forward,
    /// then floor chain (Z − Down) reversed — not one quad per shot.
    /// </summary>
    public static IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> BuildLongProfileLrudRibbonPolylines(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> projectedChainageZ)
    {
        var ribbons = new List<SurveyStationGeometry.PlanVectorPolyline>();
        foreach (var walk in EnumerateOrderedTraverseWalks(shots))
        {
            var ribbon = RibbonProfileFromOrderedWalk(walk, projectedChainageZ);
            if (ribbon != null)
                ribbons.Add(ribbon);
        }

        return ribbons;
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
        var ribbons = new List<SurveyStationGeometry.PlanVectorPolyline>();
        foreach (var walk in EnumerateOrderedTraverseWalks(shots))
        {
            var ribbon = RibbonFromOrderedWalk(walk, coords);
            if (ribbon != null)
                ribbons.Add(ribbon);
        }

        return ribbons;
    }

    /// <summary>Each inner list is an ordered DFS walk of one traverse connected component (same as plan ribbon).</summary>
    private static List<List<(string wf, string wt, ShotRecord sh)>> EnumerateOrderedTraverseWalks(
        IReadOnlyList<ShotRecord> shots)
    {
        var walks = new List<List<(string wf, string wt, ShotRecord sh)>>();
        var legs = shots.Where(s => s.IsTraverseLeg).ToList();
        if (legs.Count == 0)
            return walks;

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
            if (walk.Count > 0)
                walks.Add(walk);
        }

        return walks;
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

    /// <summary>
    /// Long-profile / section: ceiling (chainage, Z+Up) and floor (chainage, Z−Down) at the ends of one walk step.
    /// U/D are gravity-relative; they are not swapped when walking opposite the shot’s From→To.
    /// </summary>
    public static bool TryProfileCeilingFloorForWalk(
        string walkFrom,
        string walkTo,
        ShotRecord shot,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> elev,
        out (float x, float y) ceilingFrom,
        out (float x, float y) ceilingTo,
        out (float x, float y) floorFrom,
        out (float x, float y) floorTo)
    {
        ceilingFrom = ceilingTo = floorFrom = floorTo = default;
        if (!elev.TryGetValue(walkFrom, out var ca) || !elev.TryGetValue(walkTo, out var cb))
            return false;
        var s0 = ca.X;
        var s1 = cb.X;
        var z0 = ca.Y;
        var z1 = cb.Y;
        var ds = (double)(s1 - s0);
        var dz = (double)(z1 - z0);
        if (ds * ds + dz * dz < 1e-12)
            return false;
        var (_, _, lrU, lrD) = shot.EffectivePlanLrud();
        var u = lrU > Eps ? lrU : MinHalfWidth;
        var d = lrD > Eps ? lrD : MinHalfWidth;
        ceilingFrom = (s0, z0 + u);
        ceilingTo = (s1, z1 + u);
        floorFrom = (s0, z0 - d);
        floorTo = (s1, z1 - d);
        return true;
    }

    private static SurveyStationGeometry.PlanVectorPolyline? RibbonProfileFromOrderedWalk(
        IReadOnlyList<(string wf, string wt, ShotRecord sh)> walk,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> elev)
    {
        if (walk.Count == 0)
            return null;
        const float eps = 0.01f;
        const float densifyStepM = 0.22f;

        static bool Near((float x, float y) p, (float x, float y) q) =>
            Math.Abs(p.x - q.x) <= eps && Math.Abs(p.y - q.y) <= eps;

        void AppendPt(List<(float x, float y)> chain, (float x, float y) p)
        {
            if (chain.Count > 0 && Near(chain[^1], p))
                return;
            chain.Add(p);
        }

        var ceiling = new List<(float x, float y)>();
        var floor = new List<(float x, float y)>();
        foreach (var (wf, wt, sh) in walk)
        {
            if (!TryProfileCeilingFloorForWalk(wf, wt, sh, elev, out var c0, out var c1, out var f0, out var f1))
                continue;
            AppendPt(ceiling, c0);
            AppendPt(ceiling, c1);
            AppendPt(floor, f0);
            AppendPt(floor, f1);
        }

        if (ceiling.Count < 2 || floor.Count < 2)
            return null;

        var ceilingD = DensifyPlanChain(ceiling, densifyStepM);
        var floorD = DensifyPlanChain(floor, densifyStepM);
        var ring = new List<(float x, float y)>(ceilingD.Count + floorD.Count);
        ring.AddRange(ceilingD);
        for (var i = floorD.Count - 1; i >= 0; i--)
            ring.Add(floorD[i]);
        return ring.Count < 3
            ? null
            : new SurveyStationGeometry.PlanVectorPolyline("lrudProfile", ring, Closed: true);
    }

    private static SurveyStationGeometry.PlanVectorPolyline? RibbonFromOrderedWalk(
        IReadOnlyList<(string wf, string wt, ShotRecord sh)> walk,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        if (walk.Count == 0)
            return null;
        const float eps = 0.01f;
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

        ApplyPlanRibbonInnerMiters(left, right, walk, coords);
        if (left.Count < 2 || right.Count < 2)
            return null;

        var leftD = DensifyPlanChain(left, densifyStepM);
        var rightD = DensifyPlanChain(right, densifyStepM);
        var ring = new List<(float x, float y)>(leftD.Count + rightD.Count + 32);
        ring.AddRange(leftD);
        for (var i = rightD.Count - 1; i >= 0; i--)
            ring.Add(rightD[i]);

        InsertPlanRibbonSemicircleCaps(ring, leftD.Count, walk, coords);
        return ring.Count < 3
            ? null
            : new SurveyStationGeometry.PlanVectorPolyline("lrudPlanRibbon", ring, Closed: true);
    }

    /// <summary>
    /// At sharp bends, replace the two inner-wall vertices at each junction with the infinite-line intersection
    /// of the incoming and outgoing wall rails (miter), removing the self-crossing loop.
    /// </summary>
    private static void ApplyPlanRibbonInnerMiters(
        List<(float x, float y)> left,
        List<(float x, float y)> right,
        IReadOnlyList<(string wf, string wt, ShotRecord sh)> walk,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        if (walk.Count < 2 || left.Count < 4 || right.Count < 4)
            return;

        const float turnEps = 1e-5f;
        const float maxMiterM = 24f;

        for (var i = walk.Count - 1; i >= 1; i--)
        {
            var w0 = walk[i - 1];
            var w1 = walk[i];
            if (!coords.TryGetValue(w0.wf, out var c0a) || !coords.TryGetValue(w0.wt, out var c0b))
                continue;
            if (!coords.TryGetValue(w1.wf, out var c1a) || !coords.TryGetValue(w1.wt, out var c1b))
                continue;

            var d0x = c0b.X - c0a.X;
            var d0y = c0b.Y - c0a.Y;
            var len0 = Math.Sqrt(d0x * (double)d0x + d0y * (double)d0y);
            if (len0 < 1e-5)
                continue;
            var u0x = (float)(d0x / len0);
            var u0y = (float)(d0y / len0);

            var d1x = c1b.X - c1a.X;
            var d1y = c1b.Y - c1a.Y;
            var len1 = Math.Sqrt(d1x * (double)d1x + d1y * (double)d1y);
            if (len1 < 1e-5)
                continue;
            var u1x = (float)(d1x / len1);
            var u1y = (float)(d1y / len1);

            var cross = (double)u0x * u1y - (double)u0y * u1x;
            if (Math.Abs(cross) < turnEps)
                continue;

            var li = 2 * i - 1;
            if (li < 0 || li + 1 >= left.Count)
                continue;
            var ri = 2 * i - 1;
            if (ri < 0 || ri + 1 >= right.Count)
                continue;

            var pIn = left[li];
            var pOut = left[li + 1];
            var qIn = right[ri];
            var qOut = right[ri + 1];

            if (cross > turnEps)
            {
                // Left turn (CCW): inner wall is left — miter left chain.
                if (!TryMiterIntersect(pIn, u0x, u0y, pOut, u1x, u1y, maxMiterM, out var mx, out var my))
                    continue;
                left[li] = (mx, my);
                left.RemoveAt(li + 1);
            }
            else
            {
                // Right turn (CW): inner wall is right.
                if (!TryMiterIntersect(qIn, u0x, u0y, qOut, u1x, u1y, maxMiterM, out var mx, out var my))
                    continue;
                right[ri] = (mx, my);
                right.RemoveAt(ri + 1);
            }
        }
    }

    /// <summary>Infinite-line intersection of P + t*u and Q + s*v; clamps if intersection is too far from P–Q segment.</summary>
    private static bool TryMiterIntersect(
        (float x, float y) p,
        float ux,
        float uy,
        (float x, float y) q,
        float vx,
        float vy,
        float maxMiterM,
        out float mx,
        out float my)
    {
        mx = my = 0;
        var det = (double)ux * vy - (double)uy * vx;
        if (Math.Abs(det) < 1e-10)
            return false;
        var rx = (double)q.x - p.x;
        var ry = (double)q.y - p.y;
        var t = (rx * vy - ry * vx) / det;
        var ix = p.x + (float)(t * ux);
        var iy = p.y + (float)(t * uy);
        var midx = (p.x + q.x) * 0.5f;
        var midy = (p.y + q.y) * 0.5f;
        var dx = ix - midx;
        var dy = iy - midy;
        if (dx * dx + dy * dy > maxMiterM * maxMiterM)
        {
            mx = midx;
            my = midy;
        }
        else
        {
            mx = ix;
            my = iy;
        }

        return true;
    }

    /// <summary>
    /// Replace flat mouth chords at the first and last stations with sampled semicircle arcs (Bezier-friendly dense points).
    /// </summary>
    private static void InsertPlanRibbonSemicircleCaps(
        List<(float x, float y)> ring,
        int leftCount,
        IReadOnlyList<(string wf, string wt, ShotRecord sh)> walk,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        int arcSegments = 14)
    {
        if (ring.Count < 4 || leftCount < 2 || walk.Count == 0)
            return;

        var lnIdx = leftCount - 1;
        var rnIdx = leftCount;
        if (lnIdx < 0 || rnIdx >= ring.Count)
            return;

        var l0 = ring[0];
        var r0 = ring[^1];
        var ln = ring[lnIdx];
        var rn = ring[rnIdx];

        // End cap: bulge outward from passage at terminal station.
        if (coords.TryGetValue(walk[^1].wf, out var cEwF) && coords.TryGetValue(walk[^1].wt, out var cEwT))
        {
            var edx = cEwF.X - cEwT.X;
            var edy = cEwF.Y - cEwT.Y;
            var el = Math.Sqrt(edx * (double)edx + edy * (double)edy);
            if (el > 1e-5)
            {
                var eux = (float)(edx / el);
                var euy = (float)(edy / el);
                var endArc = SampleSemicircleArcInterior(ln, rn, eux, euy, arcSegments);
                if (endArc.Count > 0)
                    ring.InsertRange(rnIdx, endArc);
            }
        }

        // Start cap: bulge outward from passage at entry station (after end insert, indices shift).
        if (coords.TryGetValue(walk[0].wf, out var cSwF) && coords.TryGetValue(walk[0].wt, out var cSwT))
        {
            var sdx = cSwT.X - cSwF.X;
            var sdy = cSwT.Y - cSwF.Y;
            var sl = Math.Sqrt(sdx * (double)sdx + sdy * (double)sdy);
            if (sl > 1e-5)
            {
                var sux = (float)(sdx / sl);
                var suy = (float)(sdy / sl);
                // ring[^1] is still R0 after end insert? End insert was before old Rn — ring[last] may have changed.
                var rEnd = ring[^1];
                var startArc = SampleSemicircleArcInterior(rEnd, l0, sux, suy, arcSegments);
                if (startArc.Count > 0)
                    ring.AddRange(startArc);
            }
        }
    }

    /// <summary>
    /// Semicircle on diameter A–B in (x,y), sampled interior points (exclusive of A and B). <paramref name="intoCaveUx,Vy"/>
    /// points from the mouth chord midpoint toward the interior so the arc bulges outward.
    /// </summary>
    private static List<(float x, float y)> SampleSemicircleArcInterior(
        (float x, float y) a,
        (float x, float y) b,
        float intoCaveUx,
        float intoCaveUy,
        int segments)
    {
        var arc = new List<(float x, float y)>();
        if (segments < 3)
            return arc;
        var ax = a.x;
        var ay = a.y;
        var bx = b.x;
        var by = b.y;
        var dvx = bx - ax;
        var dvy = by - ay;
        var chord = Math.Sqrt(dvx * (double)dvx + dvy * (double)dvy);
        if (chord < 1e-5)
            return arc;
        var r = (float)(chord * 0.5);
        var ox = (ax + bx) * 0.5f;
        var oy = (ay + by) * 0.5f;
        var e1x = (float)(dvx / chord);
        var e1y = (float)(dvy / chord);
        var e2x = -e1y;
        var e2y = e1x;
        if (e2x * intoCaveUx + e2y * intoCaveUy > 0)
        {
            e2x = -e2x;
            e2y = -e2y;
        }

        for (var k = 1; k < segments; k++)
        {
            var theta = Math.PI * (1.0 - (double)k / segments);
            var ct = Math.Cos(theta);
            var st = Math.Sin(theta);
            var px = (float)(ox + r * (ct * e1x + st * e2x));
            var py = (float)(oy + r * (ct * e1y + st * e2y));
            arc.Add((px, py));
        }

        return arc;
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
