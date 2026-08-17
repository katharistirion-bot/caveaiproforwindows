using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Open named Android cartography documents stored on <c>data.json</c>.</summary>
public static class NamedCartographyDocuments
{
    public readonly record struct Summary(string Id, string Name, bool IsOpen);

    public static IReadOnlyList<Summary> List(CaveProjectDocument? project)
    {
        if (project == null || !TryGetMaps(project, out var maps))
            return Array.Empty<Summary>();
        var openId = project.ActiveCartographyMapId?.Trim() ?? "";
        var list = new List<Summary>();
        foreach (var map in maps.EnumerateArray())
        {
            var id = ReadString(map, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var name = ReadString(map, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = "Plan";
            list.Add(new Summary(id, name, string.Equals(id, openId, StringComparison.Ordinal)));
        }
        return list;
    }

    public static bool TryOpen(CaveProjectDocument project, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !TryGetMaps(project, out var maps))
            return false;
        foreach (var map in maps.EnumerateArray())
        {
            if (!string.Equals(ReadString(map, "id"), id, StringComparison.Ordinal))
                continue;
            CopyArray(map, "sketches", value => project.Sketches = value);
            CopyArray(map, "sectionSketches", value => project.SectionSketches = value);
            CopyArray(map, "mapSymbols", value => project.MapSymbols = value);
            CopyArray(map, "vectorLines", value => project.VectorLines = value);
            CopyArray(map, "brackets", value => project.Brackets = value);
            CopyArray(map, "depthSpanAnnotations", value => project.DepthSpanAnnotations = value);
            project.ActiveCartographyMapId = id;
            return true;
        }
        return false;
    }

    private static bool TryGetMaps(CaveProjectDocument project, out JsonElement maps)
    {
        if (project.NamedCartographyMaps.ValueKind == JsonValueKind.Array)
        {
            maps = project.NamedCartographyMaps;
            return true;
        }
        if (project.ExtensionData != null &&
            project.ExtensionData.TryGetValue("namedCartographyMaps", out maps) &&
            maps.ValueKind == JsonValueKind.Array)
            return true;
        maps = default;
        return false;
    }

    private static string ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";

    private static void CopyArray(JsonElement map, string name, Action<JsonElement> assign)
    {
        if (map.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            assign(el.Clone());
    }
}
