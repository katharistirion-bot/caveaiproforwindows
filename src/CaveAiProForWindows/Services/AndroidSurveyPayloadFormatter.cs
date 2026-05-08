using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Formats Android-exported survey metadata, shots, and remaining JSON for properties panel and diagnostics.</summary>
public static class AndroidSurveyPayloadFormatter
{
    private const int MaxJsonChars = 14_000;

    public static string FormatProjectContext(CaveProjectDocument p)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        AppendLine(sb, "Project", p.Name);
        AppendLine(sb, "Date", p.Date);
        AppendLine(sb, "Start / end", JoinNonEmpty(" · ", p.StartTime, p.EndTime));
        AppendLine(
            sb,
            "Entrance lat / lon / alt (m)",
            JoinNonEmpty(
                " · ",
                p.Lat?.ToString("0.######", inv),
                p.Lon?.ToString("0.######", inv),
                p.Alt.ToString("0.###", inv)));
        AppendLine(
            sb,
            "Exit lat / lon / alt",
            JoinNonEmpty(
                " · ",
                p.ExitLat?.ToString("0.######", inv),
                p.ExitLon?.ToString("0.######", inv),
                p.ExitAlt?.ToString("0.###", inv)));
        AppendLine(sb, "Linked library cave id", p.LinkedLibraryCaveId);
        AppendLine(sb, "Survey archive schema", p.SurveyArchiveSchemaVersion);
        if (p.SurveyArchivedAtMs is { } ms)
            AppendLine(sb, "Survey archived at (ms)", ms.ToString(inv));
        AppendLine(sb, "TLS mesh URI", p.CartographyTlsMeshObjUri);
        AppendLine(sb, "Visit baseline fingerprint", Truncate(p.VisitBaselineFingerprintJson, 400));
        if (p.ExcludeEntranceCoordsFromPublicPublish is { } ex)
            AppendLine(sb, "Exclude entrance from public publish", ex.ToString());
        if (p.RequireVehicleParkStep is { } rp)
            AppendLine(sb, "Require vehicle park step", rp.ToString());
        AppendLine(sb, "Vehicle park notes", p.VehicleParkNotes);
        if (p.VehicleParkCoords.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            AppendLine(sb, "Vehicle park coords (JSON)", Truncate(p.VehicleParkCoords.GetRawText(), 800));

        AppendSurveyArchiveV2ProjectBlock(sb, p);

        AppendBlobLine(sb, "Sketches (plan)", p.Sketches);
        AppendBlobLine(sb, "Section sketches", p.SectionSketches);
        AppendBlobLine(sb, "Map symbols", p.MapSymbols);
        AppendBlobLine(sb, "Vector lines", p.VectorLines);
        AppendBlobLine(sb, "Track points", p.TrackPoints);
        AppendBlobLine(sb, "Survey event log", p.SurveyEventLog);
        AppendBlobLine(sb, "Depth span annotations", p.DepthSpanAnnotations);
        AppendBlobLine(sb, "Brackets", p.Brackets);
        AppendBlobLine(sb, "Surface LIDAR raster", p.SurfaceLidarRaster);
        AppendBlobLine(sb, "Public library cartography URIs", p.PublicLibraryCartographyUris);
        AppendBlobLine(sb, "Rocks", p.Rocks);
        AppendBlobLine(sb, "Field catalog entries", p.FieldCatalogEntries);

        if (p.ExtensionData is { Count: > 0 } ext)
        {
            sb.AppendLine();
            sb.AppendLine("Other top-level JSON keys (ExtensionData):");
            foreach (var kv in ext.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"  • {kv.Key}: {SummarizeElement(kv.Value)}");
        }

        return TrimTo(sb.ToString(), MaxJsonChars);
    }

    public static string FormatShotDetail(CaveProjectDocument? project, ShotRecord shot)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        AppendLine(sb, "Shot id", shot.Id);
        AppendLine(sb, "From → To", $"{shot.FromStation} → {shot.ToStation}");
        AppendLine(sb, "Distance (m)", shot.Distance.ToString("0.####", inv));
        AppendLine(sb, "Azimuth / clino (°)", $"{shot.Azimuth.ToString("0.##", inv)} / {shot.Clino.ToString("0.##", inv)}");
        AppendLine(sb, "Depth (m)", shot.Depth.ToString("0.####", inv));
        AppendLine(sb, "Notes", shot.Notes);
        AppendLine(sb, "Comment", shot.Comment);
        AppendLine(sb, "Time (string)", shot.Time);
        if (shot.TimestampUtcMs is { } ts)
            AppendLine(sb, "TimestampUtcMs", ts.ToString(inv));
        AppendLine(sb, "Symbol", shot.Symbol);
        AppendLine(sb, "Splay watch", shot.HasSplayWatch.ToString());
        if (shot.Radials.Count > 0)
            AppendLine(sb, "Radials (n)", shot.Radials.Count.ToString(inv));
        AppendLine(sb, "Photos (count)", shot.Photos.Count.ToString(inv));
        if (shot.Photos.Count > 0)
            AppendLine(sb, "Photos", string.Join(" | ", shot.Photos.Take(12)) + (shot.Photos.Count > 12 ? " …" : ""));
        AppendLine(sb, "Audio memo URI", shot.AudioMemoUri);
        AppendSensorLine(sb, "Ambient BLE temp °C", shot.AmbientBleTempCelsius);
        AppendSensorLine(sb, "Manual ambient temp °C", shot.ManualAmbientTempCelsius);
        AppendSensorLine(sb, "Ambient BLE RH %", shot.AmbientBleRelativeHumidityPct);
        AppendSensorLine(sb, "Manual RH %", shot.ManualRelativeHumidityPct);
        AppendSensorLine(sb, "O₂ vol %", shot.AtmosphericO2VolPct);
        AppendSensorLine(sb, "CO₂ ppm", shot.Co2Ppm);
        AppendSensorLine(sb, "Barometric hPa", shot.BarometricPressureHpa);
        AppendShotInstrumentBlock(sb, shot);

        if (shot.ExtensionData is { Count: > 0 } ext)
        {
            sb.AppendLine();
            sb.AppendLine("Per-shot JSON (ExtensionData):");
            foreach (var kv in ext.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"  • {kv.Key}: {SummarizeElement(kv.Value)}");
        }

        if (project != null)
        {
            sb.AppendLine();
            sb.AppendLine("Project times (context):");
            AppendLine(sb, "  Project start/end", JoinNonEmpty(" · ", project.StartTime, project.EndTime));
        }

        return TrimTo(sb.ToString(), MaxJsonChars);
    }

    public static string FormatStationSurveyNotes(CaveProjectDocument? project, string stationName)
    {
        if (project == null || string.IsNullOrWhiteSpace(stationName))
            return "";
        var n = stationName.Trim();
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        var i = 0;
        foreach (var s in project.Shots)
        {
            var from = (s.FromStation ?? "").Trim();
            var to = (s.ToStation ?? "").Trim();
            if (!from.Equals(n, StringComparison.OrdinalIgnoreCase) &&
                !to.Equals(n, StringComparison.OrdinalIgnoreCase))
                continue;
            i++;
            sb.AppendLine($"— Shot #{i} ({from} → {to})");
            if (!string.IsNullOrWhiteSpace(s.Notes))
                sb.AppendLine($"  notes: {s.Notes}");
            if (!string.IsNullOrWhiteSpace(s.Comment))
                sb.AppendLine($"  comment: {s.Comment}");
            if (!string.IsNullOrWhiteSpace(s.Time))
                sb.AppendLine($"  time: {s.Time}");
            if (s.TimestampUtcMs is { } ts)
                sb.AppendLine($"  timestampUtcMs: {ts.ToString(inv)}");
            if (!string.IsNullOrWhiteSpace(s.AudioMemoUri))
                sb.AppendLine($"  audio: {s.AudioMemoUri}");
            if (s.Photos.Count > 0)
                sb.AppendLine($"  photos: {s.Photos.Count}");
            AppendCompactShotSensors(sb, s);
            sb.AppendLine();
        }

        if (i == 0)
            return "(No shot rows reference this station name.)";

        return TrimTo(sb.ToString().TrimEnd(), MaxJsonChars);
    }

    private static void AppendLine(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        sb.Append(label);
        sb.Append(": ");
        sb.AppendLine(value.Trim());
    }

    private static void AppendSensorLine(StringBuilder sb, string label, float? v)
    {
        if (v is null)
            return;
        sb.Append(label);
        sb.Append(": ");
        sb.AppendLine(v.Value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static void AppendCompactShotSensors(StringBuilder sb, ShotRecord s)
    {
        var inv = CultureInfo.InvariantCulture;
        var parts = new List<string>();
        void Add(string label, float? v)
        {
            if (v is { } x)
                parts.Add($"{label}={x.ToString("0.##", inv)}");
        }

        Add("BLE°C", s.AmbientBleTempCelsius);
        Add("manual°C", s.ManualAmbientTempCelsius);
        Add("BLE_RH%", s.AmbientBleRelativeHumidityPct);
        Add("RH%", s.ManualRelativeHumidityPct);
        Add("O₂%", s.AtmosphericO2VolPct);
        Add("CO₂ppm", s.Co2Ppm);
        Add("hPa", s.BarometricPressureHpa);
        Add("fusionQ", s.SensorFusionQuality);
        if (parts.Count == 0)
            return;
        sb.Append("  sensors: ");
        sb.AppendLine(string.Join(", ", parts));
    }

    private static void AppendBlobLine(StringBuilder sb, string label, JsonElement el)
    {
        if (el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return;
        sb.Append(label);
        sb.Append(" (");
        sb.Append(el.ValueKind.ToString());
        if (el.ValueKind == JsonValueKind.Array)
        {
            sb.Append(", len=");
            sb.Append(el.GetArrayLength());
        }

        sb.AppendLine("):");
        sb.AppendLine(Truncate(el.GetRawText(), 2_400));
    }

    private static void AppendBlobLine(StringBuilder sb, string label, JsonElement? el)
    {
        if (el is not { } e)
            return;
        AppendBlobLine(sb, label, e);
    }

    private static string JoinNonEmpty(string sep, params string?[] parts)
    {
        var list = parts.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s!.Trim()).ToList();
        return list.Count == 0 ? "" : string.Join(sep, list);
    }

    private static string SummarizeElement(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => $"array[{e.GetArrayLength()}]",
        JsonValueKind.Object => $"object({e.EnumerateObject().Count()} props)",
        JsonValueKind.String => Truncate(e.GetString() ?? "", 120),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => e.ValueKind.ToString(),
    };

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        if (s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    private static string TrimTo(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return s[..max] + "\n… (truncated for UI; use Data Inspector backup detail for full raw JSON.)";
    }

    private static void AppendSurveyArchiveV2ProjectBlock(StringBuilder sb, CaveProjectDocument p)
    {
        var inv = CultureInfo.InvariantCulture;
        if (p.ExportDeviceContext is { } dev)
        {
            sb.AppendLine("— Export device (schema v2) —");
            AppendLine(sb, "  Manufacturer", dev.Manufacturer);
            AppendLine(sb, "  Model / device", JoinNonEmpty(" · ", dev.Model, dev.Device));
            if (dev.AndroidSdkInt is { } sdk)
                AppendLine(sb, "  Android SDK", sdk.ToString(inv));
            AppendLine(sb, "  App version", JoinNonEmpty(" · ", dev.AppVersionName, dev.AppVersionCode?.ToString(inv)));
            AppendLine(sb, "  Export engine", dev.ExportEngineVersion);
            sb.AppendLine();
        }

        if (p.SurveyCalibrationProfile is { } cal)
        {
            sb.AppendLine("— Survey calibration profile —");
            AppendLine(sb, "  Profile", JoinNonEmpty(" · ", cal.ProfileId, cal.ProfileName));
            if (cal.CalibratedAtUtcMs is { } calMs)
                AppendLine(sb, "  Calibrated at (UTC ms)", calMs.ToString(inv));
            AppendSensorLine(sb, "  Declination applied (°)", cal.MagneticDeclinationAppliedDeg);
            AppendSensorLine(sb, "  Tape scale", cal.TapeCalibrationScale);
            if (cal.CompassCalibrationJson.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                AppendLine(sb, "  compassCalibrationJson", Truncate(cal.CompassCalibrationJson.GetRawText(), 1_200));
            sb.AppendLine();
        }

        if (p.SurveyAiClassifications.Count > 0)
        {
            sb.AppendLine($"— AI classifications ({p.SurveyAiClassifications.Count}) —");
            foreach (var t in p.SurveyAiClassifications.Take(24))
            {
                var bits = JoinNonEmpty(
                    " · ",
                    t.EntityType,
                    t.EntityRef,
                    t.Label,
                    t.Confidence is { } c ? $"p={c.ToString("0.##", inv)}" : null,
                    t.ModelVersion);
                if (!string.IsNullOrWhiteSpace(bits))
                    sb.AppendLine($"  • {bits}");
            }

            if (p.SurveyAiClassifications.Count > 24)
                sb.AppendLine($"  … +{p.SurveyAiClassifications.Count - 24} more");
            sb.AppendLine();
        }

        if (p.StationEnvironmentSnapshots.Count > 0)
        {
            sb.AppendLine($"— Station environment snapshots ({p.StationEnvironmentSnapshots.Count}) —");
            foreach (var st in p.StationEnvironmentSnapshots.Take(12))
            {
                var bits = $"{st.StationName}";
                if (st.CapturedAtUtcMs is { } ms)
                    bits += $" @ {ms.ToString(inv)}";
                sb.AppendLine($"  • {bits}");
                AppendSensorLine(sb, "    BLE temp °C", st.AmbientBleTempCelsius);
                AppendSensorLine(sb, "    RH %", st.AmbientBleRelativeHumidityPct ?? st.ManualRelativeHumidityPct);
            }

            if (p.StationEnvironmentSnapshots.Count > 12)
                sb.AppendLine($"  … +{p.StationEnvironmentSnapshots.Count - 12} more");
            sb.AppendLine();
        }
    }

    private static void AppendShotInstrumentBlock(StringBuilder sb, ShotRecord shot)
    {
        var inv = CultureInfo.InvariantCulture;
        if (shot.MeasurementStartedUtcMs is null && shot.MeasurementCompletedUtcMs is null &&
            shot.CompassSampleVarianceDeg2 is null && shot.ClinoSampleVarianceDeg2 is null &&
            shot.CompassStdDeg is null && shot.ClinoStdDeg is null && shot.TapeStdM is null &&
            shot.SensorFusionQuality is null && shot.HorizontalPositionAccuracyM is null &&
            shot.VerticalPositionAccuracyM is null)
            return;

        sb.AppendLine("— Instrument / QC (schema v2) —");
        if (shot.MeasurementStartedUtcMs is { } t0)
            AppendLine(sb, "  measurementStartedUtcMs", t0.ToString(inv));
        if (shot.MeasurementCompletedUtcMs is { } t1)
            AppendLine(sb, "  measurementCompletedUtcMs", t1.ToString(inv));
        AppendSensorLine(sb, "  compass sample var (deg²)", shot.CompassSampleVarianceDeg2);
        AppendSensorLine(sb, "  clino sample var (deg²)", shot.ClinoSampleVarianceDeg2);
        AppendSensorLine(sb, "  compass std (°)", shot.CompassStdDeg);
        AppendSensorLine(sb, "  clino std (°)", shot.ClinoStdDeg);
        AppendSensorLine(sb, "  tape std (m)", shot.TapeStdM);
        AppendSensorLine(sb, "  sensor fusion quality", shot.SensorFusionQuality);
        AppendSensorLine(sb, "  horiz. pos. accuracy (m)", shot.HorizontalPositionAccuracyM);
        AppendSensorLine(sb, "  vert. pos. accuracy (m)", shot.VerticalPositionAccuracyM);
        sb.AppendLine();
    }
}
