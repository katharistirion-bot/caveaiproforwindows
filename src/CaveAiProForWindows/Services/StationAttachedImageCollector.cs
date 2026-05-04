using System.Globalization;
using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Collects station-attached image paths from <c>rocks</c>, <c>fieldCatalogEntries</c>, sketch arrays, and traverse shot <c>photos</c>.
/// </summary>
public static class StationAttachedImageCollector
{
    private const int MaxRefs = 160;

    private static readonly string[] StationPropertyNames =
    [
        "station", "stationName", "fromStation", "atStation", "linkedStation", "traverseStation",
        "shotStation", "baseStation", "referencedStation", "nearStation", "anchorStation",
    ];

    private static readonly string[] ImagePropertyNames =
    [
        "imageUri", "bitmapUri", "photoUri", "thumbnailUri", "photoReference", "photoUrl", "imageUrl",
        "attachmentUri", "fileUri", "uri",
    ];

    private static readonly string[] RotationPropertyNames =
    [
        "rotation", "rotationDeg", "rotationDegrees", "angle", "heading", "azimuthImage", "imageRotation",
    ];

    private static readonly string[] ScalePropertyNames =
    [
        "scale", "imageScale", "photoScale", "displayScale", "sizeScale", "zoom",
    ];

    public static IReadOnlyList<StationAttachedImageRef> Collect(CaveProjectDocument p, int vectorViewMode)
    {
        var list = new List<StationAttachedImageRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string station, string uri, string category)
        {
            station = (station ?? "").Trim();
            uri = (uri ?? "").Trim();
            if (station.Length == 0 || uri.Length == 0)
                return;
            if (!LooksLikeRasterImageReference(uri))
                return;
            var key = station + "|" + uri + "|" + category;
            if (!seen.Add(key))
                return;
            if (list.Count >= MaxRefs)
                return;
            list.Add(new StationAttachedImageRef(station, uri, category, null, null));
        }

        void TryAddWithMeta(string station, string uri, string category, JsonElement metaSource)
        {
            station = (station ?? "").Trim();
            uri = (uri ?? "").Trim();
            if (station.Length == 0 || uri.Length == 0)
                return;
            if (!LooksLikeRasterImageReference(uri))
                return;
            var rot = TryPickDouble(metaSource, RotationPropertyNames);
            var sc = TryPickDouble(metaSource, ScalePropertyNames);
            if (sc is { } s && (s <= 0 || double.IsNaN(s) || double.IsInfinity(s)))
                sc = null;
            if (rot is { } r && (double.IsNaN(r) || double.IsInfinity(r)))
                rot = null;
            var key = station + "|" + uri + "|" + category;
            if (!seen.Add(key))
                return;
            if (list.Count >= MaxRefs)
                return;
            list.Add(new StationAttachedImageRef(station, uri, category, rot, sc));
        }

        void AppendSketchImages(JsonElement root, string rootKey)
        {
            if (root.ValueKind != JsonValueKind.Array)
                return;
            var i = 0;
            foreach (var el in root.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                {
                    i++;
                    continue;
                }

                if (el.TryGetProperty("viewMode", out var vmEl) && vmEl.ValueKind == JsonValueKind.Number &&
                    vmEl.TryGetInt32(out var vm))
                {
                    if (vectorViewMode == SurveyStationGeometry.AndroidViewModePlan && vm != SurveyStationGeometry.AndroidViewModePlan)
                    {
                        i++;
                        continue;
                    }

                    if (vectorViewMode == SurveyStationGeometry.AndroidViewModeSection && vm != SurveyStationGeometry.AndroidViewModeSection)
                    {
                        i++;
                        continue;
                    }
                }

                var st = PickStationName(el);
                var uri = PickImageUri(el);
                if (st != null && uri != null)
                    TryAddWithMeta(st, uri, $"{rootKey}[{i}]", el);

                i++;
            }
        }

        if (p.Rocks is { ValueKind: JsonValueKind.Array } rockArr)
        {
            var ri = 0;
            foreach (var el in rockArr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                {
                    ri++;
                    continue;
                }

                var st = PickStationName(el);
                var uri = PickImageUri(el);
                if (st != null && uri != null)
                    TryAddWithMeta(st, uri, $"rocks[{ri}]", el);
                ri++;
            }
        }

        if (p.FieldCatalogEntries is { ValueKind: JsonValueKind.Array } catArr)
        {
            var fi = 0;
            foreach (var el in catArr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                {
                    fi++;
                    continue;
                }

                var st = PickStationName(el);
                var uri = PickImageUri(el);
                if (st != null && uri != null)
                    TryAddWithMeta(st, uri, $"fieldCatalogEntries[{fi}]", el);
                fi++;
            }
        }

        if (p.ExtensionData != null)
        {
            if (vectorViewMode == SurveyStationGeometry.AndroidViewModePlan &&
                p.ExtensionData.TryGetValue("sketches", out var skPlan))
                AppendSketchImages(skPlan, "sketches");
            if (vectorViewMode == SurveyStationGeometry.AndroidViewModeSection &&
                p.ExtensionData.TryGetValue("sectionSketches", out var skSec))
                AppendSketchImages(skSec, "sectionSketches");
        }

        for (var si = 0; si < p.Shots.Count; si++)
        {
            var shot = p.Shots[si];
            var st = (shot.FromStation ?? "").Trim();
            if (st.Length == 0)
                continue;
            var pi = 0;
            foreach (var ph in shot.Photos)
            {
                if (!string.IsNullOrWhiteSpace(ph))
                    TryAdd(st, ph.Trim(), $"shots[{si}].photos[{pi}]");
                pi++;
            }
        }

        return list;
    }

    private static string? PickStationName(JsonElement obj)
    {
        foreach (var key in StationPropertyNames)
        {
            if (!obj.TryGetProperty(key, out var v))
                continue;
            var s = JsonString(v);
            if (!string.IsNullOrWhiteSpace(s))
                return s.Trim();
        }

        return null;
    }

    private static string? PickImageUri(JsonElement obj)
    {
        foreach (var key in ImagePropertyNames)
        {
            if (!obj.TryGetProperty(key, out var v))
                continue;
            var s = JsonString(v);
            if (!string.IsNullOrWhiteSpace(s) && LooksLikeRasterImageReference(s))
                return s.Trim();
        }

        return null;
    }

    private static double? TryPickDouble(JsonElement obj, string[] names)
    {
        foreach (var key in names)
        {
            if (!obj.TryGetProperty(key, out var v))
                continue;
            if (v.ValueKind == JsonValueKind.Number)
            {
                var d = v.GetDouble();
                if (!double.IsNaN(d) && !double.IsInfinity(d))
                    return d;
            }

            if (v.ValueKind == JsonValueKind.String &&
                double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds) &&
                !double.IsNaN(ds) && !double.IsInfinity(ds))
                return ds;
        }

        return null;
    }

    private static string? JsonString(JsonElement v) =>
        v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Raster-ish references suitable for <see cref="RasterImageDecoder"/> (excludes bare .obj etc.).</summary>
    public static bool LooksLikeRasterImageReference(string uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath))
            return false;
        var t = uriOrPath.Trim();
        var q = t.IndexOf('?', StringComparison.Ordinal);
        var pathForExt = q >= 0 ? t[..q] : t;
        pathForExt = pathForExt.Replace('\\', '/');
        if (pathForExt.Contains("..", StringComparison.Ordinal))
            return false;
        if (pathForExt.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (StandaloneMapFileSupport.HasStandaloneMapExtension(pathForExt))
        {
            var ext = Path.GetExtension(pathForExt).ToLowerInvariant();
            if (ext == ".obj" || ext == ".mtl" || ext == ".svg" || ext == ".kml" || ext == ".kmz" || ext == ".gpx" ||
                ext == ".dxf" || ext == ".geojson")
                return false;
            return true;
        }

        if (pathForExt.StartsWith("photos/", StringComparison.OrdinalIgnoreCase) ||
            pathForExt.Contains("/photos/", StringComparison.OrdinalIgnoreCase) ||
            pathForExt.StartsWith("export_assets/", StringComparison.OrdinalIgnoreCase) ||
            pathForExt.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
            pathForExt.Contains("/maps/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
