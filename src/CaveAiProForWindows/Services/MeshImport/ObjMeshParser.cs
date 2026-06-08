using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.MeshImport;

/// <summary>Minimal Wavefront OBJ loader (v + f). 100% local — no Assimp native dependency.</summary>
public static class ObjMeshParser
{
    public static ParsedTriangleMesh ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("OBJ file not found.", path);
        return ParseLines(File.ReadLines(path));
    }

    public static ParsedTriangleMesh ParseLines(IEnumerable<string> lines)
    {
        var verts = new List<Point3D>();
        var indices = new Int32Collection();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                    double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                {
                    verts.Add(new Point3D(x, y, z));
                }
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                    continue;

                var face = new List<int>();
                for (var i = 1; i < parts.Length; i++)
                {
                    var tok = parts[i].Split('/')[0];
                    if (int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vi))
                    {
                        if (vi < 0)
                            vi = verts.Count + vi + 1;
                        face.Add(vi - 1);
                    }
                }

                for (var i = 1; i < face.Count - 1; i++)
                {
                    indices.Add(face[0]);
                    indices.Add(face[i]);
                    indices.Add(face[i + 1]);
                }
            }
        }

        return BuildMesh(verts, indices);
    }

    private static ParsedTriangleMesh BuildMesh(IReadOnlyList<Point3D> verts, Int32Collection indices)
    {
        if (verts.Count == 0 || indices.Count < 3)
            throw new InvalidDataException("OBJ contains no triangle geometry.");

        var positions = new Point3DCollection(verts);
        var min = new Point3D(
            verts.Min(v => v.X),
            verts.Min(v => v.Y),
            verts.Min(v => v.Z));
        var max = new Point3D(
            verts.Max(v => v.X),
            verts.Max(v => v.Y),
            verts.Max(v => v.Z));

        return new ParsedTriangleMesh
        {
            Positions = positions,
            TriangleIndices = indices,
            BoundsMin = min,
            BoundsMax = max,
        };
    }
}
