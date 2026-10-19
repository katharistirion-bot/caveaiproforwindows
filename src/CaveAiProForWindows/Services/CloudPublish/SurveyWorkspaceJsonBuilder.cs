using System.IO;
using System.Text;
using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Slim survey JSON for web workspace (mirrors website slimProjectForWorkspace / Android SurveyWorkspaceJson).
/// Always returns bytes — either a slim object or a fallback marker so stale Storage objects are overwritten.
/// </summary>
public static class SurveyWorkspaceJsonBuilder
{
    public const int MaxBytes = 12 * 1024 * 1024;

    private static readonly byte[] FallbackMarkerUtf8 =
        Encoding.UTF8.GetBytes("""{"__caveAiWorkspaceInvalid":true,"shots":[]}""");

    private static readonly HashSet<string> ProjectKeys = new(StringComparer.Ordinal)
    {
        "name", "date", "alt", "exitAlt", "shots", "planStationPositionOverrides",
        "vectorLines", "mapSymbols", "brackets", "sketches", "sketchLayer", "mapObjects",
        "planSketchLayer", "freehandSketches", "mapSketchLayer", "sectionSketches",
        "publicLibraryCartographyUris", "lat", "lon", "surfaceLidarRaster",
        "surveyCalibrationProfile", "magneticDeclinationOverrideDeg",
        "namedCartographyMaps", "activeCartographyMapId", "depthSpanAnnotations",
    };

    /// <summary>The open sheet is already stored on the live sketch/vector fields.</summary>
    private static readonly HashSet<string> OpenCartographyLayerKeys = new(StringComparer.Ordinal)
    {
        "sketches",
        "sectionSketches",
        "mapSymbols",
        "vectorLines",
        "brackets",
        "depthSpanAnnotations",
    };

    private static readonly HashSet<string> ShotKeys = new(StringComparer.Ordinal)
    {
        "fromStation", "toStation", "from", "to", "azimuth", "clino", "distance",
        "l", "r", "u", "d", "left", "right", "up", "down", "radials", "hasSplayWatch",
        "atmosphericO2VolPct", "ambientBleTempCelsius", "manualAmbientTempCelsius",
        "ambientBleRelativeHumidityPct", "manualRelativeHumidityPct", "co2Ppm", "pressureHpa",
    };

    public static byte[] BuildBytes(byte[] fullProjectObjectUtf8)
    {
        try
        {
            using var doc = JsonDocument.Parse(fullProjectObjectUtf8);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return FallbackMarkerUtf8;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!ProjectKeys.Contains(prop.Name))
                        continue;
                    writer.WritePropertyName(prop.Name);
                    if (prop.Name == "shots" && prop.Value.ValueKind == JsonValueKind.Array)
                        WriteSlimShots(writer, prop.Value);
                    else if (prop.Name == "publicLibraryCartographyUris" && prop.Value.ValueKind == JsonValueKind.Array)
                        WriteSlimUriList(writer, prop.Value);
                    else if (prop.Name == "namedCartographyMaps" && prop.Value.ValueKind == JsonValueKind.Array)
                        WriteSlimNamedCartographyMaps(writer, doc.RootElement, prop.Value);
                    else
                        prop.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            var bytes = stream.ToArray();
            return bytes.Length == 0 || bytes.Length > MaxBytes ? FallbackMarkerUtf8 : bytes;
        }
        catch
        {
            return FallbackMarkerUtf8;
        }
    }

    private static void WriteSlimShots(Utf8JsonWriter writer, JsonElement shots)
    {
        writer.WriteStartArray();
        foreach (var shot in shots.EnumerateArray())
        {
            if (shot.ValueKind != JsonValueKind.Object)
                continue;
            var kept = new List<JsonProperty>();
            foreach (var prop in shot.EnumerateObject())
            {
                if (!ShotKeys.Contains(prop.Name) || prop.Value.ValueKind == JsonValueKind.Null)
                    continue;
                kept.Add(prop);
            }
            if (kept.Count == 0)
                continue;
            writer.WriteStartObject();
            foreach (var prop in kept)
            {
                writer.WritePropertyName(prop.Name);
                prop.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    /// <summary>
    /// Drop the open sheet's duplicated ink. Other named maps stay complete so the viewer can switch.
    /// </summary>
    private static void WriteSlimNamedCartographyMaps(Utf8JsonWriter writer, JsonElement root, JsonElement maps)
    {
        var openId = "";
        if (root.TryGetProperty("activeCartographyMapId", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            openId = idEl.GetString()?.Trim() ?? "";

        writer.WriteStartArray();
        foreach (var item in maps.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || openId.Length == 0)
            {
                item.WriteTo(writer);
                continue;
            }

            var id = "";
            if (item.TryGetProperty("id", out var mapId) && mapId.ValueKind == JsonValueKind.String)
                id = mapId.GetString()?.Trim() ?? "";
            if (!string.Equals(id, openId, StringComparison.Ordinal))
            {
                item.WriteTo(writer);
                continue;
            }

            writer.WriteStartObject();
            foreach (var prop in item.EnumerateObject())
            {
                if (OpenCartographyLayerKeys.Contains(prop.Name))
                    continue;
                writer.WritePropertyName(prop.Name);
                prop.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteSlimUriList(Utf8JsonWriter writer, JsonElement uris)
    {
        writer.WriteStartArray();
        foreach (var item in uris.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;
            var s = item.GetString();
            if (!string.IsNullOrEmpty(s))
                writer.WriteStringValue(s);
        }
        writer.WriteEndArray();
    }
}