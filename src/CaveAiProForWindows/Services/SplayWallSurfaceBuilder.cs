using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CaveAiProForWindows.Services;

/// <summary>Semi-opaque wall facets from 3D splay segments (station fan or ribbon strips).</summary>
public static class SplayWallSurfaceBuilder
{
    private const double StationKeyScale = 100.0;

    /// <summary>Builds triangular wall fans at each splay origin station.</summary>
    public static MeshGeometry3D? BuildStationFanMesh(IReadOnlyList<(Point3D A, Point3D B)> segments)
    {
        if (segments.Count == 0)
            return null;

        var byStation = new Dictionary<long, List<(Point3D Origin, Point3D Wall)>>();
        foreach (var (a, b) in segments)
        {
            var segLen = (b - a).Length;
            if (segLen < 0.08)
                continue;
            var key = StationKey(a);
            if (!byStation.TryGetValue(key, out var list))
            {
                list = new List<(Point3D, Point3D)>();
                byStation[key] = list;
            }

            list.Add((a, b));
        }

        var positions = new List<Point3D>();
        var indices = new List<int>();
        foreach (var group in byStation.Values)
        {
            if (group.Count < 2)
                continue;

            var origin = group[0].Origin;
            var walls = group
                .Select(g => g.Wall)
                .OrderBy(w => Math.Atan2(w.Y - origin.Y, w.X - origin.X))
                .ToList();

            for (var i = 0; i < walls.Count; i++)
            {
                var j = (i + 1) % walls.Count;
                AddTriangle(positions, indices, origin, walls[i], walls[j]);
            }
        }

        if (positions.Count < 3)
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

    private static long StationKey(Point3D p) =>
        ((long)Math.Round(p.X * StationKeyScale) << 42)
        ^ ((long)Math.Round(p.Y * StationKeyScale) << 21)
        ^ (long)Math.Round(p.Z * StationKeyScale);

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
