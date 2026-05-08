using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Geographic bounding box for an Android X-Ray backdrop / satellite snapshot bitmap (degrees, WGS-84).
/// Used by <see cref="Views.OfflineXRayView"/> to align the survey lat/lon to the satellite pixel grid so the
/// traverse overlays the terrain exactly like the on-device X-Ray view.
/// </summary>
public sealed record XRayBackdropMetadata(
    double MinLat,
    double MaxLat,
    double MinLon,
    double MaxLon,
    string SourceLabel)
{
    public bool IsValid =>
        double.IsFinite(MinLat) && double.IsFinite(MaxLat) &&
        double.IsFinite(MinLon) && double.IsFinite(MaxLon) &&
        MaxLat > MinLat && MaxLon > MinLon &&
        Math.Abs(MaxLat) <= 90.0 + 1e-9 && Math.Abs(MinLat) <= 90.0 + 1e-9 &&
        Math.Abs(MaxLon) <= 360.0 + 1e-9 && Math.Abs(MinLon) <= 360.0 + 1e-9;

    public double SpanLatDeg => MaxLat - MinLat;
    public double SpanLonDeg => MaxLon - MinLon;
    public double CenterLat => 0.5 * (MinLat + MaxLat);
    public double CenterLon => 0.5 * (MinLon + MaxLon);
}

/// <summary>
/// Parses Android export metadata that describes the geographic extent of the satellite/X-Ray backdrop
/// (multiple shapes supported: explicit min/max bounds, NE/SW corner objects, or center + zoom + image pixel size).
/// </summary>
public static class XRayBackdropMetadataParser
{
    /// <summary>Property names that may carry a bounding-box object/array (case-insensitive).</summary>
    private static readonly string[] BoundsObjectKeys =
    [
        "xrayBackdropImageBounds", "xrayBackdropBoundsLatLon", "xrayBackdropBbox", "xrayBackdropBounds",
        "satelliteSnapshotBounds", "satelliteBounds", "satelliteBbox", "satelliteImageBounds",
        "geminiSatelliteBounds", "geminiSatelliteImageBounds",
        "mapBackdropBounds", "mapSnapshotBounds", "basemapBounds", "imageBoundsLatLon", "imageBbox",
    ];

    /// <summary>Try every known shape and return the first valid bbox.</summary>
    public static XRayBackdropMetadata? TryRead(CaveProjectDocument project)
    {
        if (project.XrayBackdropImageBounds.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
            TryReadBounds(project.XrayBackdropImageBounds, "xrayBackdropImageBounds") is { IsValid: true } explicitBbox)
            return explicitBbox;

        if (TryFromCenterAndZoom(project) is { IsValid: true } centerBased)
            return centerBased;
        if (TryFromCenterAndSpan(project) is { IsValid: true } centerSpan)
            return centerSpan;

        if (project.ExtensionData == null)
            return null;

        foreach (var keyName in BoundsObjectKeys)
        {
            if (!project.ExtensionData.TryGetValue(keyName, out var el))
                continue;
            if (TryReadBounds(el, keyName) is { IsValid: true } md)
                return md;
        }

        // Fall back: any extension key whose name contains "bounds"/"bbox"/"extent" with relevant prefix.
        foreach (var kv in project.ExtensionData)
        {
            var k = kv.Key.ToLowerInvariant();
            if (!(k.Contains("bound") || k.Contains("bbox") || k.Contains("extent")))
                continue;
            if (!(k.Contains("xray") || k.Contains("satellite") || k.Contains("backdrop") ||
                  k.Contains("snapshot") || k.Contains("basemap") || k.Contains("map")))
                continue;
            if (TryReadBounds(kv.Value, kv.Key) is { IsValid: true } md)
                return md;
        }

        return null;
    }

    private static XRayBackdropMetadata? TryReadBounds(JsonElement el, string sourceLabel)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryReadBoundsObject(el, sourceLabel) is { } o)
                    return o;
                break;
            case JsonValueKind.Array:
                if (TryReadBoundsArray(el, sourceLabel) is { } a)
                    return a;
                break;
        }

        return null;
    }

    private static XRayBackdropMetadata? TryReadBoundsObject(JsonElement el, string sourceLabel)
    {
        if (TryReadCornerPair(el, "northEast", "southWest", out var bbox1, sourceLabel))
            return bbox1;
        if (TryReadCornerPair(el, "ne", "sw", out var bbox2, sourceLabel))
            return bbox2;
        if (TryReadCornerPair(el, "topRight", "bottomLeft", out var bbox3, sourceLabel))
            return bbox3;
        if (TryReadCornerPair(el, "northWest", "southEast", out var bbox4, sourceLabel))
            return bbox4;

        if (TryReadFlatBounds(el, out var flat))
            return flat with { SourceLabel = sourceLabel };

        return null;
    }

    private static bool TryReadCornerPair(
        JsonElement el,
        string a,
        string b,
        out XRayBackdropMetadata? bbox,
        string sourceLabel)
    {
        bbox = null;
        if (!el.TryGetProperty(a, out var aEl) || !el.TryGetProperty(b, out var bEl))
            return false;
        if (!TryReadLatLon(aEl, out var lat1, out var lon1))
            return false;
        if (!TryReadLatLon(bEl, out var lat2, out var lon2))
            return false;
        var minLat = Math.Min(lat1, lat2);
        var maxLat = Math.Max(lat1, lat2);
        var minLon = Math.Min(lon1, lon2);
        var maxLon = Math.Max(lon1, lon2);
        bbox = new XRayBackdropMetadata(minLat, maxLat, minLon, maxLon, sourceLabel);
        return bbox.IsValid;
    }

    private static bool TryReadFlatBounds(JsonElement el, out XRayBackdropMetadata bbox)
    {
        bbox = new XRayBackdropMetadata(0, 0, 0, 0, "");
        var minLat = TryReadDouble(el, "minLat", "southLat", "south", "minLatitude");
        var maxLat = TryReadDouble(el, "maxLat", "northLat", "north", "maxLatitude");
        var minLon = TryReadDouble(el, "minLon", "westLon", "west", "minLongitude", "minLng");
        var maxLon = TryReadDouble(el, "maxLon", "eastLon", "east", "maxLongitude", "maxLng");
        if (minLat == null || maxLat == null || minLon == null || maxLon == null)
            return false;
        bbox = new XRayBackdropMetadata(minLat.Value, maxLat.Value, minLon.Value, maxLon.Value, "flatBounds");
        return bbox.IsValid;
    }

    private static XRayBackdropMetadata? TryReadBoundsArray(JsonElement arr, string sourceLabel)
    {
        if (arr.GetArrayLength() < 2)
            return null;
        if (!TryReadLatLon(arr[0], out var lat1, out var lon1) ||
            !TryReadLatLon(arr[1], out var lat2, out var lon2))
            return null;
        var min = (Math.Min(lat1, lat2), Math.Min(lon1, lon2));
        var max = (Math.Max(lat1, lat2), Math.Max(lon1, lon2));
        var bbox = new XRayBackdropMetadata(min.Item1, max.Item1, min.Item2, max.Item2, sourceLabel);
        return bbox.IsValid ? bbox : null;
    }

    private static bool TryReadLatLon(JsonElement el, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var lt = TryReadDouble(el, "lat", "latitude");
                var ln = TryReadDouble(el, "lon", "lng", "longitude");
                if (lt == null || ln == null)
                    return false;
                lat = lt.Value;
                lon = ln.Value;
                return true;
            }
            case JsonValueKind.Array when el.GetArrayLength() >= 2:
            {
                lat = ReadFloatOrZero(el[0]);
                lon = ReadFloatOrZero(el[1]);
                return double.IsFinite(lat) && double.IsFinite(lon);
            }
        }

        return false;
    }

    private static XRayBackdropMetadata? TryFromCenterAndZoom(CaveProjectDocument project)
    {
        if (project.ExtensionData == null)
            return null;
        var ext = project.ExtensionData;

        var centerLat = TryReadAlias(ext, "xrayBackdropCenterLat", "satelliteCenterLat", "geminiSatelliteCenterLat", "backdropCenterLat", "snapshotCenterLat");
        var centerLon = TryReadAlias(ext, "xrayBackdropCenterLon", "satelliteCenterLon", "geminiSatelliteCenterLon", "backdropCenterLon", "snapshotCenterLon", "xrayBackdropCenterLng", "satelliteCenterLng");
        var zoom = TryReadAlias(ext, "xrayBackdropZoom", "satelliteZoom", "geminiSatelliteZoom", "backdropZoom", "snapshotZoom");
        var pxW = TryReadAlias(ext, "xrayBackdropImageWidthPx", "satelliteImageWidthPx", "snapshotImageWidthPx", "backdropWidthPx", "imageWidthPx");
        var pxH = TryReadAlias(ext, "xrayBackdropImageHeightPx", "satelliteImageHeightPx", "snapshotImageHeightPx", "backdropHeightPx", "imageHeightPx");

        if (centerLat == null || centerLon == null || zoom == null || pxW == null || pxH == null)
            return null;
        if (pxW.Value <= 0 || pxH.Value <= 0 || zoom.Value < 0 || zoom.Value > 24)
            return null;

        // Web Mercator metres-per-pixel at given zoom and latitude.
        var metresPerPixel = 156543.03392 * Math.Cos(centerLat.Value * Math.PI / 180.0) / Math.Pow(2, zoom.Value);
        var spanXMetres = pxW.Value * metresPerPixel;
        var spanYMetres = pxH.Value * metresPerPixel;
        var spanLatDeg = spanYMetres / 111320.0;
        var spanLonDeg = spanXMetres / (111320.0 * Math.Max(1e-6, Math.Cos(centerLat.Value * Math.PI / 180.0)));

        return new XRayBackdropMetadata(
            centerLat.Value - spanLatDeg / 2.0,
            centerLat.Value + spanLatDeg / 2.0,
            centerLon.Value - spanLonDeg / 2.0,
            centerLon.Value + spanLonDeg / 2.0,
            "centerZoom(WebMercator)");
    }

    private static XRayBackdropMetadata? TryFromCenterAndSpan(CaveProjectDocument project)
    {
        if (project.ExtensionData == null)
            return null;
        var ext = project.ExtensionData;
        var centerLat = TryReadAlias(ext, "xrayBackdropCenterLat", "satelliteCenterLat", "geminiSatelliteCenterLat");
        var centerLon = TryReadAlias(ext, "xrayBackdropCenterLon", "satelliteCenterLon", "geminiSatelliteCenterLon", "xrayBackdropCenterLng");
        var spanLat = TryReadAlias(ext, "xrayBackdropSpanLatDeg", "satelliteSpanLatDeg", "backdropSpanLat", "snapshotSpanLat");
        var spanLon = TryReadAlias(ext, "xrayBackdropSpanLonDeg", "satelliteSpanLonDeg", "backdropSpanLon", "snapshotSpanLon", "xrayBackdropSpanLngDeg");
        if (centerLat == null || centerLon == null || spanLat == null || spanLon == null)
            return null;
        if (spanLat.Value <= 0 || spanLon.Value <= 0)
            return null;
        return new XRayBackdropMetadata(
            centerLat.Value - spanLat.Value / 2.0,
            centerLat.Value + spanLat.Value / 2.0,
            centerLon.Value - spanLon.Value / 2.0,
            centerLon.Value + spanLon.Value / 2.0,
            "centerSpan");
    }

    private static double? TryReadAlias(Dictionary<string, JsonElement> ext, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!ext.TryGetValue(k, out var el))
                continue;
            switch (el.ValueKind)
            {
                case JsonValueKind.Number when el.TryGetDouble(out var d):
                    return d;
                case JsonValueKind.String when double.TryParse(el.GetString(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s):
                    return s;
            }
        }

        return null;
    }

    private static double? TryReadDouble(JsonElement obj, params string[] propNames)
    {
        foreach (var prop in propNames)
        {
            if (!obj.TryGetProperty(prop, out var p))
                continue;
            switch (p.ValueKind)
            {
                case JsonValueKind.Number when p.TryGetDouble(out var d):
                    return d;
                case JsonValueKind.String when double.TryParse(p.GetString(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s):
                    return s;
            }
        }

        return null;
    }

    private static double ReadFloatOrZero(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String => double.TryParse(el.GetString(),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0,
            _ => 0,
        };
}
