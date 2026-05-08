using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Reads Gson JSON blobs from explicit <see cref="CaveProjectDocument"/> fields first, then <see cref="CaveProjectDocument.ExtensionData"/>.</summary>
public static class CaveProjectJsonBlobs
{
    public static bool TryGetSketches(CaveProjectDocument p, out JsonElement el) =>
        TryBlob(p.Sketches, p.ExtensionData, "sketches", out el);

    public static bool TryGetSectionSketches(CaveProjectDocument p, out JsonElement el) =>
        TryBlob(p.SectionSketches, p.ExtensionData, "sectionSketches", out el);

    public static bool TryGetMapSymbols(CaveProjectDocument p, out JsonElement el) =>
        TryBlob(p.MapSymbols, p.ExtensionData, "mapSymbols", out el);

    private static bool TryBlob(JsonElement primary, Dictionary<string, JsonElement>? ext, string fallbackKey, out JsonElement el)
    {
        if (primary.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
        {
            el = primary;
            return true;
        }

        if (ext != null && ext.TryGetValue(fallbackKey, out el) &&
            el.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            return true;

        el = default;
        return false;
    }
}
