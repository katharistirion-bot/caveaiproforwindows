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

    /// <summary>Minimum 3; 8–16 recommended for smooth tubes.</summary>
    public const int DefaultEllipseSegments = 12;

    /// <summary>Returns null if there is no traversable geometry.</summary>
    public static MeshGeometry3D? BuildTubeMesh(
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
            return false;
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

    private static Vector3DCollection ComputeVertexNormals(Point3DCollection positions, Int32Collection tri)
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
