using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public static class ExplorationAnalytics
{
    public static string BuildSummaryText(CaveProjectDocument p)
    {
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
        if (p.Lat is { } la && p.Lon is { } lo) sb.AppendLine($"Entrance: {la}, {lo}");
        sb.AppendLine($"Shots total: {shots.Count}  (traverse legs: {traverses}, splays / other: {splays})");
        sb.AppendLine($"Unique station IDs (from/to on traverses): {stations.Count}");
        sb.AppendLine($"Sum traverse distances (horizontal leg lengths as recorded): {sumTraverseDist:0.##} m");
        sb.AppendLine($"Geo/bio rock samples: {p.RocksCount}");
        sb.AppendLine($"Field catalog entries: {p.FieldCatalogEntryCount}");
        var vecCount = JsonArrayLength(p.VectorLines);
        if (vecCount > 0)
            sb.AppendLine($"Map vector overlays (vectorLines): {vecCount} — drawn on Plan tab (cyan) with sketches.");
        var planStations = SurveyStationGeometry.CalculatePlanCoordinates(p.Shots, (float)p.Alt);
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
        if (ext == null || ext.Count == 0)
            return;

        static int ArrCount(Dictionary<string, JsonElement> d, string key) =>
            d.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Array ? el.GetArrayLength() : 0;

        if (ext.TryGetValue("linkedLibraryCaveId", out var lid) && lid.ValueKind == JsonValueKind.String)
        {
            var s = lid.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                sb.AppendLine($"Cave Library link: linkedLibraryCaveId = {s}");
        }

        var cartUris = ArrCount(ext, "publicLibraryCartographyUris");
        if (cartUris > 0)
            sb.AppendLine($"Public Library cartography URIs in JSON: {cartUris} (use Export → Extract full archive on PC for files).");

        if (ext.TryGetValue("surfaceLidarRaster", out var slr) && slr.ValueKind == JsonValueKind.Object)
            sb.AppendLine("Surface raster / LIDAR overlay: present in JSON (extract ZIP for bundled rasters).");

        if (ext.TryGetValue("cartographyTlsMeshObjUri", out var mesh) && mesh.ValueKind == JsonValueKind.String)
        {
            var s = mesh.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                sb.AppendLine("TLS / mesh OBJ uri: present (Android path — extract full archive to copy blobs).");
        }

        var nMapSym = ArrCount(ext, "mapSymbols");
        if (nMapSym > 0) sb.AppendLine($"Map symbols: {nMapSym}");
        var nSketch = ArrCount(ext, "sketches");
        if (nSketch > 0) sb.AppendLine($"Plan sketches (polylines): {nSketch}");
        var nSecSk = ArrCount(ext, "sectionSketches");
        if (nSecSk > 0) sb.AppendLine($"Section sketches: {nSecSk}");
        var nTrack = ArrCount(ext, "trackPoints");
        if (nTrack > 0) sb.AppendLine($"Surface track points: {nTrack}");
        if (ext.TryGetValue("surveyEventLog", out var log) && log.ValueKind == JsonValueKind.Array)
            sb.AppendLine($"Survey event log lines: {log.GetArrayLength()}");
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
        sb.AppendLine("fromStation,toStation,distance_m,clino_deg,azimuth_deg,depth_m,l_m,r_m,u_m,d_m,notes,symbol");
        foreach (var s in p.Shots)
        {
            static string Csv(string? x)
            {
                if (string.IsNullOrEmpty(x)) return "";
                var t = x.Replace("\"", "\"\"", StringComparison.Ordinal);
                if (t.Contains(',') || t.Contains('"') || t.Contains('\n')) return $"\"{t}\"";
                return t;
            }

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
