using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.MeshImport;

/// <summary>ASCII and binary STL loader — local only.</summary>
public static class StlMeshParser
{
    public static ParsedTriangleMesh ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("STL file not found.", path);

        using var fs = File.OpenRead(path);
        var header = new byte[80];
        if (fs.Read(header, 0, 80) != 80)
            throw new InvalidDataException("STL header too short.");

        if (LooksLikeAscii(header))
        {
            fs.Position = 0;
            using var reader = new StreamReader(fs, Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
            return ParseAscii(reader);
        }

        return ParseBinary(fs);
    }

    private static bool LooksLikeAscii(byte[] header)
    {
        var text = Encoding.ASCII.GetString(header).TrimStart();
        return text.StartsWith("solid", StringComparison.OrdinalIgnoreCase);
    }

    private static ParsedTriangleMesh ParseAscii(TextReader reader)
    {
        var verts = new List<Point3D>();
        var indices = new Int32Collection();
        var vertexMap = new Dictionary<(long, long, long), int>();

        while (reader.ReadLine() is { } line)
        {
            var t = line.Trim();
            if (!t.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                continue;

            if (!TryParseTriple(parts[1], parts[2], parts[3], out var p))
                continue;

            var key = Quantize(p);
            if (!vertexMap.TryGetValue(key, out var idx))
            {
                idx = verts.Count;
                verts.Add(p);
                vertexMap[key] = idx;
            }

            indices.Add(idx);
            if (indices.Count % 3 == 0 && indices.Count > 0)
            {
                // triangle complete — STL repeats vertices per facet
            }
        }

        if (verts.Count == 0 || indices.Count < 3)
            throw new InvalidDataException("ASCII STL contains no triangles.");

        var positions = new Point3DCollection(verts);
        return new ParsedTriangleMesh
        {
            Positions = positions,
            TriangleIndices = indices,
            BoundsMin = new Point3D(verts.Min(p => p.X), verts.Min(p => p.Y), verts.Min(p => p.Z)),
            BoundsMax = new Point3D(verts.Max(p => p.X), verts.Max(p => p.Y), verts.Max(p => p.Z)),
        };
    }

    private static ParsedTriangleMesh ParseBinary(Stream fs)
    {
        var countBytes = new byte[4];
        if (fs.Read(countBytes, 0, 4) != 4)
            throw new InvalidDataException("STL triangle count missing.");
        var triCount = BitConverter.ToUInt32(countBytes, 0);

        var verts = new List<Point3D>();
        var indices = new Int32Collection();
        var buf = new byte[50];

        for (var t = 0u; t < triCount; t++)
        {
            if (fs.Read(buf, 0, 50) != 50)
                break;

            for (var v = 0; v < 3; v++)
            {
                var off = 12 + v * 12;
                var x = BitConverter.ToSingle(buf, off);
                var y = BitConverter.ToSingle(buf, off + 4);
                var z = BitConverter.ToSingle(buf, off + 8);
                indices.Add(verts.Count);
                verts.Add(new Point3D(x, y, z));
            }
        }

        if (verts.Count == 0)
            throw new InvalidDataException("STL contains no triangles.");

        var positions = new Point3DCollection(verts);
        return new ParsedTriangleMesh
        {
            Positions = positions,
            TriangleIndices = indices,
            BoundsMin = new Point3D(verts.Min(p => p.X), verts.Min(p => p.Y), verts.Min(p => p.Z)),
            BoundsMax = new Point3D(verts.Max(p => p.X), verts.Max(p => p.Y), verts.Max(p => p.Z)),
        };
    }

    private static bool TryParseTriple(string sx, string sy, string sz, out Point3D p)
    {
        p = default;
        if (!double.TryParse(sx, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(sy, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !double.TryParse(sz, NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            return false;
        p = new Point3D(x, y, z);
        return true;
    }

    private static (long, long, long) Quantize(Point3D p) =>
        ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000), (long)Math.Round(p.Z * 1000));
}
