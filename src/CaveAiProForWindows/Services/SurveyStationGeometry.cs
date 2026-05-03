using System.Globalization;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Mirrors Android <c>calculateCaveCoordinates</c> in <c>CaveSurveyHelpers.kt</c> (plan frame: dx/dy from compass/clino, z from entrance alt).
/// </summary>
public static class SurveyStationGeometry
{
    public sealed record StationPlanCoords(string Name, float X, float Y, float Z);

    /// <param name="entranceAlt">Android uses <c>project.alt</c> (entrance altitude, metres).</param>
    public static Dictionary<string, StationPlanCoords> CalculatePlanCoordinates(
        IReadOnlyList<ShotRecord> shots,
        float entranceAlt)
    {
        var coords = new Dictionary<string, StationPlanCoords>(StringComparer.Ordinal);
        var stationComponent = new Dictionary<string, int>(StringComparer.Ordinal);
        var nextComponentId = 0;
        if (shots.Count == 0)
            return coords;

        var unresolved = shots.Where(s => s.IsTraverseLeg).ToList();
        var componentIndex = 0;
        const float componentSpacing = 60f;

        void MergePlanComponents(int cidAnchor, int cidMoved, StationPlanCoords expectedTo, StationPlanCoords movedTo)
        {
            var ox = expectedTo.X - movedTo.X;
            var oy = expectedTo.Y - movedTo.Y;
            var oz = expectedTo.Z - movedTo.Z;
            var names = coords.Where(kv => stationComponent.GetValueOrDefault(kv.Key) == cidMoved).Select(kv => kv.Key).ToList();
            foreach (var name in names)
            {
                if (!coords.TryGetValue(name, out var c))
                    continue;
                coords[name] = c with { X = c.X + ox, Y = c.Y + oy, Z = c.Z + oz };
                stationComponent[name] = cidAnchor;
            }
        }

        while (unresolved.Count > 0)
        {
            var seed = unresolved[0];
            if (!coords.ContainsKey(seed.FromStation))
            {
                var cid = nextComponentId++;
                coords[seed.FromStation] = new StationPlanCoords(seed.FromStation, componentIndex * componentSpacing, 0f, entranceAlt);
                stationComponent[seed.FromStation] = cid;
                componentIndex++;
            }

            var progressed = true;
            while (progressed)
            {
                progressed = false;
                for (var i = unresolved.Count - 1; i >= 0; i--)
                {
                    var shot = unresolved[i];
                    coords.TryGetValue(shot.FromStation, out var fromC);
                    coords.TryGetValue(shot.ToStation, out var toC);
                    var azRad = shot.Azimuth * (Math.PI / 180.0);
                    var clRad = shot.Clino * (Math.PI / 180.0);
                    var hDist = shot.Distance * (float)Math.Cos(clRad);
                    var dx = hDist * (float)Math.Sin(azRad);
                    var dy = hDist * (float)Math.Cos(azRad);
                    var dz = shot.Distance * (float)Math.Sin(clRad);

                    switch (fromC, toC)
                    {
                        case ({} from, null):
                        {
                            if (!stationComponent.TryGetValue(shot.FromStation, out var cid))
                                continue;
                            coords[shot.ToStation] = new StationPlanCoords(
                                shot.ToStation,
                                from.X + dx,
                                from.Y + dy,
                                from.Z + dz);
                            stationComponent[shot.ToStation] = cid;
                            unresolved.RemoveAt(i);
                            progressed = true;
                            break;
                        }
                        case (null, {} to):
                        {
                            if (!stationComponent.TryGetValue(shot.ToStation, out var cid))
                                continue;
                            coords[shot.FromStation] = new StationPlanCoords(
                                shot.FromStation,
                                to.X - dx,
                                to.Y - dy,
                                to.Z - dz);
                            stationComponent[shot.FromStation] = cid;
                            unresolved.RemoveAt(i);
                            progressed = true;
                            break;
                        }
                        case ({} from, {} to):
                        {
                            if (!stationComponent.TryGetValue(shot.FromStation, out var cidFrom) ||
                                !stationComponent.TryGetValue(shot.ToStation, out var cidTo))
                                continue;
                            if (cidFrom != cidTo)
                            {
                                var expectedTo = new StationPlanCoords(shot.ToStation, from.X + dx, from.Y + dy, from.Z + dz);
                                MergePlanComponents(cidFrom, cidTo, expectedTo, to);
                            }

                            unresolved.RemoveAt(i);
                            progressed = true;
                            break;
                        }
                    }
                }
            }

            if (unresolved.Count > 0 && !coords.ContainsKey(unresolved[0].FromStation))
            {
                var cid = nextComponentId++;
                var first = unresolved[0].FromStation;
                coords[first] = new StationPlanCoords(first, componentIndex * componentSpacing, 0f, entranceAlt);
                stationComponent[first] = cid;
                componentIndex++;
            }
        }

        return coords;
    }

    /// <summary>Plan viewMode in Android (0 = plan, 1 = section, 3 = long profile).</summary>
    public const int AndroidViewModePlan = 0;

    public const int AndroidViewModeSection = 1;

    /// <summary>Parses <c>vectorLines</c> for a given Android <c>viewMode</c>.</summary>
    public static IReadOnlyList<PlanVectorPolyline> ParseVectorLinesForViewMode(JsonElement? vectorLinesRoot, int requiredViewMode)
    {
        var list = new List<PlanVectorPolyline>();
        if (vectorLinesRoot is not { ValueKind: System.Text.Json.JsonValueKind.Array } arr)
            return list;

        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != System.Text.Json.JsonValueKind.Object)
                continue;
            if (el.TryGetProperty("viewMode", out var vmEl) && vmEl.ValueKind == JsonValueKind.Number &&
                vmEl.TryGetInt32(out var vm) && vm != requiredViewMode)
                continue;
            var type = el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "vec"
                : "vec";
            if (!TryReadPointsFromObject(el, out var pts) || pts.Count < 2)
                continue;
            var closed = el.TryGetProperty("closed", out var cl) && cl.ValueKind == JsonValueKind.True;
            list.Add(new PlanVectorPolyline(type, pts, closed));
        }

        return list;
    }

    /// <summary>Parses <c>vectorLines</c> JSON for plan view (viewMode 0).</summary>
    public static IReadOnlyList<PlanVectorPolyline> ParsePlanVectorLines(JsonElement? vectorLinesRoot) =>
        ParseVectorLinesForViewMode(vectorLinesRoot, AndroidViewModePlan);

    /// <summary>Section strokes from <c>sectionSketches</c> intended for section view (excludes plan-only viewMode 0).</summary>
    public static IReadOnlyList<PlanVectorPolyline> ParseSectionSketchesForSectionView(Dictionary<string, JsonElement>? extensionData)
    {
        var list = new List<PlanVectorPolyline>();
        if (extensionData == null || !extensionData.TryGetValue("sectionSketches", out var root))
            return list;
        if (root.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (el.TryGetProperty("viewMode", out var vmEl) && vmEl.ValueKind == JsonValueKind.Number &&
                vmEl.TryGetInt32(out var vm) && vm == AndroidViewModePlan)
                continue;
            if (!TryReadPointsFromObject(el, out var pts) || pts.Count < 2)
                continue;
            var closed = el.TryGetProperty("closed", out var cl) && cl.ValueKind == JsonValueKind.True;
            list.Add(new PlanVectorPolyline("sectionSketch", pts, closed));
        }

        return list;
    }

    /// <summary>
    /// Plan wall / passage outlines from Android <c>sketches</c> (same survey XY metres as vectorLines).
    /// Accepts flexible property names and optional <c>geometry</c> nesting.
    /// </summary>
    public static IReadOnlyList<PlanVectorPolyline> ParsePlanSketches(Dictionary<string, JsonElement>? extensionData)
    {
        var list = new List<PlanVectorPolyline>();
        if (extensionData == null || !extensionData.TryGetValue("sketches", out var root))
            return list;
        AppendSketchPolylinesFromJson(root, list);
        return list;
    }

    /// <summary>Section sketches drawn in plan context (only entries that declare plan view).</summary>
    public static IReadOnlyList<PlanVectorPolyline> ParsePlanSectionSketchesInPlan(Dictionary<string, JsonElement>? extensionData)
    {
        var list = new List<PlanVectorPolyline>();
        if (extensionData == null || !extensionData.TryGetValue("sectionSketches", out var root))
            return list;
        if (root.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (el.TryGetProperty("viewMode", out var vmEl) && vmEl.ValueKind == JsonValueKind.Number &&
                vmEl.TryGetInt32(out var vm) && vm != AndroidViewModePlan)
                continue;
            if (!TryReadPointsFromObject(el, out var pts) || pts.Count < 2)
                continue;
            var closed = el.TryGetProperty("closed", out var cl) && cl.ValueKind == JsonValueKind.True;
            list.Add(new PlanVectorPolyline("section", pts, closed));
        }

        return list;
    }

    /// <summary>Point markers from <c>mapSymbols</c> when JSON exposes survey X/Y.</summary>
    public static IReadOnlyList<PlanMapSymbol> ParsePlanMapSymbols(Dictionary<string, JsonElement>? extensionData)
    {
        var list = new List<PlanMapSymbol>();
        if (extensionData == null || !extensionData.TryGetValue("mapSymbols", out var root) || root.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (el.TryGetProperty("viewMode", out var vmEl) && vmEl.ValueKind == JsonValueKind.Number &&
                vmEl.TryGetInt32(out var vm) && vm != AndroidViewModePlan)
                continue;
            if (!TryReadSurveyXY(el, out var x, out var y))
                continue;
            var label = el.TryGetProperty("symbol", out var sym) && sym.ValueKind == JsonValueKind.String
                ? sym.GetString()
                : el.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String
                    ? ty.GetString()
                    : null;
            list.Add(new PlanMapSymbol(x, y, label));
        }

        return list;
    }

    private static void AppendSketchPolylinesFromJson(JsonElement root, List<PlanVectorPolyline> list)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return;
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                var strokePts = new List<(float x, float y)>();
                foreach (var p in el.EnumerateArray())
                {
                    if (TryReadPoint(p, out var x, out var y))
                        strokePts.Add((x, y));
                }

                if (strokePts.Count >= 2)
                    list.Add(new PlanVectorPolyline("sketch", strokePts, false));
                continue;
            }

            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (el.TryGetProperty("viewMode", out var vmEl) && vmEl.ValueKind == JsonValueKind.Number &&
                vmEl.TryGetInt32(out var vm) && vm != AndroidViewModePlan)
                continue;
            JsonElement obj = el;
            if (el.TryGetProperty("geometry", out var geo) && geo.ValueKind == JsonValueKind.Object)
                obj = geo;
            if (!TryReadPointsFromObject(obj, out var pts) || pts.Count < 2)
                continue;
            var type = el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "sketch"
                : "sketch";
            var closed = el.TryGetProperty("closed", out var cl) && cl.ValueKind == JsonValueKind.True;
            list.Add(new PlanVectorPolyline(type, pts, closed));
        }
    }

    private static bool TryReadPointsFromObject(JsonElement obj, out List<(float x, float y)> pts)
    {
        pts = new List<(float x, float y)>();
        foreach (var name in new[] { "points", "path", "strokePoints", "vertices", "polyline" })
        {
            if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var p in arr.EnumerateArray())
            {
                if (TryReadPoint(p, out var x, out var y))
                    pts.Add((x, y));
            }

            if (pts.Count >= 2)
                return true;
            pts.Clear();
        }

        return false;
    }

    private static bool TryReadSurveyXY(JsonElement obj, out float x, out float y)
    {
        x = y = 0;
        if (obj.TryGetProperty("x", out var xEl) && obj.TryGetProperty("y", out var yEl))
        {
            x = ReadFloat(xEl);
            y = ReadFloat(yEl);
            return true;
        }

        if (obj.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Object)
            return TryReadSurveyXY(pos, out x, out y);
        return false;
    }

    private static bool TryReadPoint(System.Text.Json.JsonElement p, out float x, out float y)
    {
        x = 0;
        y = 0;
        switch (p.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Array when p.GetArrayLength() >= 2:
                x = ReadFloat(p[0]);
                y = ReadFloat(p[1]);
                return true;
            case System.Text.Json.JsonValueKind.Object:
                if (p.TryGetProperty("first", out var f) && p.TryGetProperty("second", out var s))
                {
                    x = ReadFloat(f);
                    y = ReadFloat(s);
                    return true;
                }

                if (TryReadSurveyXY(p, out x, out y))
                    return true;
                break;
        }

        return false;
    }

    private static float ReadFloat(System.Text.Json.JsonElement e) =>
        e.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => (float)e.GetDouble(),
            System.Text.Json.JsonValueKind.String => float.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f,
            _ => 0f,
        };

    public sealed record PlanVectorPolyline(string Type, IReadOnlyList<(float x, float y)> Points, bool Closed = false);

    public sealed record PlanMapSymbol(float X, float Y, string? Label);
}
