using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Collects plan-map pins from <c>fieldCatalogEntries</c> and <c>rocks</c> JSON arrays.</summary>
public static class FieldCatalogMapPinCollector
{
    public static IReadOnlyList<FieldCatalogMapPin> Collect(CaveProjectDocument project)
    {
        var list = new List<FieldCatalogMapPin>();
        if (project.FieldCatalogEntries is { ValueKind: JsonValueKind.Array } cat)
        {
            var i = 0;
            foreach (var el in cat.EnumerateArray())
            {
                TryAddFromElement(list, el, $"fieldCatalog[{i}]");
                i++;
            }
        }

        if (project.Rocks is { ValueKind: JsonValueKind.Array } rocks)
        {
            var i = 0;
            foreach (var el in rocks.EnumerateArray())
            {
                TryAddFromElement(list, el, $"rocks[{i}]", defaultCategory: "Rock sample");
                i++;
            }
        }

        return list;
    }

    private static void TryAddFromElement(
        List<FieldCatalogMapPin> list,
        JsonElement el,
        string source,
        string? defaultCategory = null)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return;
        if (!TryReadFloat(el, "planMapX", out var x) || !TryReadFloat(el, "planMapY", out var y))
            return;

        var parsed = FieldCatalogEntryParser.TryParse(el, source);
        var title = parsed?.Title ?? ReadString(el, "name", "title", "label") ?? "Catalog pin";
        var cat = parsed?.FieldKind is { } fk
            ? FieldCatalogEntryKindMapper.DisplayLabel(fk)
            : defaultCategory ?? "Field catalog";
        var sci = parsed?.ScientificName ?? ReadString(el, "scientificName", "species");
        list.Add(new FieldCatalogMapPin(x, y, title, cat, string.IsNullOrWhiteSpace(sci) ? null : sci.Trim()));
    }

    private static bool TryReadFloat(JsonElement el, string name, out float value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        if (p.TryGetSingle(out value))
            return true;
        if (p.TryGetDouble(out var d))
        {
            value = (float)d;
            return true;
        }

        return false;
    }

    private static string? ReadString(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString()?.Trim();
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }

        return null;
    }
}
