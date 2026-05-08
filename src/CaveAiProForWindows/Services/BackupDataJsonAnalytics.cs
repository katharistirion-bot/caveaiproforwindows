using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CaveAiProForWindows.Services;

/// <summary>Human-readable analytics from raw CaveAI <c>data.json</c> (full array scan — photos/audio per shot, rocks, catalog, top-level keys).</summary>
public static class BackupDataJsonAnalytics
{
    public static string BuildReport(string? sourceLabel, string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return "(empty JSON)";
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            return BuildReport(sourceLabel, doc.RootElement);
        }
        catch (Exception ex)
        {
            return "Could not parse JSON: " + ex.Message;
        }
    }

    public static string BuildReport(string? sourceLabel, JsonElement root)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(sourceLabel))
        {
            sb.AppendLine($"Source: {sourceLabel}");
            sb.AppendLine(new string('=', 48));
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            sb.AppendLine("Root is not a JSON array — expected [ { CaveProject }, … ].");
            return sb.ToString().TrimEnd();
        }

        var pi = 0;
        foreach (var p in root.EnumerateArray())
        {
            pi++;
            if (p.ValueKind != JsonValueKind.Object)
            {
                sb.AppendLine($"Project #{pi}: (not an object)");
                continue;
            }

            var name = TryString(p, "name");
            var date = TryString(p, "date");
            sb.AppendLine($"=== Project #{pi}: {name}  ({date}) ===");

            if (p.TryGetProperty("shots", out var shots) && shots.ValueKind == JsonValueKind.Array)
            {
                var n = shots.GetArrayLength();
                var legs = 0;
                var withPhotos = 0;
                var photoRefs = 0;
                var withAudio = 0;
                var withNotes = 0;
                var withComment = 0;
                var withTime = 0;
                var withTs = 0;
                var unionShotKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var s in shots.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object)
                        continue;
                    if (IsTraverseLeg(s))
                        legs++;
                    var pc = CountPhotos(s);
                    if (pc > 0)
                    {
                        withPhotos++;
                        photoRefs += pc;
                    }

                    if (HasNonEmptyString(s, "audioMemoUri"))
                        withAudio++;
                    if (HasNonEmptyString(s, "notes"))
                        withNotes++;
                    if (HasNonEmptyString(s, "comment"))
                        withComment++;
                    if (HasNonEmptyString(s, "time"))
                        withTime++;
                    if (s.TryGetProperty("timestampUtcMs", out var tsEl) && tsEl.ValueKind == JsonValueKind.Number)
                        withTs++;
                    foreach (var prop in s.EnumerateObject())
                        unionShotKeys.Add(prop.Name);
                }

                sb.AppendLine(
                    $"Shots: {n} rows · {legs} traverse leg(s) · {withPhotos} shot(s) with photo URI(s) ({photoRefs} total photo ref(s)) · {withAudio} shot(s) with audioMemoUri");
                sb.AppendLine(
                    $"  Text / time fields: {withNotes} non-empty notes · {withComment} non-empty comment · {withTime} non-empty time · {withTs} numeric timestampUtcMs");
                sb.AppendLine(
                    $"  Union of per-shot JSON keys ({unionShotKeys.Count}): {string.Join(", ", unionShotKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}");
            }
            else
            {
                sb.AppendLine("Shots: (missing or not an array)");
            }

            var rocks = ArrayLen(p, "rocks");
            var cat = ArrayLen(p, "fieldCatalogEntries");
            sb.AppendLine($"Rocks (Geo/Bio samples): {rocks}  ·  Field catalog entries: {cat}");

            var vl = ArrayLen(p, "vectorLines");
            sb.AppendLine($"vectorLines segments: {vl}");

            var nMapSymArr = ArrayLen(p, "mapSymbols");
            var nSketchArr = ArrayLen(p, "sketches");
            var nSketchLayerArr = ArrayLen(p, "sketchLayer");
            var nMapObjectsArr = ArrayLen(p, "mapObjects");
            var nSecSketchArr = ArrayLen(p, "sectionSketches");
            var nTrackArr = ArrayLen(p, "trackPoints");
            var nDepthSpan = ArrayLen(p, "depthSpanAnnotations");
            var nBrackets = ArrayLen(p, "brackets");
            sb.AppendLine(
                $"mapSymbols: {nMapSymArr} · sketches: {nSketchArr} · sketchLayer: {nSketchLayerArr} · mapObjects: {nMapObjectsArr} · sectionSketches: {nSecSketchArr} · trackPoints: {nTrackArr} · depthSpanAnnotations: {nDepthSpan} · brackets: {nBrackets}");

            if (HasNonEmptyString(p, "startTime") || HasNonEmptyString(p, "endTime"))
            {
                var st = TryString(p, "startTime");
                var en = TryString(p, "endTime");
                sb.AppendLine($"Survey window (strings): startTime={st} · endTime={en}");
            }

            if (p.TryGetProperty("publicLibraryCartographyUris", out var lib) && lib.ValueKind == JsonValueKind.Array)
                sb.AppendLine($"publicLibraryCartographyUris: {lib.GetArrayLength()} entr(y/ies)");

            if (p.TryGetProperty("surfaceLidarRaster", out var slr) && slr.ValueKind == JsonValueKind.Object &&
                slr.TryGetProperty("imageUri", out var iu) && iu.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(iu.GetString()))
                sb.AppendLine("surfaceLidarRaster.imageUri: set");

            if (HasNonEmptyString(p, "cartographyTlsMeshObjUri"))
                sb.AppendLine("cartographyTlsMeshObjUri: set");

            if (p.TryGetProperty("surveyEventLog", out var log) && log.ValueKind == JsonValueKind.Array)
                sb.AppendLine($"surveyEventLog lines: {log.GetArrayLength()}");

            if (p.TryGetProperty("linkedLibraryCaveId", out var lid) && lid.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(lid.GetString()))
                sb.AppendLine($"linkedLibraryCaveId: {lid.GetString()}");

            if (p.TryGetProperty("surveyArchiveSchemaVersion", out var ver) && ver.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(ver.GetString()))
                sb.AppendLine($"surveyArchiveSchemaVersion: {ver.GetString()}");

            if (p.TryGetProperty("exportDeviceContext", out var dev) && dev.ValueKind == JsonValueKind.Object)
                sb.AppendLine("exportDeviceContext: object present");
            if (p.TryGetProperty("surveyCalibrationProfile", out var cal) && cal.ValueKind == JsonValueKind.Object)
                sb.AppendLine("surveyCalibrationProfile: object present");
            if (p.TryGetProperty("surveyAiClassifications", out var ai) && ai.ValueKind == JsonValueKind.Array)
                sb.AppendLine($"surveyAiClassifications: {ai.GetArrayLength()} row(s)");
            if (p.TryGetProperty("stationEnvironmentSnapshots", out var ses) && ses.ValueKind == JsonValueKind.Array)
                sb.AppendLine($"stationEnvironmentSnapshots: {ses.GetArrayLength()} row(s)");

            var sketchStyleHints = 0;
            foreach (var key in new[] { "sketches", "sectionSketches" })
            {
                if (!p.TryGetProperty(key, out var sk) || sk.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var el in sk.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object)
                        continue;
                    if (el.TryGetProperty("strokeWidthPx", out _) || el.TryGetProperty("strokeColorArgb", out _) ||
                        el.TryGetProperty("layerIndex", out _) || el.TryGetProperty("textAnnotations", out _))
                    {
                        sketchStyleHints++;
                    }
                }
            }

            if (sketchStyleHints > 0)
                sb.AppendLine($"Sketch style metadata (stroke/layer/text hints): {sketchStyleHints} element(s) across sketches/sectionSketches");

            var topKeys = p.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            sb.AppendLine($"Top-level JSON keys ({topKeys.Count}): {string.Join(", ", topKeys)}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsTraverseLeg(JsonElement shot)
    {
        if (!shot.TryGetProperty("toStation", out var t) || t.ValueKind != JsonValueKind.String)
            return false;
        var v = t.GetString();
        return !string.IsNullOrEmpty(v) && !string.Equals(v, "-", StringComparison.Ordinal);
    }

    private static int CountPhotos(JsonElement shot)
    {
        if (!shot.TryGetProperty("photos", out var ph) || ph.ValueKind != JsonValueKind.Array)
            return 0;
        var n = 0;
        foreach (var x in ph.EnumerateArray())
        {
            if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                n++;
        }

        return n;
    }

    private static bool HasNonEmptyString(JsonElement obj, string prop)
    {
        return obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(el.GetString());
    }

    private static int ArrayLen(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Array)
            return 0;
        return el.GetArrayLength();
    }

    private static string TryString(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.String)
            return "";
        return el.GetString() ?? "";
    }
}
