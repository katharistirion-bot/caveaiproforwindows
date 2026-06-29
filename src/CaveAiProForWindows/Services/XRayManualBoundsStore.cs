using System.Globalization;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Manual X-Ray geo bounds saved from PC calibration (extensionData).</summary>
public static class XRayManualBoundsStore
{
    public const string ExtensionKey = "xrayManualBoundsLatLon";

    public static bool TryRead(CaveProjectDocument project, out XRayBackdropMetadata bounds)
    {
        bounds = new XRayBackdropMetadata(0, 0, 0, 0, ExtensionKey);
        if (project.ExtensionData == null ||
            !project.ExtensionData.TryGetValue(ExtensionKey, out var el) ||
            el.ValueKind != JsonValueKind.Object)
            return false;

        if (!TryReadDouble(el, "minLat", out var minLat) ||
            !TryReadDouble(el, "maxLat", out var maxLat) ||
            !TryReadDouble(el, "minLon", out var minLon) ||
            !TryReadDouble(el, "maxLon", out var maxLon))
            return false;

        bounds = new XRayBackdropMetadata(minLat, maxLat, minLon, maxLon, ExtensionKey);
        return bounds.IsValid;
    }

    public static void Save(CaveProjectDocument project, XRayBackdropMetadata bounds)
    {
        project.ExtensionData ??= new Dictionary<string, JsonElement>();
        var inv = CultureInfo.InvariantCulture;
        var json = JsonSerializer.SerializeToElement(new
        {
            minLat = bounds.MinLat.ToString("R", inv),
            maxLat = bounds.MaxLat.ToString("R", inv),
            minLon = bounds.MinLon.ToString("R", inv),
            maxLon = bounds.MaxLon.ToString("R", inv),
            source = "windowsManualCalibration",
        });
        project.ExtensionData[ExtensionKey] = json;
    }

    private static bool TryReadDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }
}