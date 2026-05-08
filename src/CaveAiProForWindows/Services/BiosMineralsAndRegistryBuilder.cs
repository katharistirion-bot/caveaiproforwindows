using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Builds cave registry and bio/mineral catalog rows from Gson-compatible project JSON.</summary>
public static class BiosMineralsAndRegistryBuilder
{
    private static readonly string[] TitlePropertyNames =
    {
        "name", "title", "species", "scientificName", "mineralName", "sampleName", "commonName",
        "taxon", "label", "description", "notes", "comment", "type", "category", "sampleType",
    };

    public static IReadOnlyList<CaveRegistryRow> BuildCaveRegistry(IEnumerable<CaveProjectDocument> projects) =>
        projects.Select(BuildCaveRow).ToList();

    public static IReadOnlyList<BioMineralCatalogRow> BuildBioMineralCatalog(IEnumerable<CaveProjectDocument> projects)
    {
        var rows = new List<BioMineralCatalogRow>();
        foreach (var p in projects)
        {
            var cave = string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name;
            AppendObjectArrayRows(rows, cave, "rocks", p.Rocks);
            AppendObjectArrayRows(rows, cave, "fieldCatalog", p.FieldCatalogEntries);
        }

        return rows;
    }

    private static CaveRegistryRow BuildCaveRow(CaveProjectDocument p)
    {
        var name = string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name;
        var trav = p.Shots.Count(s => s.IsTraverseLeg);
        var lib = !string.IsNullOrWhiteSpace(p.LinkedLibraryCaveId)
            ? p.LinkedLibraryCaveId.Trim()
            : TryExtensionString(p.ExtensionData, "linkedLibraryCaveId");
        var cover = RegistryCoverResolver.TryGetCoverUri(p);
        var rocks = p.Rocks is { ValueKind: JsonValueKind.Array } r ? r.GetArrayLength() : 0;
        var cat = p.FieldCatalogEntries is { ValueKind: JsonValueKind.Array } f ? f.GetArrayLength() : 0;
        return new CaveRegistryRow(
            name,
            p.Date ?? "",
            p.Lat?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            p.Lon?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            p.Alt,
            trav,
            p.Shots.Count,
            rocks,
            cat,
            lib,
            cover,
            p.LoadedFromFile);
    }

    private static void AppendObjectArrayRows(List<BioMineralCatalogRow> rows, string caveName, string source, JsonElement? root)
    {
        if (root is not { ValueKind: JsonValueKind.Array } arr)
            return;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            var title = PickTitle(el);
            var cat = GuessCategory(el);
            var species = PickFirstString(el, "scientificName", "species", "taxon", "commonName", "organismName");
            var mineral = PickFirstString(el, "mineralName", "rockName", "rockType", "geologyType", "oreName", "mineral");
            var photo = PickFirstString(el, "photoUri", "imageUri", "uri", "attachmentUri", "fileUri", "photoUrl", "imageUrl");
            var coords = PickCoordinatesSummary(el);
            var details = SummarizeObject(el, 520);
            rows.Add(new BioMineralCatalogRow(caveName, source, cat, title, species, mineral, photo, coords, details));
        }
    }

    private static string PickFirstString(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!obj.TryGetProperty(key, out var v))
                continue;
            var s = JsonPrimitiveToString(v);
            if (!string.IsNullOrWhiteSpace(s))
                return CollapseWhitespace(s);
        }

        return "";
    }

    private static string PickCoordinatesSummary(JsonElement obj)
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

            if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), CultureInfo.InvariantCulture, out d))
                return true;
            return false;
        }

        if (TryD(obj, "x", out var x) && TryD(obj, "y", out var y))
        {
            if (TryD(obj, "z", out var z))
                return $"x={x:0.##} y={y:0.##} z={z:0.##}";
            return $"x={x:0.##} y={y:0.##}";
        }

        if (TryD(obj, "northing", out var n) && TryD(obj, "easting", out var e))
            return $"N={n:0.##} E={e:0.##}";
        if (TryD(obj, "latitude", out var la) && TryD(obj, "longitude", out var lo))
            return $"lat={la:0.######} lon={lo:0.######}";
        return "";
    }

    private static string PickTitle(JsonElement obj)
    {
        foreach (var key in TitlePropertyNames)
        {
            if (!obj.TryGetProperty(key, out var v))
                continue;
            var s = JsonPrimitiveToString(v);
            if (!string.IsNullOrWhiteSpace(s))
                return CollapseWhitespace(s);
        }

        return "—";
    }

    private static string GuessCategory(JsonElement obj)
    {
        var blob = new StringBuilder();
        foreach (var prop in obj.EnumerateObject())
        {
            blob.Append(prop.Name).Append(' ');
            AppendValueText(blob, prop.Value, depth: 0, maxDepth: 2, maxChars: 800);
            blob.Append(' ');
        }

        var t = blob.ToString().ToLowerInvariant();
        var mineralScore = CountKeywordHits(t, MineralHints);
        var bioScore = CountKeywordHits(t, BioHints);
        if (mineralScore > bioScore && mineralScore > 0)
            return "Mineral / geological";
        if (bioScore > mineralScore && bioScore > 0)
            return "Biotic";
        if (mineralScore > 0 && bioScore > 0)
            return "Mixed / unclear";
        return "Other / uncategorized";
    }

    private static int CountKeywordHits(string text, string[] keys)
    {
        var n = 0;
        foreach (var k in keys)
        {
            if (text.Contains(k, StringComparison.Ordinal))
                n++;
        }

        return n;
    }

    private static readonly string[] MineralHints =
    {
        "mineral", "crystal", "geol", "geology", "ore", "stalact", "stalagm", "calcite", "aragonite",
        "gypsum", "limestone", "dolomite", "quartz", "speleothem", "rock type", "litho", "strat",
        "ορυκ", "γεωλ", "σταλακ", "σταλαγ", "ασβεστ", "χαλαζ", "ανυδ", "σπηλαι", "πέτρ",
    };

    private static readonly string[] BioHints =
    {
        "species", "fauna", "flora", "fungi", "animal", "plant", "insect", "bat", "arthrop",
        "crustace", "arachn", "mollusc", "worm", "nematod", "bacter", "bio", "taxon", "dna",
        "tissue", "organism", "troglob", "troglof", "χλωρο", "φυτ", "ζωο", "έμβι", "βιοτ",
        "ιχθ", "ακρίδ", "αλισ", "μύκη", "κοράλλ", "φωκ", "νυχτερίδ",
    };

    private static string SummarizeObject(JsonElement obj, int maxLen)
    {
        var sb = new StringBuilder();
        foreach (var prop in obj.EnumerateObject())
        {
            if (sb.Length >= maxLen)
                break;
            sb.Append(prop.Name).Append('=');
            AppendValueText(sb, prop.Value, depth: 0, maxDepth: 1, maxChars: maxLen - sb.Length);
            sb.Append("; ");
        }

        var s = CollapseWhitespace(sb.ToString().TrimEnd());
        if (s.Length <= maxLen)
            return s;
        return s.Substring(0, maxLen) + "…";
    }

    private static void AppendValueText(StringBuilder sb, JsonElement v, int depth, int maxDepth, int maxChars)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.String:
                sb.Append(Truncate(CollapseWhitespace(v.GetString() ?? ""), maxChars));
                break;
            case JsonValueKind.Number:
                sb.Append(v.GetRawText());
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            case JsonValueKind.Null:
                sb.Append("null");
                break;
            case JsonValueKind.Array:
                sb.Append('[').Append(v.GetArrayLength()).Append(" items]");
                break;
            case JsonValueKind.Object when depth < maxDepth:
                sb.Append('{');
                var first = true;
                foreach (var p in v.EnumerateObject())
                {
                    if (sb.Length >= maxChars)
                        break;
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    sb.Append(p.Name).Append('=');
                    AppendValueText(sb, p.Value, depth + 1, maxDepth, maxChars - sb.Length);
                }

                sb.Append('}');
                break;
            default:
                sb.Append("{…}");
                break;
        }
    }

    private static string? JsonPrimitiveToString(JsonElement v) =>
        v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private static string CollapseWhitespace(string s) => WhitespaceRun.Replace(s, " ").Trim();

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return s.Substring(0, Math.Max(0, max - 1)) + "…";
    }

    private static string? TryExtensionString(Dictionary<string, JsonElement>? ext, string key)
    {
        if (ext == null || !ext.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
