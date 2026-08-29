using System.Globalization;
using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Mirrors Android <c>calculateCaveCoordinates</c> in <c>CaveSurveyHelpers.kt</c> (plan frame: dx/dy from compass/clino, z from entrance alt).
/// </summary>
public static class SurveyStationGeometry
{
    public sealed record StationPlanCoords(string Name, float X, float Y, float Z);

    /// <summary>Reduction from shots plus any in-memory <see cref="CaveProjectDocument.PlanStationPositionOverrides"/>.</summary>
    public static Dictionary<string, StationPlanCoords> CalculatePlanCoordinates(CaveProjectDocument project) =>
        CalculatePlanCoordinates(project.Shots, (float)project.Alt, project.PlanStationPositionOverrides);

    /// <param name="entranceAlt">Android uses <c>project.alt</c> (entrance altitude, metres).</param>
    public static Dictionary<string, StationPlanCoords> CalculatePlanCoordinates(
        IReadOnlyList<ShotRecord> shots,
        float entranceAlt) =>
        CalculatePlanCoordinates(shots, entranceAlt, null);

    /// <param name="planStationPositionOverrides">Optional manual XYZ per station (editor); applied after traverse reduction.</param>
    public static Dictionary<string, StationPlanCoords> CalculatePlanCoordinates(
        IReadOnlyList<ShotRecord> shots,
        float entranceAlt,
        IReadOnlyDictionary<string, PlanStationPositionOverride>? planStationPositionOverrides)
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
            else if (unresolved.Count > 0)
            {
                // Orphan shot — drop to avoid infinite loop on malformed geometry.
                unresolved.RemoveAt(0);
            }
        }

        ApplyPlanStationPositionOverrides(coords, planStationPositionOverrides);
        return coords;
    }

    private static void ApplyPlanStationPositionOverrides(
        Dictionary<string, StationPlanCoords> coords,
        IReadOnlyDictionary<string, PlanStationPositionOverride>? overrides)
    {
        if (overrides == null || overrides.Count == 0)
            return;
        foreach (var kv in overrides)
        {
            var n = kv.Key;
            var o = kv.Value;
            coords[n] = new StationPlanCoords(n, o.X, o.Y, o.Z);
        }
    }

    /// <summary>Plan viewMode in Android (0 = plan, 1 = section, 3 = long profile).</summary>
    public const int AndroidViewModePlan = 0;

    public const int AndroidViewModeSection = 1;

    /// <summary>Long profile / developed distance view (Android viewMode 3).</summary>
    public const int AndroidViewModeLongProfile = 3;

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
            if (IsWindowsAuthoredJson(el) || IsWindowsVectorLineType(el))
                continue;
            if (!PassesViewModeFilter(el, requiredViewMode))
                continue;
            var type = ResolveVectorLineTypeTag(el);
            if (!TryReadPolylinePointsFromEntry(el, out var pts) || pts.Count < 2)
                continue;
            var closed = el.TryGetProperty("closed", out var cl) && cl.ValueKind == JsonValueKind.True;
            var preferSharp = ResolvePreferSharpPolyline(el, type);
            var strokeArgb = ResolveStrokeColorArgb(el);
            list.Add(new PlanVectorPolyline(type, pts, closed, preferSharp, strokeArgb));
        }

        return list;
    }

    /// <summary>Parses <c>vectorLines</c> JSON for plan view (viewMode 0).</summary>
    public static IReadOnlyList<PlanVectorPolyline> ParsePlanVectorLines(JsonElement? vectorLinesRoot) =>
        ParseVectorLinesForViewMode(vectorLinesRoot, AndroidViewModePlan);

    /// <summary>Section strokes from <c>sectionSketches</c> intended for section view (excludes plan-only viewMode 0).</summary>
    public static IReadOnlyList<PlanVectorPolyline> ParseSectionSketchesForSectionView(CaveProjectDocument project)
    {
        var list = new List<PlanVectorPolyline>();
        if (!CaveProjectJsonBlobs.TryGetSectionSketches(project, out var root) || root.ValueKind != JsonValueKind.Array)
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
    /// Plan wall / passage outlines from Android <c>sketches</c>, <c>sketchLayer</c>, <c>MapObjects</c>, and extension
    /// aliases (same survey XY metres as vectorLines). Layer-specific arrays use vertex-accurate (non-smoothed) rendering.
    /// </summary>
    public static IReadOnlyList<PlanVectorPolyline> ParsePlanSketches(CaveProjectDocument project)
    {
        var list = new List<PlanVectorPolyline>();
        if (CaveProjectJsonBlobs.TryGetSketches(project, out var legacySketches))
            AppendSketchPolylinesFromJson(legacySketches, list, preferSharpPolyline: false, defaultTypeTag: "sketch",
                skipWindowsAuthored: true);

        foreach (var root in EnumerateSupplementalPlanSketchArrays(project))
            AppendSketchPolylinesFromJson(root, list, preferSharpPolyline: true, defaultTypeTag: "sketchLayer",
                skipWindowsAuthored: true);

        return list;
    }

    /// <summary>Extra Gson arrays beyond <see cref="CaveProjectJsonBlobs.TryGetSketches"/> (explicit fields + extension keys).</summary>
    private static IEnumerable<JsonElement> EnumerateSupplementalPlanSketchArrays(CaveProjectDocument project)
    {
        if (project.SketchLayer.ValueKind == JsonValueKind.Array)
            yield return project.SketchLayer;
        if (project.MapObjects.ValueKind == JsonValueKind.Array)
            yield return project.MapObjects;

        if (project.ExtensionData == null)
            yield break;

        foreach (var key in new[]
                 {
                     "sketchLayer", "SketchLayer", "mapObjects", "MapObjects", "planSketchLayer", "freehandSketches",
                     "mapSketchLayer",
                 })
        {
            if (!project.ExtensionData.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array)
                continue;
            yield return el;
        }
    }

    /// <summary>Section sketches drawn in plan context (only entries that declare plan view).</summary>
    public static IReadOnlyList<PlanVectorPolyline> ParsePlanSectionSketchesInPlan(CaveProjectDocument project)
    {
        var list = new List<PlanVectorPolyline>();
        if (!CaveProjectJsonBlobs.TryGetSectionSketches(project, out var root) || root.ValueKind != JsonValueKind.Array)
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

    /// <summary>
    /// Plan stamps from Android <c>mapSymbols</c>, <c>symbolsLayer</c>, <c>sketchObjects</c>, etc. (survey metres,
    /// <see cref="AndroidViewModePlan"/> or omitted <c>viewMode</c>).
    /// </summary>
    public static IReadOnlyList<PlanMapSymbol> ParsePlanMapSymbols(CaveProjectDocument project) =>
        ParseMapSymbolsForViewMode(project, AndroidViewModePlan);

    /// <summary>
    /// Section / extended-elevation stamps: same JSON arrays as plan, filtered to <see cref="AndroidViewModeSection"/>.
    /// </summary>
    public static IReadOnlyList<PlanMapSymbol> ParseSectionMapSymbols(CaveProjectDocument project) =>
        ParseMapSymbolsForViewMode(project, AndroidViewModeSection);

    private static readonly string[] SymbolLayerExtensionKeys =
    [
        "planMapSymbols",
        "planSymbols",
        "sketchSymbols",
        "mapStampSymbols",
        "symbolsLayer",
        "SymbolsLayer",
        "sketchObjects",
        "SketchObjects",
        "androidSymbols",
        "planSymbolLayer",
    ];

    private static IReadOnlyList<PlanMapSymbol> ParseMapSymbolsForViewMode(CaveProjectDocument project, int viewModeFilter)
    {
        var list = new List<PlanMapSymbol>();
        if (project.MapSymbols.ValueKind == JsonValueKind.Array)
            AppendPlanMapSymbolsFromArray(project.MapSymbols, list, viewModeFilter);
        if (project.ExtensionData != null)
        {
            foreach (var key in SymbolLayerExtensionKeys)
            {
                if (!project.ExtensionData.TryGetValue(key, out var root) || root.ValueKind != JsonValueKind.Array)
                    continue;
                AppendPlanMapSymbolsFromArray(root, list, viewModeFilter);
            }

            foreach (var key in new[] { "mapObjects", "MapObjects", "mapObjectLayer" })
            {
                if (!project.ExtensionData.TryGetValue(key, out var root) || root.ValueKind != JsonValueKind.Array)
                    continue;
                AppendPlanMapSymbolsFromMixedMapObjectsArray(root, list, viewModeFilter, skipWindowsAuthored: true);
            }
        }

        if (project.MapObjects.ValueKind == JsonValueKind.Array)
            AppendPlanMapSymbolsFromMixedMapObjectsArray(project.MapObjects, list, viewModeFilter, skipWindowsAuthored: true);

        return list;
    }

    /// <summary>Test / tool helper: parse symbol arrays from an extension dictionary only.</summary>
    public static IReadOnlyList<PlanMapSymbol> ParsePlanMapSymbols(Dictionary<string, JsonElement>? extensionData)
    {
        var list = new List<PlanMapSymbol>();
        if (extensionData == null)
            return list;
        foreach (var key in new[] { "mapSymbols", "planMapSymbols", "planSymbols", "sketchSymbols", "mapStampSymbols", "symbolsLayer", "sketchObjects" })
        {
            if (!extensionData.TryGetValue(key, out var root) || root.ValueKind != JsonValueKind.Array)
                continue;
            AppendPlanMapSymbolsFromArray(root, list, AndroidViewModePlan);
        }

        foreach (var key in new[] { "mapObjects", "MapObjects", "mapObjectLayer" })
        {
            if (!extensionData.TryGetValue(key, out var root) || root.ValueKind != JsonValueKind.Array)
                continue;
            AppendPlanMapSymbolsFromMixedMapObjectsArray(root, list, AndroidViewModePlan);
        }

        return list;
    }

    private static bool PassesViewModeFilter(JsonElement el, int requiredViewMode)
    {
        if (!el.TryGetProperty("viewMode", out var vmEl) || vmEl.ValueKind != JsonValueKind.Number ||
            !vmEl.TryGetInt32(out var vm))
        {
            // Android omits viewMode on plan-only payloads — treat as plan.
            return requiredViewMode == AndroidViewModePlan;
        }

        return vm == requiredViewMode;
    }

    private static void AppendPlanMapSymbolsFromArray(JsonElement root, List<PlanMapSymbol> list, int viewModeFilter)
    {
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            TryAppendSinglePlanMapSymbol(el, list, viewModeFilter);
        }
    }

    /// <summary>
    /// <c>mapObjects</c> combines strokes and stamps — ignore entries that are clearly free-hand polylines (2+ vertices).
    /// </summary>
    private static void AppendPlanMapSymbolsFromMixedMapObjectsArray(
        JsonElement root,
        List<PlanMapSymbol> list,
        int viewModeFilter,
        bool skipWindowsAuthored = false)
    {
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (skipWindowsAuthored && IsWindowsAuthoredJson(el))
                continue;
            if (LooksLikeMapObjectStrokePolyline(el))
                continue;
            TryAppendSinglePlanMapSymbol(el, list, viewModeFilter);
        }
    }

    private const string WindowsSourceClient = "CaveAiProForWindows";

    private static bool IsWindowsAuthoredJson(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return false;
        if (!el.TryGetProperty("sourceClient", out var sc) || sc.ValueKind != JsonValueKind.String)
            return false;
        return string.Equals(sc.GetString(), WindowsSourceClient, StringComparison.Ordinal);
    }

    private static bool IsWindowsVectorLineType(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return false;
        if (!el.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String)
            return false;
        return t.GetString()?.StartsWith("WINDOWS_", StringComparison.Ordinal) == true;
    }

    private static bool LooksLikeMapObjectStrokePolyline(JsonElement el)
    {
        JsonElement probe = el;
        if (el.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object)
            probe = g;
        return TryReadPointsFromObject(probe, out var pts) && pts.Count >= 2;
    }

    private static void TryAppendSinglePlanMapSymbol(JsonElement el, List<PlanMapSymbol> list, int viewModeFilter)
    {
        if (!PassesViewModeFilter(el, viewModeFilter))
            return;
        if (!TryReadSurveyXY(el, out var x, out var y))
            return;
        var z = TryReadOptionalZ(el);
        var scaleMult = TryReadOptionalPositiveFloat(el, 1f, "scale", "iconScale", "size", "stampScale");
        _ = TryReadOptionalFloat(el, 0f, out var rot, "rotation", "rotationDeg", "rotationDegrees", "heading", "bearing", "azimuthDeg");
        var surveySpanM = TryReadOptionalSurveySpanMetres(el);
        var (label, iconKey, symbolId) = ReadSymbolLabels(el);
        list.Add(new PlanMapSymbol(x, y, z, label, scaleMult, rot, iconKey, symbolId, surveySpanM));
    }

    /// <summary>Optional explicit symbol footprint in survey metres (Android <c>widthSurveyM</c>, <c>symbolSpanM</c>, …).</summary>
    private static float? TryReadOptionalSurveySpanMetres(JsonElement el)
    {
        if (!TryReadOptionalFloat(el, -1f, out var v, "widthSurveyM", "symbolWidthM", "spanSurveyM", "symbolSpanMetres",
                "sizeSurveyM", "scaleSurveyM", "scaleSurveyMetres", "stampWidthM", "footprintM"))
            return null;
        if (v <= 0 || float.IsNaN(v) || float.IsInfinity(v))
            return null;
        return v;
    }

    private static float TryReadOptionalZ(JsonElement el)
    {
        if (TryReadOptionalFloat(el, 0f, out var z, "z", "surveyZ", "elevation", "elev", "alt", "depthM", "depthMetres"))
            return z;
        return 0f;
    }

    private static bool TryReadOptionalFloat(JsonElement el, float fallback, out float value, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var p))
                continue;
            value = ReadFloat(p);
            return true;
        }

        value = fallback;
        return false;
    }

    private static float TryReadOptionalPositiveFloat(JsonElement el, float fallback, params string[] names)
    {
        if (!TryReadOptionalFloat(el, fallback, out var v, names))
            return fallback;
        if (v <= 0 || float.IsNaN(v) || float.IsInfinity(v))
            return fallback;
        return v;
    }

    private static (string? Label, string? IconKey, string? SymbolId) ReadSymbolLabels(JsonElement el)
    {
        string? PickString(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (!el.TryGetProperty(k, out var p) || p.ValueKind != JsonValueKind.String)
                    continue;
                var s = p.GetString()?.Trim();
                if (!string.IsNullOrEmpty(s))
                    return s;
            }

            return null;
        }

        var label = PickString("symbol", "type", "stamp", "kind", "name", "iconName", "symbolType", "iconKey");
        var icon = PickString("icon", "iconUri", "iconKey", "asset", "glyph");
        if (icon != null && (icon.Contains('/', StringComparison.Ordinal) || icon.Contains('\\', StringComparison.Ordinal)))
            icon = Path.GetFileNameWithoutExtension(icon.Replace('\\', '/'));
        var symbolId = PickString("symbolId", "symbolID", "SymbolID", "sketchSymbolId", "androidSymbolId", "glyphId");
        return (label, icon, symbolId);
    }

    private static void AppendSketchPolylinesFromJson(
        JsonElement root,
        List<PlanVectorPolyline> list,
        bool preferSharpPolyline,
        string defaultTypeTag,
        bool skipWindowsAuthored = false)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return;
        foreach (var el in root.EnumerateArray())
        {
            if (skipWindowsAuthored && IsWindowsAuthoredJson(el))
                continue;
            AppendSketchElement(el, list, preferSharpPolyline, defaultTypeTag);
        }
    }

    private static void AppendSketchElement(
        JsonElement el,
        List<PlanVectorPolyline> list,
        bool preferSharpPolyline,
        string defaultTypeTag)
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
                list.Add(new PlanVectorPolyline(defaultTypeTag, strokePts, Closed: false, preferSharpPolyline));
            return;
        }

        if (el.ValueKind != JsonValueKind.Object)
            return;
        if (el.TryGetProperty("viewMode", out var vmEl) && vmEl.ValueKind == JsonValueKind.Number &&
            vmEl.TryGetInt32(out var vm) && vm != AndroidViewModePlan)
            return;

        if (MapObjectKindLooksLikeSymbolOnly(el))
            return;

        foreach (var nestName in new[] { "strokes", "paths", "segments", "lineStrings" })
        {
            if (!el.TryGetProperty(nestName, out var nest) || nest.ValueKind != JsonValueKind.Array || nest.GetArrayLength() == 0)
                continue;
            foreach (var child in nest.EnumerateArray())
                AppendSketchElement(child, list, preferSharpPolyline, defaultTypeTag);
            return;
        }

        JsonElement obj = el;
        if (el.TryGetProperty("geometry", out var geo) && geo.ValueKind == JsonValueKind.Object)
            obj = geo;

        if (!TryReadPointsFromObject(obj, out var pts) || pts.Count < 2)
            return;
        var type = ResolveSketchTypeTag(el, defaultTypeTag);
        var closed = el.TryGetProperty("closed", out var cl) && cl.ValueKind == JsonValueKind.True;
        var strokeArgb = ResolveStrokeColorArgb(el);
        list.Add(new PlanVectorPolyline(type, pts, closed, preferSharpPolyline, strokeArgb));
    }

    /// <summary>Placed symbols inside <c>mapObjects</c> — skip so they are not duplicated as wall strokes.</summary>
    private static bool MapObjectKindLooksLikeSymbolOnly(JsonElement el)
    {
        if (!TryReadSketchKind(el, out var kind))
            return false;
        if (kind.Contains("symbol", StringComparison.OrdinalIgnoreCase) ||
            kind.Contains("stamp", StringComparison.OrdinalIgnoreCase) ||
            kind.Contains("marker", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kind, "pin", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool TryReadSketchKind(JsonElement el, out string kind)
    {
        kind = "";
        foreach (var prop in new[] { "kind", "objectType", "objectKind", "mapObjectType", "category" })
        {
            if (!el.TryGetProperty(prop, out var p) || p.ValueKind != JsonValueKind.String)
                continue;
            kind = p.GetString()?.Trim() ?? "";
            if (kind.Length > 0)
                return true;
        }

        return false;
    }

    private static string ResolveSketchTypeTag(JsonElement el, string fallback)
    {
        if (el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
        {
            var s = t.GetString()?.Trim();
            if (!string.IsNullOrEmpty(s))
                return s;
        }

        if (TryReadSketchKind(el, out var kind) && kind.Length > 0 &&
            !kind.Contains("symbol", StringComparison.OrdinalIgnoreCase))
            return kind;

        return fallback;
    }

    private static void AppendSketchPolylinesFromJson(JsonElement root, List<PlanVectorPolyline> list) =>
        AppendSketchPolylinesFromJson(root, list, preferSharpPolyline: false, defaultTypeTag: "sketch");

    /// <summary>Reads Android <c>strokeColorArgb</c> / <c>strokeColor</c> when exported on vector strokes.</summary>
    internal static int? ResolveStrokeColorArgb(JsonElement el)
    {
        foreach (var name in new[] { "strokeColorArgb", "colorArgb", "strokeArgb" })
        {
            if (!el.TryGetProperty(name, out var n) || n.ValueKind != JsonValueKind.Number)
                continue;
            return unchecked((int)n.GetInt64());
        }

        foreach (var name in new[] { "strokeColor", "color", "strokeColorHex" })
        {
            if (!el.TryGetProperty(name, out var s) || s.ValueKind != JsonValueKind.String)
                continue;
            return ParseHexColorArgb(s.GetString());
        }

        return null;
    }

    internal static int? ParseHexColorArgb(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        var s = hex.Trim();
        if (s.StartsWith('#'))
            s = s[1..];
        if (s.Length == 6 &&
            int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return unchecked((int)(0xFF000000 | (uint)rgb));
        }

        if (s.Length == 8 &&
            int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return argb;
        }

        return null;
    }

    private static string ResolveVectorLineTypeTag(JsonElement el)
    {
        foreach (var name in new[] { "type", "stroke", "strokeType", "kind", "objectType", "category", "mapObjectType" })
        {
            if (!el.TryGetProperty(name, out var t) || t.ValueKind != JsonValueKind.String)
                continue;
            var s = t.GetString()?.Trim();
            if (!string.IsNullOrEmpty(s))
                return s;
        }

        return "vec";
    }

    private static bool ResolvePreferSharpPolyline(JsonElement el, string typeTag)
    {
        if (el.TryGetProperty("preferSharpPolyline", out var ps) && ps.ValueKind == JsonValueKind.True)
            return true;
        if (el.TryGetProperty("sharp", out var sh) && sh.ValueKind == JsonValueKind.True)
            return true;

        var lower = typeTag.Replace('_', ' ').ToLowerInvariant();
        return lower.Contains("stroke", StringComparison.Ordinal) ||
               lower.Contains("pen", StringComparison.Ordinal) ||
               lower.Contains("walloutline", StringComparison.Ordinal) ||
               lower.Contains("sketchlayer", StringComparison.Ordinal) ||
               lower is "wall" or "sectionsketch";
    }

    /// <summary>Reads vertex chains from a vectorLines / sketch object (unwraps nested <c>geometry</c>).</summary>
    private static bool TryReadPolylinePointsFromEntry(JsonElement el, out List<(float x, float y)> pts)
    {
        pts = new List<(float x, float y)>();
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in el.EnumerateArray())
            {
                if (TryReadPoint(p, out var x, out var y))
                    pts.Add((x, y));
            }

            return pts.Count >= 2;
        }

        if (el.ValueKind != JsonValueKind.Object)
            return false;

        JsonElement probe = el;
        if (el.TryGetProperty("geometry", out var geo) && geo.ValueKind == JsonValueKind.Object)
            probe = geo;

        return TryReadPointsFromObject(probe, out pts);
    }

    private static bool TryReadPointsFromObject(JsonElement obj, out List<(float x, float y)> pts)
    {
        pts = new List<(float x, float y)>();
        foreach (var name in new[]
                 {
                     "points", "path", "strokePoints", "vertices", "polyline", "coordinates", "coords", "trail",
                     "trace",
                 })
        {
            if (!obj.TryGetProperty(name, out var arr))
                continue;

            if (name == "coordinates" && arr.ValueKind == JsonValueKind.Array)
            {
                if (TryAppendCoordinatesArray(arr, pts) && pts.Count >= 2)
                    return true;
                pts.Clear();
                continue;
            }

            if (arr.ValueKind != JsonValueKind.Array)
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

    /// <summary>GeoJSON-style <c>coordinates</c>: nested arrays of [x,y] pairs.</summary>
    private static bool TryAppendCoordinatesArray(JsonElement coordsRoot, List<(float x, float y)> pts)
    {
        var startCount = pts.Count;
        void Walk(JsonElement n)
        {
            switch (n.ValueKind)
            {
                case JsonValueKind.Array when n.GetArrayLength() >= 2 &&
                                              n[0].ValueKind is JsonValueKind.Number or JsonValueKind.String &&
                                              n[1].ValueKind is JsonValueKind.Number or JsonValueKind.String:
                    if (TryReadPoint(n, out var x, out var y))
                        pts.Add((x, y));
                    return;
                case JsonValueKind.Array:
                    foreach (var inner in n.EnumerateArray())
                        Walk(inner);
                    break;
            }
        }

        Walk(coordsRoot);
        return pts.Count - startCount >= 2;
    }

    private static bool TryReadSurveyXY(JsonElement obj, out float x, out float y)
    {
        x = y = 0;
        if (obj.TryGetProperty("surveyX", out var sxEl) && obj.TryGetProperty("surveyY", out var syEl))
        {
            x = ReadFloat(sxEl);
            y = ReadFloat(syEl);
            return true;
        }

        if (obj.TryGetProperty("east", out var east) && obj.TryGetProperty("north", out var north))
        {
            x = ReadFloat(east);
            y = ReadFloat(north);
            return true;
        }

        if (obj.TryGetProperty("easting", out var e) && obj.TryGetProperty("northing", out var n))
        {
            x = ReadFloat(e);
            y = ReadFloat(n);
            return true;
        }

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

    public sealed record PlanVectorPolyline(
        string Type,
        IReadOnlyList<(float x, float y)> Points,
        bool Closed = false,
        /// <summary>Vertex-accurate path (no Catmull–Rom smoothing) — recommended for <c>sketchLayer</c> / <c>mapObjects</c> pen strokes.</summary>
        bool PreferSharpPolyline = false,
        /// <summary>Android stroke colour packed as 0xAARRGGBB when present.</summary>
        int? StrokeColorArgb = null);

    /// <summary>User-authored sketch ink persisted in <c>mapObjects</c> / <c>sketchLayer</c> (not LRUD-derived).</summary>
    public static bool IsPersistedSketchWallType(string type) =>
        type is "sketch" or "sketchLayer" or "section";

    /// <summary>Passage outline derived from traverse LRUD (safe to offer as procedural assist seed).</summary>
    public static bool IsLrudDerivedWallType(string type) =>
        type is "lrudPlanRibbon" or "lrudPlan" or "lrudProfile" or "lrud3dEdge" or "lrud3dFace";

    /// <summary>
    /// Android plan / section map symbol / stamp (survey metres). <see cref="Scale"/> is a dimensionless multiplier from
    /// the handset; <see cref="ScaleSurveyMetres"/> optional explicit width in metres for desktop fidelity.
    /// </summary>
    public sealed record PlanMapSymbol(
        float X,
        float Y,
        float Z,
        string? Label,
        float Scale,
        float RotationDegrees,
        string? IconKey,
        string? SymbolId = null,
        float? ScaleSurveyMetres = null);
}
