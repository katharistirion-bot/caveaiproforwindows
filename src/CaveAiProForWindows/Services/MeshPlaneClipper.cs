using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CaveAiProForWindows.Services;

/// <summary>Clips triangle meshes against a plane, keeping the positive half-space.</summary>
public static class MeshPlaneClipper
{
    private const double Epsilon = 1e-6;

    public static MeshGeometry3D? ClipKeepPositiveHalfSpace(
        MeshGeometry3D? source,
        Point3D planePoint,
        Vector3D planeNormal)
    {
        if (source == null || source.Positions.Count < 3 || source.TriangleIndices.Count < 3)
            return source;

        if (planeNormal.LengthSquared < 1e-12)
            return source;

        planeNormal.Normalize();

        var positions = new List<Point3D>();
        var indices = new List<int>();
        var srcPos = source.Positions;
        var srcIdx = source.TriangleIndices;

        for (var t = 0; t < srcIdx.Count; t += 3)
        {
            var v0 = srcPos[srcIdx[t]];
            var v1 = srcPos[srcIdx[t + 1]];
            var v2 = srcPos[srcIdx[t + 2]];
            ClipTriangle(v0, v1, v2, planePoint, planeNormal, positions, indices);
        }

        if (positions.Count < 3 || indices.Count < 3)
            return null;

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TriangleIndices = new Int32Collection(indices),
        };
        mesh.Normals = CaveSurveyTubeMeshBuilder.ComputeVertexNormals(mesh.Positions, mesh.TriangleIndices);
        mesh.Freeze();
        return mesh;
    }

    private static double SignedDistance(Point3D p, Point3D planePoint, Vector3D planeNormal) =>
        Vector3D.DotProduct(new Vector3D(p.X - planePoint.X, p.Y - planePoint.Y, p.Z - planePoint.Z), planeNormal);

    private static void ClipTriangle(
        Point3D v0,
        Point3D v1,
        Point3D v2,
        Point3D planePoint,
        Vector3D planeNormal,
        List<Point3D> positions,
        List<int> indices)
    {
        var d0 = SignedDistance(v0, planePoint, planeNormal);
        var d1 = SignedDistance(v1, planePoint, planeNormal);
        var d2 = SignedDistance(v2, planePoint, planeNormal);

        var poly = new List<Point3D>();
        ClipEdge(v0, v1, d0, d1, poly);
        ClipEdge(v1, v2, d1, d2, poly);
        ClipEdge(v2, v0, d2, d0, poly);

        if (poly.Count == 3)
        {
            AddTriangle(positions, indices, poly[0], poly[1], poly[2]);
        }
        else if (poly.Count == 4)
        {
            AddTriangle(positions, indices, poly[0], poly[1], poly[2]);
            AddTriangle(positions, indices, poly[0], poly[2], poly[3]);
        }
    }

    private static void ClipEdge(Point3D a, Point3D b, double da, double db, List<Point3D> poly)
    {
        var inA = da >= -Epsilon;
        var inB = db >= -Epsilon;
        if (inA)
            poly.Add(a);
        if (inA != inB)
            poly.Add(IntersectEdge(a, b, da, db));
    }

    private static Point3D IntersectEdge(Point3D a, Point3D b, double da, double db)
    {
        var t = da / (da - db);
        return new Point3D(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);
    }

    private static void AddTriangle(
        List<Point3D> positions,
        List<int> indices,
        Point3D a,
        Point3D b,
        Point3D c)
    {
        var baseIdx = positions.Count;
        positions.Add(a);
        positions.Add(b);
        positions.Add(c);
        indices.Add(baseIdx);
        indices.Add(baseIdx + 1);
        indices.Add(baseIdx + 2);
    }
}
