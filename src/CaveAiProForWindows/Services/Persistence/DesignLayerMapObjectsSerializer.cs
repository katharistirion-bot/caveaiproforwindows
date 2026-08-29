using System.Text.Json;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SketchAssist;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.Persistence;

/// <summary>
/// Serializes the Sketch Editor <see cref="Canvas"/> design layer into Android-compatible <c>mapObjects</c> JSON
/// (mixed stroke polylines + symbol stamps).
/// </summary>
public static class DesignLayerMapObjectsSerializer
{
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false,
    };

    /// <summary>Builds a JSON array suitable for <see cref="CaveProjectDocument.MapObjects"/>.</summary>
    public static JsonElement SerializeDesignLayer(
        Canvas? designLayer,
        PlanCanvasSurveyLayout layout,
        int viewMode = SurveyStationGeometry.AndroidViewModePlan)
    {
        var (mapObjects, _) = BuildMapObjectsAndSketches(designLayer, layout, viewMode);
        return mapObjects;
    }

    private static (JsonElement MapObjects, JsonElement Sketches) BuildMapObjectsAndSketches(
        Canvas? designLayer,
        PlanCanvasSurveyLayout layout,
        int viewMode)
    {
        var entries = new List<object>();
        var sketchEntries = new List<object>();

        if (designLayer != null)
        {
            var (strokes, stamps) = DesignLayerSurveyConverter.ExtractUserGeometry(designLayer, layout);

            foreach (var stroke in strokes)
            {
                var strokeObj = new Dictionary<string, object?>
                {
                    ["kind"] = "stroke",
                    ["viewMode"] = viewMode,
                    ["closed"] = stroke.Closed,
                    ["points"] = stroke.Points
                        .Select(p => new[] { (object)p.X, p.Y })
                        .ToArray(),
                    ["sourceClient"] = ResolveSourceClient(stroke.Source),
                };
                ApplyStrokeMetadata(strokeObj, stroke.Metadata, stroke.Source);
                entries.Add(strokeObj);
                sketchEntries.Add(ToAndroidSketchEntry(strokeObj));
            }

            foreach (var stamp in stamps)
            {
                var entry = new Dictionary<string, object?>
                {
                    ["surveyX"] = stamp.SurveyX,
                    ["surveyY"] = stamp.SurveyY,
                    ["symbolId"] = SketchEditorSymbolKindExporter.ToSymbolId(stamp.Kind),
                    ["viewMode"] = viewMode,
                    ["scale"] = 1f,
                    ["sourceClient"] = WindowsSourceClient,
                };
                entries.Add(entry);
            }
        }

        return (
            JsonSerializer.SerializeToElement(entries, CompactJson),
            JsonSerializer.SerializeToElement(sketchEntries, CompactJson));
    }

    private const string WindowsSourceClient = "CaveAiProForWindows";

    private static Dictionary<string, object?> ToAndroidSketchEntry(Dictionary<string, object?> strokeMapObject)
    {
        var sketch = new Dictionary<string, object?>
        {
            ["viewMode"] = strokeMapObject["viewMode"],
            ["closed"] = strokeMapObject["closed"],
            ["points"] = strokeMapObject["points"],
            ["sourceClient"] = strokeMapObject["sourceClient"],
        };
        foreach (var key in new[] { "strokeWidthPx", "strokeColorArgb", "strokeColor", "brushProfile", "layerIndex", "layerZOrder", "layerName" })
        {
            if (strokeMapObject.TryGetValue(key, out var v) && v != null)
                sketch[key] = v;
        }

        return sketch;
    }

    private static string ResolveSourceClient(string source) =>
        string.Equals(source, SketchStrokeStyleDefaults.ProceduralSource, StringComparison.OrdinalIgnoreCase)
            ? SketchStrokeStyleDefaults.ProceduralSource
            : WindowsSourceClient;

    private static void ApplyStrokeMetadata(Dictionary<string, object?> strokeObj, DesignLayerInkMetadata? metadata, string source)
    {
        var meta = metadata ?? (string.Equals(source, SketchStrokeStyleDefaults.ProceduralSource, StringComparison.OrdinalIgnoreCase)
            ? DesignLayerInkMetadata.ForProceduralWall()
            : DesignLayerInkMetadata.ForUserStroke(SketchStrokeStyleDefaults.DefaultStrokeWidthPx));
        SketchStrokeStyleMerger.MergeInto(strokeObj, meta);
    }

    /// <summary>Writes serialized design-layer geometry onto <paramref name="project"/>.</summary>
    public static bool TryApplyDesignLayerToProject(
        CaveProjectDocument project,
        Canvas? designLayer,
        PlanCanvasSurveyLayout layout,
        int viewMode = SurveyStationGeometry.AndroidViewModePlan)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (layout.Scale <= 0 || double.IsNaN(layout.Scale))
            return false;

        var (mapObjects, sketchStrokes) = BuildMapObjectsAndSketches(designLayer, layout, viewMode);

        // When a named cartography map is open, stamps go only to MapSymbols (Android parity).
        // Keep MapObjects as strokes-only so ParsePlanMapSymbols does not double-count stamps.
        var activeMapId = project.ActiveCartographyMapId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(activeMapId))
        {
            project.MapObjects = CaveProjectJsonWriteNormalizer.CloneElement(
                StripStampEntriesFromMapObjects(mapObjects));
            var mapSymbols = BuildMapSymbolsFromStamps(designLayer, layout, viewMode);
            project.MapSymbols = CaveProjectJsonWriteNormalizer.CloneElement(mapSymbols);
        }
        else
        {
            project.MapObjects = CaveProjectJsonWriteNormalizer.CloneElement(mapObjects);
        }

        project.Sketches = CaveProjectJsonWriteNormalizer.CloneElement(
            MergeWindowsSketches(project.Sketches, sketchStrokes));

        EnsureSurveyArchiveSchemaVersion(project);
        TouchWindowsEditMetadata(project);
        return true;
    }

    private static JsonElement StripStampEntriesFromMapObjects(JsonElement mapObjects)
    {
        if (mapObjects.ValueKind != JsonValueKind.Array)
            return mapObjects;
        var strokesOnly = new List<JsonElement>();
        foreach (var el in mapObjects.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (el.TryGetProperty("symbolId", out _) || el.TryGetProperty("icon", out _))
            {
                // Stamp row — omit when MapSymbols carries the same stamps.
                if (!el.TryGetProperty("kind", out var kind) ||
                    kind.ValueKind != JsonValueKind.String ||
                    !string.Equals(kind.GetString(), "stroke", StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            strokesOnly.Add(el.Clone());
        }
        return JsonSerializer.SerializeToElement(strokesOnly, CompactJson);
    }

    private static JsonElement BuildMapSymbolsFromStamps(
        Canvas? designLayer,
        PlanCanvasSurveyLayout layout,
        int viewMode)
    {
        if (designLayer == null)
            return JsonSerializer.SerializeToElement(new List<object>(), CompactJson);

        var (_, stamps) = DesignLayerSurveyConverter.ExtractUserGeometry(designLayer, layout);

        var list = new List<object>();
        foreach (var stamp in stamps)
        {
            // Android MapSymbol: { icon: string, x: float, y: float, viewMode: int }
            // We store the Windows symbolId slug as the Android `icon` key.
            list.Add(new Dictionary<string, object?>
            {
                ["icon"] = SketchEditorSymbolKindExporter.ToSymbolId(stamp.Kind),
                ["x"] = stamp.SurveyX,
                ["y"] = stamp.SurveyY,
                ["viewMode"] = viewMode,
            });
        }

        return JsonSerializer.SerializeToElement(list, CompactJson);
    }

    private static void EnsureSurveyArchiveSchemaVersion(CaveProjectDocument project)
    {
        if (!string.IsNullOrWhiteSpace(project.SurveyArchiveSchemaVersion))
            return;
        project.SurveyArchiveSchemaVersion = "2";
    }

    /// <summary>
    /// Keeps Android-authored sketch strokes; replaces prior Windows-authored strokes with the new set.
    /// </summary>
    public static JsonElement MergeWindowsSketches(JsonElement existingSketches, JsonElement windowsStrokes)
    {
        var merged = new List<JsonElement>();

        if (existingSketches.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in existingSketches.EnumerateArray())
            {
                if (IsWindowsAuthoredEntry(el))
                    continue;
                merged.Add(el.Clone());
            }
        }

        if (windowsStrokes.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in windowsStrokes.EnumerateArray())
                merged.Add(el.Clone());
        }

        return JsonSerializer.SerializeToElement(merged, CompactJson);
    }

    private static bool IsWindowsAuthoredEntry(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return false;
        if (!el.TryGetProperty("sourceClient", out var sc) || sc.ValueKind != JsonValueKind.String)
            return false;
        return string.Equals(sc.GetString(), WindowsSourceClient, StringComparison.Ordinal);
    }

    internal static void TouchWindowsEditMetadata(CaveProjectDocument project)
    {
        project.ExtensionData ??= new Dictionary<string, JsonElement>();
        project.ExtensionData["windowsLastEditedAtMs"] =
            CaveProjectJsonWriteNormalizer.CloneJson(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        project.ExtensionData["windowsEditorVersion"] =
            CaveProjectJsonWriteNormalizer.CloneJson(AppMetadata.InformationalVersion);
    }
}
