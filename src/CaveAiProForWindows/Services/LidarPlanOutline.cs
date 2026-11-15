using System.IO;
using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Plan-wall outline from an interior scan. Same grid walk as the phone.
/// A degree-coordinate cloud is refused so it is not drawn on the cave plan.
/// </summary>
public static class LidarPlanOutline
{
    public const string LineType = "LIDAR_PLAN";
    private const int Grid = 64;
    private const int SampleCap = 80_000;

    public static bool LooksGeographic(IReadOnlyList<float> xs, IReadOnlyList<float> ys)
    {
        if (xs.Count == 0 || xs.Count != ys.Count)
            return false;
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        var far = false;
        for (var i = 0; i < xs.Count; i++)
        {
            var x = xs[i];
            var y = ys[i];
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            if (Math.Abs(x) > 20f || Math.Abs(y) > 20f)
                far = true;
        }

        if (maxX > 180f || minX < -180f || maxY > 90f || minY < -90f)
            return false;
        var span = Math.Max(maxX - minX, maxY - minY);
        return far && span < 0.5f;
    }

    public static List<List<(float X, float Y)>> Polylines(IReadOnlyList<float> xs, IReadOnlyList<float> ys)
    {
        if (xs.Count < 8 || xs.Count != ys.Count || LooksGeographic(xs, ys))
            return [];

        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        for (var i = 0; i < xs.Count; i++)
        {
            if (xs[i] < minX) minX = xs[i];
            if (xs[i] > maxX) maxX = xs[i];
            if (ys[i] < minY) minY = ys[i];
            if (ys[i] > maxY) maxY = ys[i];
        }

        var span = Math.Max(maxX - minX, maxY - minY);
        if (!float.IsFinite(span) || span < 0.05f)
            return [];

        var cell = span / Grid;
        var occupied = new HashSet<long>();
        for (var i = 0; i < xs.Count; i++)
        {
            var cx = Math.Clamp((int)((xs[i] - minX) / cell), 0, Grid - 1);
            var cy = Math.Clamp((int)((ys[i] - minY) / cell), 0, Grid - 1);
            occupied.Add(Pack(cx, cy));
        }

        var adj = new Dictionary<long, List<long>>();
        void Link(int ax, int ay, int bx, int by)
        {
            var a = Pack(ax, ay);
            var b = Pack(bx, by);
            if (!adj.TryGetValue(a, out var listA))
                adj[a] = listA = [];
            listA.Add(b);
            if (!adj.TryGetValue(b, out var listB))
                adj[b] = listB = [];
            listB.Add(a);
        }

        foreach (var key in occupied)
        {
            var cx = (int)(key >> 32);
            var cy = (int)key;
            if (!occupied.Contains(Pack(cx, cy - 1)))
                Link(cx, cy, cx + 1, cy);
            if (!occupied.Contains(Pack(cx + 1, cy)))
                Link(cx + 1, cy, cx + 1, cy + 1);
            if (!occupied.Contains(Pack(cx, cy + 1)))
                Link(cx + 1, cy + 1, cx, cy + 1);
            if (!occupied.Contains(Pack(cx - 1, cy)))
                Link(cx, cy + 1, cx, cy);
        }

        var raw = new List<List<(float X, float Y)>>();
        while (true)
        {
            long? start = null;
            foreach (var pair in adj)
            {
                if (pair.Value.Count == 1)
                {
                    start = pair.Key;
                    break;
                }
            }

            if (start == null)
            {
                foreach (var pair in adj)
                {
                    if (pair.Value.Count > 0)
                    {
                        start = pair.Key;
                        break;
                    }
                }
            }

            if (start == null)
                break;
            var chain = Walk(adj, start.Value);
            if (chain.Count < 4)
                continue;
            var metres = new List<(float X, float Y)>(chain.Count);
            foreach (var node in chain)
            {
                var gx = (int)(node >> 32);
                var gy = (int)node;
                metres.Add((minX + gx * cell, minY + gy * cell));
            }

            raw.Add(Decimate(metres));
        }

        if (raw.Count == 0)
            return [];
        double sx = 0, sy = 0;
        var n = 0;
        foreach (var line in raw)
        {
            foreach (var p in line)
            {
                sx += p.X;
                sy += p.Y;
                n++;
            }
        }

        if (n == 0)
            return [];
        var cx0 = (float)(sx / n);
        var cy0 = (float)(sy / n);
        return raw.ConvertAll(line => line.ConvertAll(p => (p.X - cx0, p.Y - cy0)));
    }

    public static List<(float X, float Y, float Z)> ParseAsciiCloud(string text)
    {
        var points = new List<(float, float, float)>();
        foreach (var raw in text.Split('\n', '\r'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var parts = line.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                continue;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                continue;
            points.Add((x, y, z));
        }

        return points;
    }

    public static List<(float X, float Y, float Z)> ParseObjVertices(string text)
    {
        var points = new List<(float, float, float)>();
        foreach (var raw in text.Split('\n', '\r'))
        {
            var line = raw.Trim();
            if (line.Length < 3 || line[0] != 'v' || (line[1] != ' ' && line[1] != '\t'))
                continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                continue;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                continue;
            points.Add((x, y, z));
        }

        return points;
    }

    /// <summary>One CaveAI binary frame: magic, version, count, XYZ floats. Y-up, plan uses X and Z.</summary>
    public static List<(float X, float Y, float Z)> ParseBinaryFrame(ReadOnlySpan<byte> data)
    {
        var points = new List<(float, float, float)>();
        if (data.Length < 7 || data[0] != 0xCA || data[1] != 0x1D || data[2] != 0x01 || data[3] != 0x02 || data[4] != 0x01)
            return points;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        var need = 7 + count * 12;
        if (data.Length < need)
            return points;
        var offset = 7;
        for (var i = 0; i < count; i++)
        {
            var x = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset, 4));
            var y = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset + 4, 4));
            var z = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset + 8, 4));
            offset += 12;
            if (float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z))
                points.Add((x, y, z));
        }

        return points;
    }

    public static List<(float X, float Y)> OutlineFromYUp(IReadOnlyList<(float X, float Y, float Z)> points)
    {
        var xs = new List<float>(points.Count);
        var ys = new List<float>(points.Count);
        foreach (var p in points)
        {
            xs.Add(p.X);
            ys.Add(p.Z);
        }

        return Flatten(Polylines(xs, ys));
    }

    /// <summary>Uncompressed LAS point formats 0–3. LAZ is refused.</summary>
    public static List<(float X, float Y, float Z)> ReadLas(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 227 || bytes[0] != (byte)'L' || bytes[1] != (byte)'A' || bytes[2] != (byte)'S' || bytes[3] != (byte)'F')
            throw new InvalidDataException("This is not a LAS file.");
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(94, 2));
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(96, 4));
        var format = bytes[104];
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(105, 2));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(107, 4));
        if ((format & 0x80) != 0 || format > 3)
            throw new InvalidDataException("Compressed LAZ is not read here. Export an uncompressed LAS, or use OBJ.");
        if (recordLength < 12 || offset >= bytes.Length)
            throw new InvalidDataException("The LAS point block is missing.");
        var scaleX = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(131, 8));
        var scaleY = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(139, 8));
        var scaleZ = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(147, 8));
        var offX = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(155, 8));
        var offY = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(163, 8));
        var offZ = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(171, 8));
        _ = headerSize;
        var points = new List<(float, float, float)>();
        var cursor = (int)offset;
        var take = (int)Math.Min(count, SampleCap);
        for (var i = 0; i < take; i++)
        {
            if (cursor + 12 > bytes.Length)
                break;
            var x = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor, 4)) * scaleX + offX;
            var y = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor + 4, 4)) * scaleY + offY;
            var z = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cursor + 8, 4)) * scaleZ + offZ;
            points.Add(((float)x, (float)y, (float)z));
            cursor += recordLength;
        }

        return points;
    }

    public static List<(float X, float Y)> RotateAndPlace(
        IReadOnlyList<(float X, float Y)> centered,
        float east,
        float north,
        int degreesClockwise)
    {
        var deg = ((degreesClockwise % 360) + 360) % 360;
        var rad = deg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var placed = new List<(float X, float Y)>(centered.Count);
        foreach (var p in centered)
        {
            var x = deg == 0 ? p.X : (float)(p.X * cos + p.Y * sin);
            var y = deg == 0 ? p.Y : (float)(-p.X * sin + p.Y * cos);
            placed.Add((x + east, y + north));
        }

        return placed;
    }

    public static (float East, float North) Anchor(CaveProjectDocument project, string? station)
    {
        if (string.IsNullOrWhiteSpace(station) || station.Equals("Entrance", StringComparison.OrdinalIgnoreCase))
            return (0f, 0f);
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        foreach (var pair in coords)
        {
            if (pair.Key.Equals(station.Trim(), StringComparison.OrdinalIgnoreCase))
                return (pair.Value.X, pair.Value.Y);
        }

        throw new InvalidOperationException("That station is not in this survey.");
    }

    public static int ReplacePlanLines(CaveProjectDocument project, IReadOnlyList<(float X, float Y)> points)
    {
        var kept = new List<JsonElement>();
        if (project.VectorLines is { ValueKind: JsonValueKind.Array } existing)
        {
            foreach (var el in existing.EnumerateArray())
            {
                if (el.TryGetProperty("type", out var type) &&
                    string.Equals(type.GetString(), LineType, StringComparison.Ordinal))
                    continue;
                kept.Add(el.Clone());
            }
        }

        if (points.Count >= 2)
        {
            var payload = new Dictionary<string, object?>
            {
                ["type"] = LineType,
                ["viewMode"] = 0,
                ["points"] = points.Select(p => new[] { p.X, p.Y }).ToArray(),
            };
            kept.Add(JsonSerializer.SerializeToElement(payload));
        }

        project.VectorLines = JsonSerializer.SerializeToElement(kept);
        return points.Count >= 2 ? 1 : 0;
    }

    private static List<(float X, float Y)> Flatten(List<List<(float X, float Y)>> lines)
    {
        var flat = new List<(float X, float Y)>();
        foreach (var line in lines)
            flat.AddRange(line);
        return flat;
    }

    private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

    private static List<long> Walk(Dictionary<long, List<long>> adj, long start)
    {
        var forward = new List<long> { start };
        var prev = long.MinValue;
        var cur = start;
        while (true)
        {
            var next = Take(adj, cur, prev);
            if (next == null)
                break;
            if (next.Value == start)
            {
                forward.Add(start);
                return forward;
            }

            forward.Add(next.Value);
            prev = cur;
            cur = next.Value;
            if (forward.Count > Grid * Grid * 4)
                break;
        }

        var backward = new List<long>();
        prev = long.MinValue;
        cur = start;
        while (true)
        {
            var next = Take(adj, cur, prev);
            if (next == null)
                break;
            backward.Add(next.Value);
            prev = cur;
            cur = next.Value;
            if (backward.Count > Grid * Grid * 4)
                break;
        }

        backward.Reverse();
        backward.AddRange(forward);
        return backward;
    }

    private static long? Take(Dictionary<long, List<long>> adj, long cur, long prev)
    {
        if (!adj.TryGetValue(cur, out var list))
            return null;
        var idx = -1;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] != prev)
            {
                idx = i;
                break;
            }
        }

        if (idx < 0)
            return null;
        var next = list[idx];
        list.RemoveAt(idx);
        if (adj.TryGetValue(next, out var other))
            other.Remove(cur);
        return next;
    }

    private static List<(float X, float Y)> Decimate(List<(float X, float Y)> points)
    {
        if (points.Count <= 300)
            return points;
        var step = (points.Count + 299) / 300;
        var outPoints = new List<(float X, float Y)>();
        for (var i = 0; i < points.Count; i += step)
            outPoints.Add(points[i]);
        if (outPoints[^1] != points[^1])
            outPoints.Add(points[^1]);
        return outPoints;
    }
}
