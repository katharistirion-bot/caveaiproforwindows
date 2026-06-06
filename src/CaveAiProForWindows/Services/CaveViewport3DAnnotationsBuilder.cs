using System.Globalization;
using System.Text.Json;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public enum Viewport3DLabelKind
{
    Station,
    Leg,
    Environment,
    Bracket,
    DepthSpan,
}

public sealed record Viewport3DLabelEntry(Point3D World, IReadOnlyList<string> Lines, Viewport3DLabelKind Kind);

public sealed class CaveViewport3DSceneLines
{
    public IReadOnlyList<(Point3D A, Point3D B)> SplaySegments { get; init; } =
        Array.Empty<(Point3D, Point3D)>();

    public IReadOnlyList<(Point3D A, Point3D B)> VectorSegments { get; init; } =
        Array.Empty<(Point3D, Point3D)>();

    public IReadOnlyList<(Point3D A, Point3D B)> DepthSpanSegments { get; init; } =
        Array.Empty<(Point3D, Point3D)>();
}

/// <summary>3D label anchors and auxiliary line segments for the pseudo-3D viewport.</summary>
public static class CaveViewport3DAnnotationsBuilder
{
    public static (CaveViewport3DSceneLines Lines, IReadOnlyList<Viewport3DLabelEntry> Labels) Build(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        var inv = CultureInfo.InvariantCulture;
        var labels = new List<Viewport3DLabelEntry>();
        var splays = SurveySplayGeometry3D.BuildSegments(project.Shots, coords).ToList();
        var vectors = BuildVectorSegments3D(project, coords);
        var depthSpans = new List<(Point3D, Point3D)>();

        foreach (var kv in coords)
        {
            var c = kv.Value;
            var zText = "Z " + c.Z.ToString("0.##", inv) + " m";
            labels.Add(new Viewport3DLabelEntry(
                new Point3D(c.X, c.Y, c.Z + 0.15),
                new[] { kv.Key, zText },
                Viewport3DLabelKind.Station));
        }

        foreach (var shot in project.Shots.Where(s => s.IsTraverseLeg))
        {
            if (!coords.TryGetValue(shot.FromStation.Trim(), out var a) ||
                !coords.TryGetValue(shot.ToStation.Trim(), out var b))
                continue;

            var mid = new Point3D((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
            var lines = new List<string> { shot.Distance.ToString("0.##", inv) + " m" };
            var secondary = SurveyMapAnnotationText.FormatLegAngles(shot.Azimuth, shot.Clino, inv);
            var dz = b.Z - a.Z;
            if (Math.Abs(dz) > 0.005)
                secondary += "  " + SurveyMapAnnotationText.FormatDeltaZ(dz, inv);
            lines.Add(secondary);

            var (L, R, U, D) = shot.EffectivePlanLrud();
            if (L + R + U + D > 0.02f)
                lines.Add(SurveyMapAnnotationText.FormatLrud(L, R, U, D, inv));

            labels.Add(new Viewport3DLabelEntry(mid, lines, Viewport3DLabelKind.Leg));
        }

        foreach (var kv in coords)
        {
            var name = kv.Key;
            var c = kv.Value;
            var outgoing = project.Shots.Where(s => NamesEq(s.FromStation, name) && s.IsTraverseLeg).ToList();
            var incoming = project.Shots.Where(s => NamesEq(s.ToStation, name) && s.IsTraverseLeg).ToList();
            var best = outgoing.LastOrDefault() ?? incoming.LastOrDefault();
            if (best == null)
                continue;
            var compact = ShotEnvironment.TryFormatCompactMapLine(best);
            if (string.IsNullOrWhiteSpace(compact))
                continue;
            labels.Add(new Viewport3DLabelEntry(
                new Point3D(c.X, c.Y, c.Z - 0.25),
                new[] { compact },
                Viewport3DLabelKind.Environment));
        }

        if (project.DepthSpanAnnotations.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in project.DepthSpanAnnotations.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;
                if (TryReadInt(el, "viewMode", out var vm) && vm is not 0 and not 3)
                    continue;

                var x1 = ReadFloat(el, "x1");
                var y1 = ReadFloat(el, "y1");
                var x2 = ReadFloat(el, "x2");
                var y2 = ReadFloat(el, "y2");
                var depth = ReadFloat(el, "depthMeters");
                var z1 = NearestZ(coords, x1, y1);
                var z2 = NearestZ(coords, x2, y2);
                var a = new Point3D(x1, y1, z1);
                var b = new Point3D(x2, y2, z2);
                depthSpans.Add((a, b));
                var lbl = depth > 0 ? depth.ToString("0.##", inv) + " m" : "depth";
                labels.Add(new Viewport3DLabelEntry(
                    new Point3D((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5),
                    new[] { "\u2195 " + lbl },
                    Viewport3DLabelKind.DepthSpan));
            }
        }

        if (project.Brackets.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in project.Brackets.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;
                if (TryReadInt(el, "viewMode", out var vm) && vm != 0)
                    continue;
                var x = ReadFloat(el, "x");
                var y = ReadFloat(el, "y");
                var z = NearestZ(coords, x, y);
                var desc = ReadString(el, "description") ?? "";
                var temp = ReadString(el, "temperature") ?? "";
                var text = string.Join(" · ", new[] { desc.Trim(), temp.Trim() }.Where(s => s.Length > 0));
                if (string.IsNullOrWhiteSpace(text))
                    text = "Bracket";
                labels.Add(new Viewport3DLabelEntry(new Point3D(x, y, z), new[] { text }, Viewport3DLabelKind.Bracket));
            }
        }

        return (new CaveViewport3DSceneLines
        {
            SplaySegments = splays,
            VectorSegments = vectors,
            DepthSpanSegments = depthSpans,
        }, labels);
    }

    private static List<(Point3D, Point3D)> BuildVectorSegments3D(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        var list = new List<(Point3D, Point3D)>();
        var polys = SurveyStationGeometry.ParseVectorLinesForViewMode(
            project.VectorLines,
            SurveyStationGeometry.AndroidViewModePlan);
        var defaultZ = coords.Count > 0 ? coords.Values.Average(c => c.Z) : 0f;

        foreach (var pl in polys)
        {
            for (var i = 1; i < pl.Points.Count; i++)
            {
                var p0 = pl.Points[i - 1];
                var p1 = pl.Points[i];
                var z0 = NearestZ(coords, p0.x, p0.y, defaultZ);
                var z1 = NearestZ(coords, p1.x, p1.y, defaultZ);
                list.Add((new Point3D(p0.x, p0.y, z0), new Point3D(p1.x, p1.y, z1)));
            }

            if (pl.Closed && pl.Points.Count >= 2)
            {
                var p0 = pl.Points[^1];
                var p1 = pl.Points[0];
                var z0 = NearestZ(coords, p0.x, p0.y, defaultZ);
                var z1 = NearestZ(coords, p1.x, p1.y, defaultZ);
                list.Add((new Point3D(p0.x, p0.y, z0), new Point3D(p1.x, p1.y, z1)));
            }
        }

        return list;
    }

    private static float NearestZ(
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        float x,
        float y,
        float fallback = 0)
    {
        if (coords.Count == 0)
            return fallback;
        var best = coords.Values.First();
        var bestD = double.MaxValue;
        foreach (var c in coords.Values)
        {
            var d = (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y);
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }

        return best.Z;
    }

    private static bool NamesEq(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static float ReadFloat(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.TryGetSingle(out var f) ? f : 0f;

    private static bool TryReadInt(JsonElement el, string name, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        if (p.TryGetInt32(out value))
            return true;
        if (p.TryGetDouble(out var d))
        {
            value = (int)d;
            return true;
        }

        return false;
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

/// <summary>Shared leg / LRUD label formatting for 2D and 3D map overlays.</summary>
public static class SurveyMapAnnotationText
{
    public static string FormatLegAngles(float azimuth, float clino, CultureInfo inv)
    {
        var cl = clino switch
        {
            > 0.45f => "\u2197" + clino.ToString("0.#", inv) + "\u00b0",
            < -0.45f => "\u2198" + Math.Abs(clino).ToString("0.#", inv) + "\u00b0",
            _ => "\u2192",
        };
        return azimuth.ToString("0", inv) + "\u00b0 " + cl;
    }

    public static string FormatDeltaZ(double dz, CultureInfo inv)
    {
        var arrow = dz > 0 ? "\u2191" : "\u2193";
        return arrow + Math.Abs(dz).ToString("0.##", inv) + " m";
    }

    public static string FormatLrud(float l, float r, float u, float d, CultureInfo inv) =>
        "L" + l.ToString("0.#", inv) +
        " R" + r.ToString("0.#", inv) +
        " U" + u.ToString("0.#", inv) +
        " D" + d.ToString("0.#", inv);
}
