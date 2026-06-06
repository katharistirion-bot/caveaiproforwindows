using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Builds <see cref="GeoBioRecord"/> entries from <see cref="CaveProjectDocument.Rocks"/>,
/// <see cref="CaveProjectDocument.FieldCatalogEntries"/>, and the schema-v3 <c>geoBioRecords</c> array (when
/// Android exports it). Each record carries category, AI text, and image refs ready for the GEO &amp; BIO view.
/// </summary>
public static class GeoBioRecordsService
{
    private static readonly string[] TitleKeys =
    [
        "name", "title", "species", "scientificName", "commonName", "taxon",
        "mineralName", "rockName", "rockType", "geologyType", "sampleName",
        "label", "description", "type", "category", "sampleType",
    ];

    private static readonly string[] CategoryKeys = ["category", "kind", "type", "geoBioCategory", "recordCategory"];

    private static readonly string[] AnalysisKeys =
    [
        "caveAiAnalysisText", "caveAiAnalysis", "caveAiText", "aiAnalysisText", "aiAnalysis",
        AndroidExportLegacyJsonKeys.AnalysisText,
        AndroidExportLegacyJsonKeys.AnalysisTextShort,
        AndroidExportLegacyJsonKeys.AnalysisTextAlt,
        "analysisText", "analysis", "geologyAnalysisText", "geologyAnalysis",
        "biologyAnalysisText", "biologyAnalysis", "cloudAnalysisText", "cloudAnalysis",
        "extendedAnalysis", "detailedDescription", "scientificInfo",
        "summary", "report", "text",
    ];

    private static readonly string[] ImageRefKeys =
    [
        "imageReferences", "images", "photos", "photoUris", "imageUris", "attachments", "photoReferences",
    ];

    private static readonly string[] SingleImageKeys =
    [
        "imageUri", "photoUri", "photoUrl", "imageUrl", "uri", "fileUri", "attachmentUri", "thumbnailUri",
        "photoReference",
    ];

    private static readonly string[] StationKeys =
    [
        "station", "stationName", "atStation", "linkedStation", "fromStation", "anchorStation",
    ];

    /// <summary>Parses every known scientific-record container into a unified, deduplicated list.</summary>
    public static IReadOnlyList<GeoBioRecord> Build(CaveProjectDocument project)
    {
        var list = new List<GeoBioRecord>();
        AppendArray(list, project.Rocks, "rocks", isRockSourceFallback: true);
        AppendFieldCatalogArray(list, project.FieldCatalogEntries);
        if (project.ExtensionData != null)
        {
            foreach (var key in new[] { "geoBioRecords", "GeoBioRecords", "scientificRecords", "scienceRecords" })
            {
                if (project.ExtensionData.TryGetValue(key, out var el))
                    AppendArray(list, el, key, isRockSourceFallback: false);
            }
        }

        return list;
    }

    internal static GeoBioCategory InferCategoryPublic(JsonElement el) => InferCategory(el);

    internal static IReadOnlyList<string> ReadImageReferencesPublic(JsonElement el) => ReadImageReferences(el);

    internal static string PickCoordinatesSummaryPublic(JsonElement el) =>
        BiosMineralsAndRegistryBuilder_PickCoordinatesSummary(el);

    private static void AppendFieldCatalogArray(List<GeoBioRecord> list, JsonElement? rootNullable)
    {
        if (rootNullable is not { ValueKind: JsonValueKind.Array } root)
            return;
        var i = 0;
        foreach (var el in root.EnumerateArray())
        {
            var parsed = FieldCatalogEntryParser.TryParse(el, $"fieldCatalogEntries[{i}]");
            if (parsed != null)
                list.Add(parsed);
            i++;
        }
    }

    private static void AppendArray(
        List<GeoBioRecord> list,
        JsonElement? rootNullable,
        string sourceName,
        bool isRockSourceFallback)
    {
        if (rootNullable is not { ValueKind: JsonValueKind.Array } root)
            return;

        var i = 0;
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                i++;
                continue;
            }

            var explicitCategory = ReadCategory(el);
            var category = explicitCategory ??
                           (isRockSourceFallback
                               ? GeoBioCategory.Rock
                               : InferCategory(el));

            var title = PickFirstString(el, TitleKeys);
            var analysis = PickFirstString(el, AnalysisKeys);
            var station = PickFirstString(el, StationKeys);
            var coords = BiosMineralsAndRegistryBuilder_PickCoordinatesSummary(el);
            var imgs = ReadImageReferences(el);
            var summary = SummarizeJsonObject(el);

            list.Add(new GeoBioRecord(
                category,
                title.Length == 0 ? "(unnamed)" : title,
                $"{sourceName}[{i}]",
                string.IsNullOrWhiteSpace(station) ? null : station,
                string.IsNullOrWhiteSpace(analysis) ? null : analysis,
                imgs,
                summary,
                string.IsNullOrWhiteSpace(coords) ? null : coords));
            i++;
        }
    }

    private static GeoBioCategory? ReadCategory(JsonElement el)
    {
        if (el.TryGetProperty("kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.String)
        {
            var fk = FieldCatalogEntryKindMapper.Parse(kindEl.GetString());
            if (fk != FieldCatalogEntryKind.Unknown)
                return FieldCatalogEntryKindMapper.ToGeoBioCategory(fk);
        }

        foreach (var key in CategoryKeys)
        {
            if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
                continue;
            var s = (v.GetString() ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0)
                continue;
            if (s.Contains("rock", StringComparison.Ordinal) || s.Contains("mineral", StringComparison.Ordinal) ||
                s.Contains("geo", StringComparison.Ordinal) || s.Contains("speleo", StringComparison.Ordinal) ||
                s.Contains("petro", StringComparison.Ordinal) || s.Contains("litho", StringComparison.Ordinal) ||
                s.Contains("strati", StringComparison.Ordinal))
                return GeoBioCategory.Rock;
            if (s.Contains("organism", StringComparison.Ordinal) || s.Contains("bio", StringComparison.Ordinal) ||
                s.Contains("flora", StringComparison.Ordinal) || s.Contains("fauna", StringComparison.Ordinal) ||
                s.Contains("species", StringComparison.Ordinal) || s.Contains("plant", StringComparison.Ordinal) ||
                s.Contains("animal", StringComparison.Ordinal) || s.Contains("insect", StringComparison.Ordinal) ||
                s.Contains("bat", StringComparison.Ordinal) || s.Contains("crusta", StringComparison.Ordinal) ||
                s.Contains("fungi", StringComparison.Ordinal) || s.Contains("mycel", StringComparison.Ordinal) ||
                s.Contains("bacter", StringComparison.Ordinal) || s.Contains("biota", StringComparison.Ordinal))
                return GeoBioCategory.Organism;
            if (s == "mixed" || s == "other" || s == "unknown")
                return GeoBioCategory.Other;
        }

        return null;
    }

    private static GeoBioCategory InferCategory(JsonElement el)
    {
        var blob = new StringBuilder();
        foreach (var prop in el.EnumerateObject())
        {
            blob.Append(prop.Name).Append(' ');
            if (prop.Value.ValueKind == JsonValueKind.String)
                blob.Append(prop.Value.GetString()).Append(' ');
        }

        var t = blob.ToString().ToLowerInvariant();
        var rockHits = CountKeywordHits(t, RockKeywords);
        var bioHits = CountKeywordHits(t, BioKeywords);
        if (rockHits > bioHits && rockHits > 0)
            return GeoBioCategory.Rock;
        if (bioHits > rockHits && bioHits > 0)
            return GeoBioCategory.Organism;
        if (rockHits > 0 && bioHits > 0)
            return GeoBioCategory.Mixed;
        return GeoBioCategory.Other;
    }

    private static readonly string[] RockKeywords =
    [
        "mineral", "crystal", "geol", "ore", "stalact", "stalagm", "calcite", "aragonite", "gypsum", "limestone",
        "dolomite", "quartz", "speleothem", "rocktype", "litho", "strat", "ορυκ", "γεωλ", "σταλακ", "σταλαγ",
        "ασβεστ", "χαλαζ", "πέτρ",
    ];

    private static readonly string[] BioKeywords =
    [
        "species", "fauna", "flora", "fungi", "animal", "plant", "insect", "bat", "arthrop", "crustace",
        "arachn", "mollusc", "worm", "nematod", "bacter", "biota", "taxon", "dna", "tissue", "organism",
        "troglob", "troglof", "χλωρο", "φυτ", "ζωο", "έμβι", "βιοτ", "ιχθ", "νυχτερίδ",
    ];

    private static int CountKeywordHits(string text, string[] keys)
    {
        var n = 0;
        foreach (var k in keys)
            if (text.Contains(k, StringComparison.Ordinal))
                n++;
        return n;
    }

    private static IReadOnlyList<string> ReadImageReferences(JsonElement el)
    {
        var refs = new List<string>();
        foreach (var k in ImageRefKeys)
        {
            if (!el.TryGetProperty(k, out var v))
                continue;
            switch (v.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in v.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s &&
                            !string.IsNullOrWhiteSpace(s))
                            refs.Add(s.Trim());
                        else if (item.ValueKind == JsonValueKind.Object)
                        {
                            var inner = PickFirstString(item, SingleImageKeys);
                            if (inner.Length > 0)
                                refs.Add(inner);
                        }
                    }

                    break;
                case JsonValueKind.String when !string.IsNullOrWhiteSpace(v.GetString()):
                    refs.Add(v.GetString()!.Trim());
                    break;
            }
        }

        var single = PickFirstString(el, SingleImageKeys);
        if (single.Length > 0)
            refs.Add(single);

        // Deduplicate while preserving order.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<string>();
        foreach (var r in refs)
        {
            if (seen.Add(r))
                deduped.Add(r);
        }

        return deduped;
    }

    private static string PickFirstString(JsonElement obj, string[] keys)
    {
        foreach (var k in keys)
        {
            if (!obj.TryGetProperty(k, out var v))
                continue;
            switch (v.ValueKind)
            {
                case JsonValueKind.String when !string.IsNullOrWhiteSpace(v.GetString()):
                    return v.GetString()!.Trim();
                case JsonValueKind.Number:
                    return v.GetRawText();
            }
        }

        return "";
    }

    private static string SummarizeJsonObject(JsonElement el)
    {
        const int maxLen = 360;
        var sb = new StringBuilder();
        foreach (var prop in el.EnumerateObject())
        {
            if (sb.Length >= maxLen)
                break;
            if (prop.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                continue;
            var raw = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString()
                : prop.Value.GetRawText();
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            sb.Append(prop.Name).Append('=').Append(raw!.Trim()).Append("; ");
        }

        var s = sb.ToString().TrimEnd();
        return s.Length > maxLen ? s[..maxLen] + "…" : s;
    }

    /// <summary>Reuses the same coordinate-summary heuristic <see cref="BiosMineralsAndRegistryBuilder"/> uses, scoped here as a static helper.</summary>
    private static string BiosMineralsAndRegistryBuilder_PickCoordinatesSummary(JsonElement obj)
    {
        static bool TryD(JsonElement o, string k, out double d)
        {
            d = 0;
            if (!o.TryGetProperty(k, out var e))
                return false;
            if (e.ValueKind == JsonValueKind.Number)
            {
                d = e.GetDouble();
                return true;
            }

            if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
                return true;
            return false;
        }

        if (TryD(obj, "x", out var x) && TryD(obj, "y", out var y))
        {
            if (TryD(obj, "z", out var z))
                return $"x={x:0.##} y={y:0.##} z={z:0.##}";
            return $"x={x:0.##} y={y:0.##}";
        }

        if (TryD(obj, "latitude", out var la) && TryD(obj, "longitude", out var lo))
            return $"lat={la:0.######} lon={lo:0.######}";
        return "";
    }
}
