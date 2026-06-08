using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Builds a smooth tubular <see cref="MeshGeometry3D"/> along traverse legs from LRUD dimensions:
/// elliptical cross-sections (polygonal) in planes perpendicular to each leg, stitched with triangle strips.
/// </summary>
public static class CaveSurveyTubeMeshBuilder
{
    private const float Eps = 1e-4f;
    private const float MinHalf = 0.18f;

    /// <summary>Minimum 3; 16–24 recommended for smooth tubes.</summary>
    public const int DefaultEllipseSegments = TubeMeshQualityResolver.StandardEllipseSegments;

    /// <summary>Returns null if there is no traversable geometry.</summary>
    public static MeshGeometry3D? BuildTubeMesh(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        int ellipseSegments = DefaultEllipseSegments,
        float centerlineSampleM = TubeMeshQualityResolver.StandardCenterlineSampleM) =>
        BuildContinuousLrudTubeMesh(shots, coords3, ellipseSegments, centerlineSampleM)
        ?? BuildPerLegLrudTubeMesh(shots, coords3, ellipseSegments);

    /// <summary>Returns null if there is no traversable geometry.</summary>
    public static MeshGeometry3D? BuildTubeMesh(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        TubeMeshQuality quality) =>
        BuildTubeMesh(
            shots,
            coords3,
            TubeMeshQualityResolver.EllipseSegments(quality),
            TubeMeshQualityResolver.CenterlineSampleM(quality));

    /// <summary>Ordered centerline samples with tangents for fly-through and tests.</summary>
    public static IReadOnlyList<(Point3D Position, Vector3D Tangent)> BuildTraverseCenterlinePath(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        float centerlineSampleM = 0.5f)
    {
        var path = new List<(Point3D, Vector3D)>();
        var walks = SurveyLrudWallGeometry.GetOrderedTraverseWalks(shots);
        foreach (var walk in walks)
        {
            if (!TryBuildWalkCenterlineSamples(walk, coords3, centerlineSampleM, out var samples))
                continue;
            foreach (var (center, _, tangent) in samples)
            {
                if (tangent.LengthSquared < 1e-12)
                    continue;
                var t = tangent;
                t.Normalize();
                path.Add((new Point3D(center.X, center.Y, center.Z), t));
            }
        }

        return path;
    }

    /// <summary>
    /// Continuous LRUD tube along traverse walks: Catmull-Rom centerline with elliptical cross-sections
    /// interpolated from station LRUD dimensions.
    /// </summary>
    public static MeshGeometry3D? BuildContinuousLrudTubeMesh(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        int ellipseSegments = DefaultEllipseSegments,
        float centerlineSampleM = 0.35f)
    {
        if (ellipseSegments < 3)
            return null;

        var walks = SurveyLrudWallGeometry.GetOrderedTraverseWalks(shots);
        if (walks.Count == 0)
            return null;

        var positions = new List<Point3D>();
        var indices = new List<int>();
        var ringStarts = new List<int>();

        foreach (var walk in walks)
        {
            if (!TryBuildWalkCenterlineSamples(walk, coords3, centerlineSampleM, out var samples))
                continue;

            Vector3D? prevTangent = null;
            Vector3D? prevRAxis = null;
            for (var si = 0; si < samples.Count; si++)
            {
                var (center, lrud, tangent) = samples[si];
                if (tangent.LengthSquared < 1e-12)
                    continue;
                tangent.Normalize();
                Vector3D rAxis;
                Vector3D uAxis;
                if (prevRAxis == null || prevTangent == null)
                {
                    if (!TrySeedBasisFromWalk(walk, coords3, tangent, out rAxis, out uAxis))
                        continue;
                }
                else if (!PropagateEllipseBasis(prevTangent.Value, prevRAxis.Value, tangent, out rAxis, out uAxis))
                {
                    continue;
                }

                prevRAxis = rAxis;
                prevTangent = tangent;

                var L = lrud.L > Eps ? lrud.L : MinHalf;
                var R = lrud.R > Eps ? lrud.R : MinHalf;
                var U = lrud.U > Eps ? lrud.U : MinHalf;
                var D = lrud.D > Eps ? lrud.D : MinHalf;
                var aSemi = 0.5 * (L + R);
                var bSemi = 0.5 * (U + D);
                var offset = rAxis * (0.5 * (R - L)) + uAxis * (0.5 * (U - D));
                ringStarts.Add(positions.Count);
                AppendEllipseRing(positions, center, tangent, rAxis, uAxis, offset, aSemi, bSemi, ellipseSegments);
            }

            for (var ri = 0; ri + 1 < ringStarts.Count; ri++)
            {
                var baseA = ringStarts[ri];
                var baseB = ringStarts[ri + 1];
                if (baseB - baseA == ellipseSegments)
                    StitchRings(indices, baseA, baseB, ellipseSegments);
            }

            ringStarts.Clear();
        }

        if (positions.Count < 3 || indices.Count < 3)
            return null;

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TriangleIndices = new Int32Collection(indices),
        };
        mesh.Normals = ComputeVertexNormals(mesh.Positions, mesh.TriangleIndices);
        mesh.Freeze();
        return mesh;
    }

    private readonly record struct LrudSlice(float L, float R, float U, float D);

    private static bool TryBuildWalkCenterlineSamples(
        IReadOnlyList<(string wf, string wt, ShotRecord sh)> walk,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        float sampleM,
        out List<(Vector3D center, LrudSlice lrud, Vector3D tangent)> samples)
    {
        samples = new List<(Vector3D, LrudSlice, Vector3D)>();
        if (walk.Count == 0)
            return false;

        var stations = new List<string> { walk[0].wf };
        foreach (var step in walk)
            stations.Add(step.wt);

        var anchors = new List<(Vector3D p, LrudSlice lrud)>();
        for (var i = 0; i < stations.Count; i++)
        {
            var st = stations[i];
            if (!coords3.TryGetValue(st, out var c))
                continue;
            if (!TryResolveStationLrud(st, i, walk, out var lrud))
                continue;
            anchors.Add((new Vector3D(c.X, c.Y, c.Z), lrud));
        }

        if (anchors.Count < 2)
            return false;

        var dense = SampleOpenCenterline3D(anchors, sampleM);
        if (dense.Count < 2)
            return false;

        for (var i = 0; i < dense.Count; i++)
        {
            var tangent = i == 0
                ? dense[1].p - dense[0].p
                : i == dense.Count - 1
                    ? dense[i].p - dense[i - 1].p
                    : dense[i + 1].p - dense[i - 1].p;
            samples.Add((dense[i].p, dense[i].lrud, tangent));
        }

        return samples.Count >= 2;
    }

    private static bool TryResolveStationLrud(
        string station,
        int index,
        IReadOnlyList<(string wf, string wt, ShotRecord sh)> walk,
        out LrudSlice lrud)
    {
        lrud = default;
        var sumL = 0f;
        var sumR = 0f;
        var sumU = 0f;
        var sumD = 0f;
        var n = 0;

        void Accumulate(ShotRecord sh, string wf, string wt)
        {
            if (!string.Equals(station, wf, StringComparison.Ordinal) &&
                !string.Equals(station, wt, StringComparison.Ordinal))
                return;
            var (l, r, u, d) = sh.EffectivePlanLrud();
            var reverse = string.Equals(wf, sh.ToStation, StringComparison.Ordinal) &&
                          string.Equals(wt, sh.FromStation, StringComparison.Ordinal);
            if (reverse)
                (l, r) = (r, l);
            sumL += l;
            sumR += r;
            sumU += u;
            sumD += d;
            n++;
        }

        if (index > 0)
            Accumulate(walk[index - 1].sh, walk[index - 1].wf, walk[index - 1].wt);
        if (index < walk.Count)
            Accumulate(walk[index].sh, walk[index].wf, walk[index].wt);

        if (n == 0)
            return false;

        lrud = new LrudSlice(sumL / n, sumR / n, sumU / n, sumD / n);
        return true;
    }

    private static List<(Vector3D p, LrudSlice lrud)> SampleOpenCenterline3D(
        IReadOnlyList<(Vector3D p, LrudSlice lrud)> anchors,
        float sampleM)
    {
        if (anchors.Count == 1)
            return new List<(Vector3D, LrudSlice)> { anchors[0] };

        var chain = new List<(float x, float y)>(anchors.Count);
        foreach (var a in anchors)
            chain.Add(((float)a.p.X, (float)a.p.Y));

        var zChain = anchors.Select(a => a.p.Z).ToList();
        var lrudChain = anchors.Select(a => a.lrud).ToList();
        var planSamples = SurveyCatmullRomSampler.SampleOpenPlanChain(chain, sampleM);
        if (planSamples.Count < 2)
            return new List<(Vector3D, LrudSlice)>(anchors);

        var dense = new List<(Vector3D p, LrudSlice lrud)>(planSamples.Count);
        foreach (var (x, y) in planSamples)
        {
            if (!TryInterpolateAlongPolyline(chain, zChain, lrudChain, x, y, out var z, out var lrud))
                continue;
            dense.Add((new Vector3D(x, y, z), lrud));
        }

        return dense.Count >= 2 ? dense : new List<(Vector3D, LrudSlice)>(anchors);
    }

    private static bool TryInterpolateAlongPolyline(
        IReadOnlyList<(float x, float y)> xy,
        IReadOnlyList<double> z,
        IReadOnlyList<LrudSlice> lrud,
        float x,
        float y,
        out double zOut,
        out LrudSlice lrudOut)
    {
        zOut = 0;
        lrudOut = default;
        if (xy.Count < 2 || xy.Count != z.Count || xy.Count != lrud.Count)
            return false;

        var bestSeg = 0;
        var bestT = 0.0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < xy.Count - 1; i++)
        {
            var ax = xy[i].x;
            var ay = xy[i].y;
            var bx = xy[i + 1].x;
            var by = xy[i + 1].y;
            var dx = bx - ax;
            var dy = by - ay;
            var lenSq = dx * dx + dy * dy;
            var t = lenSq < 1e-12 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / lenSq, 0, 1);
            var px = ax + t * dx;
            var py = ay + t * dy;
            var d = (x - px) * (x - px) + (y - py) * (y - py);
            if (d < bestDist)
            {
                bestDist = d;
                bestSeg = i;
                bestT = t;
            }
        }

        zOut = z[bestSeg] + bestT * (z[bestSeg + 1] - z[bestSeg]);
        var a = lrud[bestSeg];
        var b = lrud[bestSeg + 1];
        var tF = (float)bestT;
        lrudOut = new LrudSlice(
            a.L + tF * (b.L - a.L),
            a.R + tF * (b.R - a.R),
            a.U + tF * (b.U - a.U),
            a.D + tF * (b.D - a.D));
        return true;
    }

    /// <summary>Per-leg elliptical tubes (legacy fallback).</summary>
    private static MeshGeometry3D? BuildPerLegLrudTubeMesh(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        int ellipseSegments = DefaultEllipseSegments)
    {
        var legs = shots.Where(s => s.IsTraverseLeg).ToList();
        if (legs.Count == 0 || ellipseSegments < 3)
            return null;

        var positions = new List<Point3D>();
        var indices = new List<int>();

        foreach (var shot in legs)
        {
            if (!coords3.TryGetValue(shot.FromStation, out var ca) || !coords3.TryGetValue(shot.ToStation, out var cb))
                continue;
            var a = new Vector3D(ca.X, ca.Y, ca.Z);
            var b = new Vector3D(cb.X, cb.Y, cb.Z);
            var t = b - a;
            var len = t.Length;
            if (len < 1e-5)
                continue;
            t.Normalize();

            var (lrL, lrR, lrU, lrD) = shot.EffectivePlanLrud();
            var L = lrL > Eps ? lrL : MinHalf;
            var R = lrR > Eps ? lrR : MinHalf;
            var U = lrU > Eps ? lrU : MinHalf;
            var D = lrD > Eps ? lrD : MinHalf;
            var aSemi = 0.5 * (L + R);
            var bSemi = 0.5 * (U + D);

            if (!TryEllipseBasis(t, out var rAxis, out var uAxis))
                continue;

            var offset = rAxis * (0.5 * (R - L)) + uAxis * (0.5 * (U - D));

            var baseA = positions.Count;
            AppendEllipseRing(positions, a, t, rAxis, uAxis, offset, aSemi, bSemi, ellipseSegments);
            var baseB = positions.Count;
            AppendEllipseRing(positions, b, t, rAxis, uAxis, offset, aSemi, bSemi, ellipseSegments);
            StitchRings(indices, baseA, baseB, ellipseSegments);
        }

        if (positions.Count < 3 || indices.Count < 3)
            return null;

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TriangleIndices = new Int32Collection(indices),
        };
        mesh.Normals = ComputeVertexNormals(mesh.Positions, mesh.TriangleIndices);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Per-station sphere radius: mean of (L+R+U+D)/4 over incident traverse legs.</summary>
    public static Dictionary<string, double> ComputeStationBallJointRadii(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3)
    {
        var sum = new Dictionary<string, double>(StringComparer.Ordinal);
        var cnt = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string st, float v)
        {
            if (string.IsNullOrEmpty(st))
                return;
            if (!sum.TryGetValue(st, out var s0))
            {
                sum[st] = v;
                cnt[st] = 1;
            }
            else
            {
                sum[st] = s0 + v;
                cnt[st]++;
            }
        }

        foreach (var shot in shots.Where(s => s.IsTraverseLeg))
        {
            if (!coords3.ContainsKey(shot.FromStation) || !coords3.ContainsKey(shot.ToStation))
                continue;
            var (lrL, lrR, lrU, lrD) = shot.EffectivePlanLrud();
            var v = (lrL + lrR + lrU + lrD) * 0.25f;
            Add(shot.FromStation, v);
            Add(shot.ToStation, v);
        }

        var r = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var kv in sum)
        {
            if (cnt.TryGetValue(kv.Key, out var c) && c > 0 && coords3.ContainsKey(kv.Key))
                r[kv.Key] = Math.Max(MinHalf, kv.Value / c);
        }

        return r;
    }

    /// <summary>One mesh: all traverse legs as thin solid cylinders (survey X,Y,Z).</summary>
    public static MeshGeometry3D? BuildTraverseTubeMesh(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        double pipeRadius,
        int ringSegments = 8)
    {
        var legs = shots.Where(s => s.IsTraverseLeg).ToList();
        if (legs.Count == 0 || pipeRadius < 1e-6)
            return null;
        var positions = new List<Point3D>();
        var indices = new List<int>();
        foreach (var shot in legs)
        {
            if (!coords3.TryGetValue(shot.FromStation, out var ca) || !coords3.TryGetValue(shot.ToStation, out var cb))
                continue;
            var a = new Vector3D(ca.X, ca.Y, ca.Z);
            var b = new Vector3D(cb.X, cb.Y, cb.Z);
            var t = b - a;
            var len = t.Length;
            if (len < 1e-5)
                continue;
            t.Normalize();
            if (!TryEllipseBasis(t, out var rAxis, out var uAxis))
                continue;
            var offset = new Vector3D(0, 0, 0);
            var baseA = positions.Count;
            AppendEllipseRing(positions, a, t, rAxis, uAxis, offset, pipeRadius, pipeRadius, ringSegments);
            var baseB = positions.Count;
            AppendEllipseRing(positions, b, t, rAxis, uAxis, offset, pipeRadius, pipeRadius, ringSegments);
            StitchRings(indices, baseA, baseB, ringSegments);
        }

        if (positions.Count < 3)
            return null;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TriangleIndices = new Int32Collection(indices),
        };
        mesh.Normals = ComputeVertexNormals(mesh.Positions, mesh.TriangleIndices);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Merged spheres at traverse stations (ball joints).</summary>
    public static MeshGeometry3D? BuildBallJointSpheresMesh(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        IReadOnlyDictionary<string, double> radii,
        int stacks = 10,
        int slices = 14)
    {
        var positions = new List<Point3D>();
        var indices = new List<int>();
        foreach (var kv in radii)
        {
            if (!coords3.TryGetValue(kv.Key, out var c))
                continue;
            var center = new Point3D(c.X, c.Y, c.Z);
            AppendSphereMesh(positions, indices, center, kv.Value, stacks, slices);
        }

        if (positions.Count < 3)
            return null;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TriangleIndices = new Int32Collection(indices),
        };
        mesh.Normals = ComputeVertexNormals(mesh.Positions, mesh.TriangleIndices);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Small station markers (same stations as <paramref name="stationNames"/>).</summary>
    public static MeshGeometry3D? BuildStationMarkerSpheresMesh(
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        IEnumerable<string> stationNames,
        double markerRadius,
        int stacks = 6,
        int slices = 8)
    {
        var positions = new List<Point3D>();
        var indices = new List<int>();
        foreach (var name in stationNames.Distinct(StringComparer.Ordinal))
        {
            if (!coords3.TryGetValue(name, out var c))
                continue;
            AppendSphereMesh(positions, indices, new Point3D(c.X, c.Y, c.Z), markerRadius, stacks, slices);
        }

        if (positions.Count < 3)
            return null;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TriangleIndices = new Int32Collection(indices),
        };
        mesh.Normals = ComputeVertexNormals(mesh.Positions, mesh.TriangleIndices);
        mesh.Freeze();
        return mesh;
    }

    /// <summary>Thin cylindrical segments (splays, vectors, depth braces).</summary>
    public static MeshGeometry3D? BuildLineSegmentsMesh(
        IEnumerable<(Point3D A, Point3D B)> segments,
        double radius,
        int ringSegments = 4)
    {
        var positions = new List<Point3D>();
        var indices = new List<int>();
        foreach (var (aPt, bPt) in segments)
        {
            var a = new Vector3D(aPt.X, aPt.Y, aPt.Z);
            var b = new Vector3D(bPt.X, bPt.Y, bPt.Z);
            var t = b - a;
            if (t.Length < 1e-5)
                continue;
            t.Normalize();
            if (!TryEllipseBasis(t, out var rAxis, out var uAxis))
                continue;
            var offset = new Vector3D(0, 0, 0);
            var baseA = positions.Count;
            AppendEllipseRing(positions, a, t, rAxis, uAxis, offset, radius, radius, ringSegments);
            var baseB = positions.Count;
            AppendEllipseRing(positions, b, t, rAxis, uAxis, offset, radius, radius, ringSegments);
            StitchRings(indices, baseA, baseB, ringSegments);
        }

        if (positions.Count < 3)
            return null;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TriangleIndices = new Int32Collection(indices),
        };
        mesh.Normals = ComputeVertexNormals(mesh.Positions, mesh.TriangleIndices);
        mesh.Freeze();
        return mesh;
    }

    private static void AppendSphereMesh(
        List<Point3D> positions,
        List<int> indices,
        Point3D center,
        double radius,
        int stacks,
        int slices)
    {
        if (stacks < 2 || slices < 3 || radius < 1e-6)
            return;
        var baseIdx = positions.Count;
        for (var i = 0; i <= stacks; i++)
        {
            var phi = Math.PI * i / stacks - Math.PI * 0.5;
            var cp = Math.Cos(phi);
            var sp = Math.Sin(phi);
            for (var j = 0; j < slices; j++)
            {
                var theta = 2 * Math.PI * j / slices;
                var ct = Math.Cos(theta);
                var sth = Math.Sin(theta);
                var x = center.X + radius * cp * ct;
                var y = center.Y + radius * cp * sth;
                var z = center.Z + radius * sp;
                positions.Add(new Point3D(x, y, z));
            }
        }

        var cols = slices;
        for (var i = 0; i < stacks; i++)
        {
            for (var j = 0; j < slices; j++)
            {
                var j1 = (j + 1) % slices;
                var i0 = baseIdx + i * cols + j;
                var i1 = baseIdx + i * cols + j1;
                var i2 = baseIdx + (i + 1) * cols + j1;
                var i3 = baseIdx + (i + 1) * cols + j;
                indices.Add(i0);
                indices.Add(i1);
                indices.Add(i2);
                indices.Add(i0);
                indices.Add(i2);
                indices.Add(i3);
            }
        }
    }

    private static bool TrySeedBasisFromWalk(
        IReadOnlyList<(string wf, string wt, ShotRecord sh)> walk,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        Vector3D tangent,
        out Vector3D rAxis,
        out Vector3D uAxis)
    {
        if (walk.Count > 0)
        {
            var (wf, wt, _) = walk[0];
            if (TrySeedBasisFromWalkStep(wf, wt, coords3, tangent, out rAxis, out uAxis))
                return true;
        }

        return TryEllipseBasis(tangent, out rAxis, out uAxis);
    }

    private static bool TrySeedBasisFromWalkStep(
        string walkFrom,
        string walkTo,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords3,
        Vector3D tangent,
        out Vector3D rAxis,
        out Vector3D uAxis)
    {
        if (!coords3.TryGetValue(walkFrom, out var ca) || !coords3.TryGetValue(walkTo, out var cb))
            return TryEllipseBasis(tangent, out rAxis, out uAxis);

        var dx = cb.X - ca.X;
        var dy = cb.Y - ca.Y;
        var len = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        if (len < 1e-4)
            return TryEllipseBasis(tangent, out rAxis, out uAxis);

        var fx = dx / len;
        var fy = dy / len;
        var planLeft = new Vector3D(-fy, fx, 0);
        var projected = planLeft - tangent * Vector3D.DotProduct(planLeft, tangent);
        if (projected.LengthSquared < 1e-12)
            return TryEllipseBasis(tangent, out rAxis, out uAxis);

        rAxis = projected;
        rAxis.Normalize();
        uAxis = Vector3D.CrossProduct(rAxis, tangent);
        uAxis.Normalize();
        return true;
    }

    private static bool PropagateEllipseBasis(
        Vector3D prevTangent,
        Vector3D prevRAxis,
        Vector3D newTangent,
        out Vector3D rAxis,
        out Vector3D uAxis)
    {
        newTangent.Normalize();
        var dot = Vector3D.DotProduct(prevTangent, newTangent);
        Vector3D rotated;
        if (dot > 0.9999)
            rotated = prevRAxis;
        else if (dot < -0.9999)
            rotated = -prevRAxis;
        else
        {
            var rotAxis = Vector3D.CrossProduct(prevTangent, newTangent);
            rotAxis.Normalize();
            var angle = Math.Acos(Math.Clamp(dot, -1, 1));
            rotated = RotateVector(prevRAxis, rotAxis, angle);
        }

        rotated -= newTangent * Vector3D.DotProduct(rotated, newTangent);
        if (rotated.LengthSquared < 1e-12)
            return TryEllipseBasis(newTangent, out rAxis, out uAxis);

        rAxis = rotated;
        rAxis.Normalize();
        uAxis = Vector3D.CrossProduct(rAxis, newTangent);
        uAxis.Normalize();
        return true;
    }

    private static Vector3D RotateVector(Vector3D v, Vector3D axis, double angle)
    {
        axis.Normalize();
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var cross = Vector3D.CrossProduct(axis, v);
        var dot = Vector3D.DotProduct(axis, v);
        return v * cos + cross * sin + axis * (dot * (1 - cos));
    }

    private static bool TryEllipseBasis(Vector3D tangent, out Vector3D rAxis, out Vector3D uAxis)
    {
        var refUp = Math.Abs(tangent.Z) < 0.85 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
        rAxis = Vector3D.CrossProduct(tangent, refUp);
        if (rAxis.LengthSquared < 1e-12)
        {
            refUp = new Vector3D(0, 1, 0);
            rAxis = Vector3D.CrossProduct(tangent, refUp);
        }

        if (rAxis.LengthSquared < 1e-12)
        {
            uAxis = default;
            return false;
        }

        rAxis.Normalize();
        uAxis = Vector3D.CrossProduct(rAxis, tangent);
        uAxis.Normalize();
        return true;
    }

    private static void AppendEllipseRing(
        List<Point3D> positions,
        Vector3D center,
        Vector3D tangent,
        Vector3D rAxis,
        Vector3D uAxis,
        Vector3D lrudOffset,
        double aSemi,
        double bSemi,
        int n)
    {
        var c = center + lrudOffset;
        for (var i = 0; i < n; i++)
        {
            var ang = 2 * Math.PI * i / n;
            var cos = Math.Cos(ang);
            var sin = Math.Sin(ang);
            var p = c + rAxis * (aSemi * cos) + uAxis * (bSemi * sin);
            positions.Add(new Point3D(p.X, p.Y, p.Z));
        }
    }

    private static void StitchRings(List<int> indices, int baseA, int baseB, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var i0 = baseA + i;
            var i1 = baseA + (i + 1) % n;
            var j0 = baseB + i;
            var j1 = baseB + (i + 1) % n;
            indices.Add(i0);
            indices.Add(j0);
            indices.Add(i1);
            indices.Add(i1);
            indices.Add(j0);
            indices.Add(j1);
        }
    }

    public static Vector3DCollection ComputeVertexNormals(Point3DCollection positions, Int32Collection tri)
    {
        var nV = positions.Count;
        var acc = new Vector3D[nV];
        for (var t = 0; t + 2 < tri.Count; t += 3)
        {
            var i0 = tri[t];
            var i1 = tri[t + 1];
            var i2 = tri[t + 2];
            var p0 = positions[i0];
            var p1 = positions[i1];
            var p2 = positions[i2];
            var e1 = p1 - p0;
            var e2 = p2 - p0;
            var fn = Vector3D.CrossProduct(e1, e2);
            if (fn.LengthSquared < 1e-18)
                continue;
            fn.Normalize();
            acc[i0] += fn;
            acc[i1] += fn;
            acc[i2] += fn;
        }

        var norms = new Vector3DCollection();
        for (var i = 0; i < nV; i++)
        {
            var v = acc[i];
            if (v.LengthSquared > 1e-18)
                v.Normalize();
            else
                v = new Vector3D(0, 0, 1);
            norms.Add(v);
        }

        return norms;
    }
}
