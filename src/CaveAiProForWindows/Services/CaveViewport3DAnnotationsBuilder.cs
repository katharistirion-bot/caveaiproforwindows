using System.Globalization;
using System.Text.Json;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public enum Viewport3DLabelKind
{
    Station,
    Symbol,
    FieldCatalog,
    StationSnapshot,
    AiTag,
    Leg,
    Environment,
    Bracket,
    DepthSpan,
}

public sealed record Viewport3DLabelEntry(
    Point3D World,
    IReadOnlyList<string> Lines,
    Viewport3DLabelKind Kind,
    string? TargetStation = null,
    string? FieldCatalogTitle = null,
    SurveyStationGeometry.PlanMapSymbol? MapSymbol = null);

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
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        Viewport3DAnnotationOptions? options = null)
    {
        options ??= Viewport3DAnnotationOptions.AllOn;
        var inv = CultureInfo.InvariantCulture;
        var labels = new List<Viewport3DLabelEntry>();
        var splays = SurveySplayGeometry3D.BuildSegments(project.Shots, coords).ToList();
        var vectors = BuildVectorSegments3D(project, coords);
        var depthSpans = new List<(Point3D, Point3D)>();
        var chainage = SurveyTraverseChainage.TryCompute(project);

        if (options.ShowLabels && options.ShowStationNames)
        {
            foreach (var kv in coords)
            {
                var c = kv.Value;
                var lines = new List<string> { kv.Key };
                if (options.ShowStationZ)
                    lines.Add("Z " + c.Z.ToString("0.##", inv) + " m");
                if (chainage != null && chainage.TryGetChainage(kv.Key, out var s))
                    lines.Add("S " + s.ToString("0.##", inv) + " m");
                labels.Add(new Viewport3DLabelEntry(
                    new Point3D(c.X, c.Y, c.Z + 0.18),
                    lines,
                    Viewport3DLabelKind.Station,
                    TargetStation: kv.Key));
            }
        }
        else if (options.ShowLabels && options.ShowStationZ)
        {
            foreach (var kv in coords)
            {
                var c = kv.Value;
                labels.Add(new Viewport3DLabelEntry(
                    new Point3D(c.X, c.Y, c.Z + 0.18),
                    new[] { kv.Key, "Z " + c.Z.ToString("0.##", inv) + " m" },
                    Viewport3DLabelKind.Station,
                    TargetStation: kv.Key));
            }
        }

        if (options.ShowLabels && options.ShowLegDetails)
        {
            foreach (var shot in project.Shots.Where(s => s.IsTraverseLeg))
            {
                if (!coords.TryGetValue(shot.FromStation.Trim(), out var a) ||
                    !coords.TryGetValue(shot.ToStation.Trim(), out var b))
                    continue;

                var mid = new Point3D((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
                var lines = BuildLegLabelLines(shot, a, b, chainage, inv);
                labels.Add(new Viewport3DLabelEntry(mid, lines, Viewport3DLabelKind.Leg));
            }
        }

        if (options.ShowLabels && options.ShowEnvironment)
        {
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
        }

        if (options.ShowLabels && options.ShowDepthSpans &&
            project.DepthSpanAnnotations.ValueKind == JsonValueKind.Array)
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
                if (depth <= 0 && TryReadFloat(el, "depthSpanM", out var alt))
                    depth = alt;
                var z1 = NearestZ(coords, x1, y1);
                var z2 = NearestZ(coords, x2, y2);
                var a = new Point3D(x1, y1, z1);
                var b = new Point3D(x2, y2, z2);
                depthSpans.Add((a, b));
                var spanLen = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                var lbl = depth > 0 ? depth.ToString("0.##", inv) + " m depth" : "depth span";
                var detail = spanLen > 0.05
                    ? lbl + " · span " + spanLen.ToString("0.##", inv) + " m"
                    : lbl;
                labels.Add(new Viewport3DLabelEntry(
                    new Point3D((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5),
                    new[] { "\u2195 " + detail },
                    Viewport3DLabelKind.DepthSpan));
            }
        }

        if (options.ShowLabels && options.ShowBrackets && project.Brackets.ValueKind == JsonValueKind.Array)
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

        if (options.ShowLabels && options.ShowMapSymbols)
            AppendMapSymbolLabels(project, coords, labels);

        if (options.ShowLabels && options.ShowFieldCatalog)
            AppendFieldCatalogLabels(project, coords, labels);

        if (options.ShowLabels && options.ShowStationSnapshots)
            AppendStationSnapshotLabels(project, coords, labels, inv);

        if (options.ShowLabels && options.ShowAiTags)
            AppendAiClassificationLabels(project, coords, labels, inv);

        return (new CaveViewport3DSceneLines
        {
            SplaySegments = splays,
            VectorSegments = vectors,
            DepthSpanSegments = depthSpans,
        }, labels);
    }

    private static List<string> BuildLegLabelLines(
        ShotRecord shot,
        SurveyStationGeometry.StationPlanCoords a,
        SurveyStationGeometry.StationPlanCoords b,
        TraverseChainageResult? chainage,
        CultureInfo inv)
    {
        var (primary, secondary, lrud, chainageLine) =
            SurveyLegLabelFormatter.FormatLegLabel(shot, a, b, chainage, includeEndpointNames: true);
        var lines = new List<string>();
        var split = primary.Split("  ·  ", 2, StringSplitOptions.None);
        if (split.Length == 2)
        {
            lines.Add(split[0]);
            lines.Add(split[1]);
        }
        else
        {
            lines.Add(primary);
        }

        lines.Add(secondary);
        if (lrud != null)
            lines.Add(lrud);
        if (chainageLine != null)
            lines.Add(chainageLine);
        return lines;
    }

    private static void AppendMapSymbolLabels(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        List<Viewport3DLabelEntry> labels)
    {
        foreach (var sym in SurveyStationGeometry.ParsePlanMapSymbols(project))
        {
            var title = MapSymbolIconResolver.ExportLabel(sym);
            var lines = new List<string> { title };
            var station = AndroidSurveyStationMatcher.FindNearestStation(sym.X, sym.Y, coords);
            if (!string.IsNullOrWhiteSpace(station))
                lines.Add("@ " + station);
            var z = sym.Z != 0 ? sym.Z : NearestZ(coords, sym.X, sym.Y);
            labels.Add(new Viewport3DLabelEntry(
                new Point3D(sym.X, sym.Y, z + 0.14),
                lines,
                Viewport3DLabelKind.Symbol,
                TargetStation: station,
                MapSymbol: sym));
        }
    }

    private static void AppendFieldCatalogLabels(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        List<Viewport3DLabelEntry> labels)
    {
        foreach (var pin in FieldCatalogMapPinCollector.Collect(project))
        {
            var lines = new List<string> { pin.Title };
            if (!string.IsNullOrWhiteSpace(pin.CategoryLabel))
                lines.Add(pin.CategoryLabel);
            if (!string.IsNullOrWhiteSpace(pin.ScientificName))
                lines.Add(pin.ScientificName);
            var station = AndroidSurveyStationMatcher.FindNearestStation(pin.X, pin.Y, coords);
            if (!string.IsNullOrWhiteSpace(station))
                lines.Add("@ " + station);
            var z = NearestZ(coords, pin.X, pin.Y);
            labels.Add(new Viewport3DLabelEntry(
                new Point3D(pin.X, pin.Y, z + 0.16),
                lines,
                Viewport3DLabelKind.FieldCatalog,
                TargetStation: station,
                FieldCatalogTitle: pin.Title));
        }

        foreach (var rec in GeoBioRecordsService.Build(project))
        {
            if (string.IsNullOrWhiteSpace(rec.Station))
                continue;
            var station = rec.Station.Trim();
            if (!coords.TryGetValue(station, out var c))
                continue;
            if (rec.CoordinatesSummary != null && rec.CoordinatesSummary.StartsWith("x=", StringComparison.Ordinal))
                continue;
            var lines = new List<string> { rec.Title };
            if (rec.FieldKind is { } fk)
                lines.Add(FieldCatalogEntryKindMapper.DisplayLabel(fk));
            else if (rec.Category == GeoBioCategory.Rock)
                lines.Add("Geology");
            else if (rec.Category == GeoBioCategory.Organism)
                lines.Add("Biology");
            if (!string.IsNullOrWhiteSpace(rec.ScientificName))
                lines.Add(rec.ScientificName);
            lines.Add("@ " + station);
            labels.Add(new Viewport3DLabelEntry(
                new Point3D(c.X, c.Y, c.Z + 0.2),
                lines,
                Viewport3DLabelKind.FieldCatalog,
                TargetStation: station,
                FieldCatalogTitle: rec.Title));
        }
    }

    private static void AppendStationSnapshotLabels(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        List<Viewport3DLabelEntry> labels,
        CultureInfo inv)
    {
        foreach (var snap in project.StationEnvironmentSnapshots)
        {
            var station = (snap.StationName ?? "").Trim();
            if (station.Length == 0 || !coords.TryGetValue(station, out var c))
                continue;

            var lines = new List<string> { station };
            var sensors = FormatStationSnapshotSensors(snap, inv);
            if (!string.IsNullOrWhiteSpace(sensors))
                lines.Add(sensors);
            if (!string.IsNullOrWhiteSpace(snap.Notes))
                lines.Add(snap.Notes.Trim());
            labels.Add(new Viewport3DLabelEntry(
                new Point3D(c.X, c.Y, c.Z - 0.22),
                lines,
                Viewport3DLabelKind.StationSnapshot));
        }
    }

    private static void AppendAiClassificationLabels(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        List<Viewport3DLabelEntry> labels,
        CultureInfo inv)
    {
        foreach (var tag in project.SurveyAiClassifications)
        {
            if (string.IsNullOrWhiteSpace(tag.Label))
                continue;
            var station = ResolveAiTagStation(tag, project, coords);
            if (string.IsNullOrWhiteSpace(station) || !coords.TryGetValue(station, out var c))
                continue;

            var lines = new List<string> { "AI: " + tag.Label.Trim() };
            if (tag.Confidence is float conf and > 0f and <= 1f)
                lines.Add(conf.ToString("P0", inv));
            if (!string.IsNullOrWhiteSpace(tag.EntityType))
                lines.Add(tag.EntityType.Trim());
            lines.Add("@ " + station);
            labels.Add(new Viewport3DLabelEntry(
                new Point3D(c.X, c.Y, c.Z + 0.26),
                lines,
                Viewport3DLabelKind.AiTag));
        }
    }

    private static string? ResolveAiTagStation(
        SurveyAiClassificationTag tag,
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        var entityRef = (tag.EntityRef ?? "").Trim();
        if (entityRef.Length == 0)
            return null;

        var entityType = (tag.EntityType ?? "").Trim();
        if (entityType.Contains("station", StringComparison.OrdinalIgnoreCase) &&
            coords.ContainsKey(entityRef))
            return entityRef;

        foreach (var shot in project.Shots)
        {
            if (!string.IsNullOrWhiteSpace(shot.Id) &&
                string.Equals(shot.Id.Trim(), entityRef, StringComparison.OrdinalIgnoreCase))
            {
                var from = (shot.FromStation ?? "").Trim();
                if (from.Length > 0 && coords.ContainsKey(from))
                    return from;
            }
        }

        if (coords.ContainsKey(entityRef))
            return entityRef;

        return null;
    }

    private static string FormatStationSnapshotSensors(StationEnvironmentSnapshot snap, CultureInfo inv)
    {
        var parts = new List<string>();
        if (snap.ManualAmbientTempCelsius is float mt)
            parts.Add($"T {mt.ToString("0.#", inv)} °C");
        else if (snap.AmbientBleTempCelsius is float bt)
            parts.Add($"T {bt.ToString("0.#", inv)} °C");
        if (snap.ManualRelativeHumidityPct is float mh)
            parts.Add($"RH {mh.ToString("0.#", inv)} %");
        else if (snap.AmbientBleRelativeHumidityPct is float bh)
            parts.Add($"RH {bh.ToString("0.#", inv)} %");
        if (snap.Co2Ppm is float co2)
            parts.Add($"CO₂ {co2.ToString("0", inv)} ppm");
        if (snap.BarometricPressureHpa is float bp)
            parts.Add($"{bp.ToString("0", inv)} hPa");
        return parts.Count == 0 ? "" : string.Join(" · ", parts);
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

    private static bool TryReadFloat(JsonElement el, string name, out float value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        return p.TryGetSingle(out value);
    }

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

