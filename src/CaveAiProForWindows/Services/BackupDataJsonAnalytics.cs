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
                }

                sb.AppendLine(
                    $"Shots: {n} rows · {legs} traverse leg(s) · {withPhotos} shot(s) with photo URI(s) ({photoRefs} total photo ref(s)) · {withAudio} shot(s) with audioMemoUri");
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
