using System.Globalization;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// When Android exports explicit plan-frame station coordinates, merge them into
/// <see cref="CaveProjectDocument.PlanStationPositionOverrides"/> so the PC uses the same XYZ as the device.
/// </summary>
public static class ImportedStationCoordinatesBootstrap
{
    private static readonly string[] ContainerKeys =
    [
        "stationPlanCoordinates",
        "planStationCoordinates",
        "surveyStationCoordinates",
        "stationSurveyPositions",
        "planStations",
    ];

    public static void TryApply(CaveProjectDocument project)
    {
        if (project.ExtensionData == null || project.ExtensionData.Count == 0)
            return;

        if (project.ExtensionData.TryGetValue("planStationPositionOverrides", out var overrideMap) &&
            overrideMap.ValueKind == JsonValueKind.Object)
            IngestOverrideObjectMap(overrideMap, project);

        foreach (var key in ContainerKeys)
        {
            if (!project.ExtensionData.TryGetValue(key, out var root))
                continue;
            if (TryApplyFromElement(root, project))
                return;
        }
    }

    /// <summary>Object map <c>{ "B2": { "x", "y", "z" } }</c> (canonical Android / web key).</summary>
    private static void IngestOverrideObjectMap(JsonElement root, CaveProjectDocument project)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(prop.Name) || prop.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (!TryReadTriple(prop.Value, out var x, out var y, out var z))
                continue;
            project.PlanStationPositionOverrides[prop.Name.Trim()] = new PlanStationPositionOverride(x, y, z);
        }
    }

    private static bool TryApplyFromElement(JsonElement root, CaveProjectDocument project)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                return IngestStationArray(root, project);
            case JsonValueKind.Object:
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Array)
                        continue;
                    if (IngestStationArray(prop.Value, project))
                        return true;
                }

                return false;
            }
            default:
                return false;
        }
    }

    private static bool IngestStationArray(JsonElement arr, CaveProjectDocument project)
    {
        var any = false;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (!TryReadStationName(el, out var name) || string.IsNullOrWhiteSpace(name))
                continue;
            if (!TryReadTriple(el, out var x, out var y, out var z))
                continue;
            project.PlanStationPositionOverrides[name.Trim()] = new PlanStationPositionOverride(x, y, z);
            any = true;
        }

        return any;
    }

    private static bool TryReadStationName(JsonElement el, out string name)
    {
        name = "";
        foreach (var key in new[] { "name", "station", "stationName", "id", "label" })
        {
            if (!el.TryGetProperty(key, out var p) || p.ValueKind != JsonValueKind.String)
                continue;
            name = p.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(name))
                return true;
        }

        return false;
    }

    private static bool TryReadTriple(JsonElement el, out float x, out float y, out float z)
    {
        x = y = z = 0;
        if (TryFirstFloat(el, out x, "x", "east", "easting", "planX", "surveyX") &&
            TryFirstFloat(el, out y, "y", "north", "northing", "planY", "surveyY") &&
            TryFirstFloat(el, out z, "z", "elev", "elevation", "alt", "planZ", "surveyZ"))
            return true;

        if (el.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Object)
            return TryReadTriple(pos, out x, out y, out z);

        return false;
    }

    private static bool TryFirstFloat(JsonElement obj, out float value, params string[] names)
    {
        foreach (var n in names)
        {
            if (!obj.TryGetProperty(n, out var p))
                continue;
            if (TryGetFloat(p, out var v))
            {
                value = v;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetFloat(JsonElement el, out float v)
    {
        v = 0;
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                if (el.TryGetSingle(out v))
                    return true;
                if (el.TryGetDouble(out var d))
                {
                    v = (float)d;
                    return true;
                }

                return false;
            case JsonValueKind.String:
                return float.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            default:
                return false;
        }
    }
}
