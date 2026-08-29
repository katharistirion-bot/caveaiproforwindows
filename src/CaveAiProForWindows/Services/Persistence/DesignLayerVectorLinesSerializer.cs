using System.Globalization;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.Persistence;

/// <summary>
/// Mirrors Windows sketch ink into Android <c>vectorLines[]</c> for ZIP round-trip (alongside <c>mapObjects</c>).
/// </summary>
public static class DesignLayerVectorLinesSerializer
{
    private const string WindowsSourceClient = "CaveAiProForWindows";
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    /// <summary>Rebuilds Windows-authored <c>vectorLines</c> from persisted mapObjects / sketches.</summary>
    public static void SyncWindowsInkToVectorLines(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var windowsLines = new List<JsonElement>();
        // Prefer mapObjects (canonical DesignLayer persist). Sketches often duplicate the same strokes.
        if (project.MapObjects.ValueKind == JsonValueKind.Array && project.MapObjects.GetArrayLength() > 0)
            CollectStrokeVectorLines(project.MapObjects, windowsLines);
        else
            CollectStrokeVectorLines(project.Sketches, windowsLines);
        project.VectorLines = MergeWindowsVectorLines(project.VectorLines, JsonSerializer.SerializeToElement(windowsLines, CompactJson));
    }

    /// <summary>Keeps Android vector lines; replaces prior Windows lines with the new set.</summary>
    public static JsonElement MergeWindowsVectorLines(JsonElement? existingVectorLines, JsonElement windowsLines)
    {
        var merged = new List<JsonElement>();

        if (existingVectorLines is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (IsWindowsAuthoredVectorLine(el))
                    continue;
                merged.Add(el.Clone());
            }
        }

        if (windowsLines.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in windowsLines.EnumerateArray())
                merged.Add(el.Clone());
        }

        return JsonSerializer.SerializeToElement(merged, CompactJson);
    }

    private static void CollectStrokeVectorLines(JsonElement source, List<JsonElement> target)
    {
        if (source.ValueKind != JsonValueKind.Array)
            return;

        foreach (var el in source.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (IsMapObjectSymbol(el))
                continue;
            if (!TryBuildVectorLine(el, out var line))
                continue;
            target.Add(line);
        }
    }

    private static bool IsMapObjectSymbol(JsonElement el)
    {
        if (el.TryGetProperty("symbolId", out _))
            return true;
        if (el.TryGetProperty("kind", out var kind) &&
            kind.ValueKind == JsonValueKind.String &&
            string.Equals(kind.GetString(), "symbol", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool TryBuildVectorLine(JsonElement strokeObj, out JsonElement line)
    {
        line = default;
        if (!TryReadPoints(strokeObj, out var points) || points.Count < 2)
            return false;

        var viewMode = ReadInt(strokeObj, "viewMode", SurveyStationGeometry.AndroidViewModePlan);
        var type = ResolveVectorType(strokeObj);
        var payload = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["viewMode"] = viewMode,
            ["points"] = points.Select(static p => new object[] { p.x, p.y }).ToArray(),
            ["sourceClient"] = WindowsSourceClient,
        };
        if (strokeObj.TryGetProperty("closed", out var closed) &&
            closed.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            payload["closed"] = closed.GetBoolean();
        }

        line = JsonSerializer.SerializeToElement(payload, CompactJson);
        return true;
    }

    private static string ResolveVectorType(JsonElement el)
    {
        foreach (var key in new[] { "type", "stroke", "strokeType", "brushProfile", "kind" })
        {
            if (!el.TryGetProperty(key, out var t) || t.ValueKind != JsonValueKind.String)
                continue;
            var s = t.GetString()?.Trim();
            if (string.IsNullOrEmpty(s) || string.Equals(s, "stroke", StringComparison.OrdinalIgnoreCase))
                continue;
            return s.ToUpperInvariant();
        }

        return "WINDOWS_SKETCH";
    }

    private static bool TryReadPoints(JsonElement el, out List<(float x, float y)> points)
    {
        points = new List<(float, float)>();
        if (!el.TryGetProperty("points", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
            {
                points.Add((
                    ReadFloat(p[0]),
                    ReadFloat(p[1])));
                continue;
            }

            if (p.ValueKind == JsonValueKind.Object &&
                p.TryGetProperty("x", out var xEl) &&
                p.TryGetProperty("y", out var yEl))
            {
                points.Add((ReadFloat(xEl), ReadFloat(yEl)));
            }
        }

        return points.Count >= 2;
    }

    private static float ReadFloat(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out var d) => (float)d,
            JsonValueKind.String when float.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) => f,
            _ => 0f,
        };
    }

    private static int ReadInt(JsonElement el, string name, int fallback)
    {
        if (!el.TryGetProperty(name, out var v))
            return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback,
        };
    }

    private static bool IsWindowsAuthoredVectorLine(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return false;
        if (el.TryGetProperty("sourceClient", out var sc) &&
            sc.ValueKind == JsonValueKind.String &&
            string.Equals(sc.GetString(), WindowsSourceClient, StringComparison.Ordinal))
            return true;
        if (el.TryGetProperty("type", out var t) &&
            t.ValueKind == JsonValueKind.String &&
            t.GetString()?.StartsWith("WINDOWS_", StringComparison.Ordinal) == true)
            return true;
        return false;
    }
}
