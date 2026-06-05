using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Per-station WGS-84 fix from Android JSON (when exported).</summary>
public readonly record struct SurveyStationGpsFix(string StationName, double Lat, double Lon, string Source);

/// <summary>Collects station-level GPS from Android export shapes.</summary>
public static class SurveyStationGpsCatalog
{
    public static IReadOnlyDictionary<string, SurveyStationGpsFix> Build(CaveProjectDocument project)
    {
        var map = new Dictionary<string, SurveyStationGpsFix>(StringComparer.OrdinalIgnoreCase);

        foreach (var snap in project.StationEnvironmentSnapshots)
        {
            var lat = snap.Lat ?? snap.Latitude;
            var lon = snap.Lon ?? snap.Longitude;
            if (lat is { } la && lon is { } lo && !string.IsNullOrWhiteSpace(snap.StationName))
                TryAdd(map, snap.StationName.Trim(), la, lo, "stationEnvironmentSnapshots");
        }

        if (project.ExtensionData != null)
        {
            foreach (var key in new[]
                     {
                         "stationGpsPositions", "stationCoordinates", "stationLatLon",
                         "stationGps", "gpsStations",
                     })
            {
                if (project.ExtensionData.TryGetValue(key, out var el))
                    ParseArray(el, map, key);
            }
        }

        return map;
    }

    private static void ParseArray(JsonElement el, Dictionary<string, SurveyStationGpsFix> map, string source)
    {
        if (el.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var station = ReadString(item, "station", "stationName", "name", "stn");
            if (string.IsNullOrWhiteSpace(station))
                continue;
            if (!TryReadLatLon(item, out var lat, out var lon))
                continue;
            TryAdd(map, station!, lat, lon, source);
        }
    }

    private static void TryAdd(
        Dictionary<string, SurveyStationGpsFix> map,
        string station,
        double lat,
        double lon,
        string source)
    {
        if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
            return;
        map[station] = new SurveyStationGpsFix(station, lat, lon, source);
    }

    private static string? ReadString(JsonElement o, params string[] names)
    {
        foreach (var n in names)
        {
            if (!o.TryGetProperty(n, out var el))
                continue;
            if (el.ValueKind == JsonValueKind.String)
                return el.GetString()?.Trim();
            if (el.ValueKind == JsonValueKind.Number)
                return el.GetRawText();
        }

        return null;
    }

    private static bool TryReadLatLon(JsonElement o, out double lat, out double lon)
    {
        lat = lon = 0;
        if (!TryReadDouble(o, out lat, "lat", "latitude", "y"))
            return false;
        return TryReadDouble(o, out lon, "lon", "lng", "longitude", "x");
    }

    private static bool TryReadDouble(JsonElement o, out double value, params string[] names)
    {
        value = 0;
        foreach (var n in names)
        {
            if (!o.TryGetProperty(n, out var el))
                continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value))
                return true;
            if (el.ValueKind == JsonValueKind.String &&
                double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
        }

        return false;
    }
}

/// <summary>Exports georeferenced X-Ray raster + world file for QGIS / GIS workflows.</summary>
public static class XRayGeoreferencedExporter
{
    public static void ExportPngWithWorldFile(
        string outputPathWithoutExtension,
        byte[] pngBytes,
        XRayBackdropMetadata bbox,
        double imageWidthPx,
        double imageHeightPx)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPathWithoutExtension);
        var pngPath = outputPathWithoutExtension + ".png";
        File.WriteAllBytes(pngPath, pngBytes);

        var spanLon = Math.Max(1e-12, bbox.MaxLon - bbox.MinLon);
        var spanLat = Math.Max(1e-12, bbox.MaxLat - bbox.MinLat);
        var pxSizeX = spanLon / imageWidthPx;
        var pxSizeY = -spanLat / imageHeightPx;

        var pgw = string.Join('\n', new[]
        {
            pxSizeX.ToString("R", CultureInfo.InvariantCulture),
            "0",
            "0",
            pxSizeY.ToString("R", CultureInfo.InvariantCulture),
            (bbox.MinLon + pxSizeX * 0.5).ToString("R", CultureInfo.InvariantCulture),
            (bbox.MaxLat + pxSizeY * 0.5).ToString("R", CultureInfo.InvariantCulture),
        });
        File.WriteAllText(outputPathWithoutExtension + ".pgw", pgw, Encoding.ASCII);

        const string wgs84 =
            "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563," +
            "AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]]," +
            "PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]]," +
            "UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]]," +
            "AUTHORITY[\"EPSG\",\"4326\"]]";
        File.WriteAllText(outputPathWithoutExtension + ".prj", wgs84, Encoding.ASCII);
    }
}
