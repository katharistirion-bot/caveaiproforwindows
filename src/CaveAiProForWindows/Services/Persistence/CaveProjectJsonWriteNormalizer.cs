using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.Persistence;

/// <summary>Ensures <see cref="CaveProjectDocument"/> JSON fields can be re-serialized to disk.</summary>
internal static class CaveProjectJsonWriteNormalizer
{
    private static readonly JsonElement EmptyArray = ParseClone("[]");
    private static readonly JsonElement NullValue = ParseClone("null");

    public static void Prepare(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);

        NamedCartographyDocuments.SyncLiveIntoActive(project);

        project.Sketches = CoalesceArray(project.Sketches);
        project.SketchLayer = CoalesceArray(project.SketchLayer);
        project.MapObjects = CoalesceArray(project.MapObjects);
        project.SectionSketches = CoalesceArray(project.SectionSketches);
        project.MapSymbols = CoalesceArray(project.MapSymbols);
        project.TrackPoints = CoalesceArray(project.TrackPoints);
        project.SurveyEventLog = CoalesceArray(project.SurveyEventLog);
        project.VehicleParkCoords = CoalesceObjectOrNull(project.VehicleParkCoords);
        project.SurfaceLidarRaster = CoalesceObjectOrNull(project.SurfaceLidarRaster);
        project.PublicLibraryCartographyUris = CoalesceObjectOrNull(project.PublicLibraryCartographyUris);
        project.DepthSpanAnnotations = CoalesceArray(project.DepthSpanAnnotations);
        project.Brackets = CoalesceArray(project.Brackets);
        project.CaveAiGeologyAnalysisJson = CoalesceObjectOrNull(project.CaveAiGeologyAnalysisJson);
        project.XrayBackdropImageBounds = CoalesceObjectOrNull(project.XrayBackdropImageBounds);

        if (project.VectorLines is { } vl)
            project.VectorLines = CoalesceArray(vl);
        if (project.Rocks is { } rocks)
            project.Rocks = CoalesceArray(rocks);
        if (project.FieldCatalogEntries is { } fce)
            project.FieldCatalogEntries = CoalesceArray(fce);

        project.NamedCartographyMaps = CoalesceArray(project.NamedCartographyMaps);

        if (project.ExtensionData == null)
            return;

        var next = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in project.ExtensionData)
        {
            if (string.Equals(key, "planStationPositionOverrides", StringComparison.OrdinalIgnoreCase))
                continue;
            next[key] = value.ValueKind == JsonValueKind.Undefined ? NullValue.Clone() : value.Clone();
        }
        project.ExtensionData = next;
    }

    public static void PrepareAll(IEnumerable<CaveProjectDocument> projects)
    {
        foreach (var p in projects)
            Prepare(p);
    }

    internal static JsonElement CloneElement(JsonElement source)
    {
        if (source.ValueKind == JsonValueKind.Undefined)
            return NullValue.Clone();
        return source.Clone();
    }

    internal static JsonElement CloneJson<T>(T value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private static JsonElement CoalesceArray(JsonElement el) =>
        el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? EmptyArray.Clone() : el.Clone();

    private static JsonElement CoalesceObjectOrNull(JsonElement el) =>
        el.ValueKind == JsonValueKind.Undefined ? NullValue.Clone() : el.Clone();

    private static JsonElement ParseClone(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
