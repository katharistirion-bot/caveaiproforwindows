using System.Globalization;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public static class ExplorationAnalytics
{
    public static string BuildSummaryText(CaveProjectDocument p)
    {
        var inv = CultureInfo.InvariantCulture;
        var shots = p.Shots;
        var traverses = shots.Count(s => s.IsTraverseLeg);
        var splays = shots.Count(s => !s.IsTraverseLeg);
        var sumTraverseDist = shots.Where(s => s.IsTraverseLeg).Sum(s => (double)s.Distance);
        var stations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in shots)
        {
            if (!string.IsNullOrWhiteSpace(s.FromStation)) stations.Add(s.FromStation);
            if (s.IsTraverseLeg && !string.IsNullOrWhiteSpace(s.ToStation)) stations.Add(s.ToStation);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Project: {p.Name}");
        sb.AppendLine($"Date: {p.Date}   Entrance alt (m): {p.Alt:0.##}");
        if (p.Lat is { } la && p.Lon is { } lo)
            sb.AppendLine($"Entrance: {la.ToString(inv)}, {lo.ToString(inv)}");
        sb.AppendLine($"Shots total: {shots.Count}  (traverse legs: {traverses}, splays / other: {splays})");
        sb.AppendLine($"Unique station IDs (from/to on traverses): {stations.Count}");
        sb.AppendLine($"Sum traverse distances (horizontal leg lengths as recorded): {sumTraverseDist:0.##} m");
        sb.AppendLine($"Geo/bio rock samples: {p.RocksCount}");
        sb.AppendLine($"Field catalog entries: {p.FieldCatalogEntryCount}");
        var vecCount = JsonArrayLength(p.VectorLines);
        if (vecCount > 0)
            sb.AppendLine($"Map vector overlays (vectorLines): {vecCount} — drawn on Plan tab (cyan) with sketches.");
        var planStations = SurveyStationGeometry.CalculatePlanCoordinates(p);
        if (planStations.Count > 0)
            sb.AppendLine($"Plan geometry (traverse reduction, same as Android): {planStations.Count} station position(s).");
        AppendMapsAndLibraries(sb, p);
        if (shots.Count > 0)
        {
            var last = shots[^1];
            sb.AppendLine($"Last shot depth field: {last.Depth:0.##} m  →  {last.FromStation}–{last.ToStation}  {last.Distance:0.##} m / {last.Clino}°");
        }

        return sb.ToString();
    }

    private static void AppendMapsAndLibraries(StringBuilder sb, CaveProjectDocument p)
    {
        var ext = p.ExtensionData;

        static int ArrCountExt(Dictionary<string, JsonElement>? d, string key) =>
            d != null && d.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Array ? el.GetArrayLength() : 0;

        static int ArrCountPrimaryOrExt(JsonElement primary, Dictionary<string, JsonElement>? d, string key)
        {
            if (primary.ValueKind == JsonValueKind.Array)
                return primary.GetArrayLength();
            return ArrCountExt(d, key);
        }

        if (!string.IsNullOrWhiteSpace(p.LinkedLibraryCaveId))
            sb.AppendLine($"Cave Library link: linkedLibraryCaveId = {p.LinkedLibraryCaveId.Trim()}");
        else if (ext?.TryGetValue("linkedLibraryCaveId", out var lid) == true && lid.ValueKind == JsonValueKind.String)
        {
            var s = lid.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                sb.AppendLine($"Cave Library link: linkedLibraryCaveId = {s}");
        }

        var cartUris = ArrCountPrimaryOrExt(p.PublicLibraryCartographyUris, ext, "publicLibraryCartographyUris");
        if (cartUris > 0)
            sb.AppendLine($"Public Library cartography URIs in JSON: {cartUris} (use Export → Extract full archive on PC for files).");

        var hasSurfaceRaster = p.SurfaceLidarRaster.ValueKind is JsonValueKind.Object or JsonValueKind.Array ||
                               (ext?.TryGetValue("surfaceLidarRaster", out var slrExt) == true &&
                                slrExt.ValueKind is JsonValueKind.Object or JsonValueKind.Array);
        if (hasSurfaceRaster)
            sb.AppendLine("Surface raster / LIDAR overlay: present in JSON (extract ZIP for bundled rasters).");

        if (!string.IsNullOrWhiteSpace(p.CartographyTlsMeshObjUri))
            sb.AppendLine("TLS / mesh OBJ uri: present (Android path — extract full archive to copy blobs).");
        else if (ext?.TryGetValue("cartographyTlsMeshObjUri", out var mesh) == true && mesh.ValueKind == JsonValueKind.String)
        {
            var s = mesh.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                sb.AppendLine("TLS / mesh OBJ uri: present (Android path — extract full archive to copy blobs).");
        }

        var nMapSym = ArrCountPrimaryOrExt(p.MapSymbols, ext, "mapSymbols");
        if (nMapSym > 0) sb.AppendLine($"Map symbols: {nMapSym}");
        var nSketch = ArrCountPrimaryOrExt(p.Sketches, ext, "sketches");
        if (nSketch > 0) sb.AppendLine($"Plan sketches (polylines): {nSketch}");
        var nSketchLayer = ArrCountPrimaryOrExt(p.SketchLayer, ext, "sketchLayer");
        if (nSketchLayer > 0) sb.AppendLine($"Sketch layer (plan strokes): {nSketchLayer}");
        var nMapObjects = ArrCountPrimaryOrExt(p.MapObjects, ext, "mapObjects");
        if (nMapObjects > 0) sb.AppendLine($"Map objects (mixed strokes / symbols): {nMapObjects}");
        var nSecSk = ArrCountPrimaryOrExt(p.SectionSketches, ext, "sectionSketches");
        if (nSecSk > 0) sb.AppendLine($"Section sketches: {nSecSk}");
        var nTrack = ArrCountPrimaryOrExt(p.TrackPoints, ext, "trackPoints");
        if (nTrack > 0) sb.AppendLine($"Surface track points: {nTrack}");

        var logLen = p.SurveyEventLog.ValueKind == JsonValueKind.Array
            ? p.SurveyEventLog.GetArrayLength()
            : ArrCountExt(ext, "surveyEventLog");
        if (logLen > 0)
            sb.AppendLine($"Survey event log lines: {logLen}");
    }

    private static int JsonArrayLength(JsonElement? e)
    {
        if (e is not { ValueKind: JsonValueKind.Array } arr)
            return 0;
        return arr.GetArrayLength();
    }

    /// <summary>UTF-8 with BOM for Excel-friendly Greek/notes.</summary>
    public static byte[] ExportShotsToCsvUtf8Bom(CaveProjectDocument p)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "fromStation,toStation,distance_m,clino_deg,azimuth_deg,depth_m,l_m,r_m,u_m,d_m,notes,comment,time,timestampUtcMs," +
            "measurementStartedUtcMs,measurementCompletedUtcMs,compassSampleVarianceDeg2,clinoSampleVarianceDeg2," +
            "compassStdDeg,clinoStdDeg,tapeStdM,sensorFusionQuality,horizontalPositionAccuracyM,verticalPositionAccuracyM,symbol");
        foreach (var s in p.Shots)
        {
            static string Csv(string? x)
            {
                if (string.IsNullOrEmpty(x)) return "";
                var t = x.Replace("\"", "\"\"", StringComparison.Ordinal);
                if (t.Contains(',') || t.Contains('"') || t.Contains('\n')) return $"\"{t}\"";
                return t;
            }

            static string F(float? v, IFormatProvider inv) => v?.ToString(inv) ?? "";

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sb.AppendLine(string.Join(',',
                Csv(s.FromStation),
                Csv(s.ToStation),
                s.Distance.ToString(inv),
                s.Clino.ToString(inv),
                s.Azimuth.ToString(inv),
                s.Depth.ToString(inv),
                s.L.ToString(inv),
                s.R.ToString(inv),
                s.U.ToString(inv),
                s.D.ToString(inv),
                Csv(s.Notes),
                Csv(s.Comment),
                Csv(s.Time),
                s.TimestampUtcMs?.ToString(inv) ?? "",
                s.MeasurementStartedUtcMs?.ToString(inv) ?? "",
                s.MeasurementCompletedUtcMs?.ToString(inv) ?? "",
                F(s.CompassSampleVarianceDeg2, inv),
                F(s.ClinoSampleVarianceDeg2, inv),
                F(s.CompassStdDeg, inv),
                F(s.ClinoStdDeg, inv),
                F(s.TapeStdM, inv),
                F(s.SensorFusionQuality, inv),
                F(s.HorizontalPositionAccuracyM, inv),
                F(s.VerticalPositionAccuracyM, inv),
                Csv(s.Symbol)));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }
}
