using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

/// <summary>Android <c>exportDeviceContext</c> — device / build identity (schema v2).</summary>
public sealed class SurveyExportDeviceContext
{
    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("androidSdkInt")]
    public int? AndroidSdkInt { get; set; }

    [JsonPropertyName("appVersionName")]
    public string? AppVersionName { get; set; }

    [JsonPropertyName("appVersionCode")]
    public long? AppVersionCode { get; set; }

    [JsonPropertyName("exportEngineVersion")]
    public string? ExportEngineVersion { get; set; }
}

/// <summary>Android <c>surveyCalibrationProfile</c> — active calibration snapshot.</summary>
public sealed class SurveyCalibrationProfileSnapshot
{
    [JsonPropertyName("profileId")]
    public string? ProfileId { get; set; }

    [JsonPropertyName("profileName")]
    public string? ProfileName { get; set; }

    [JsonPropertyName("calibratedAtUtcMs")]
    public long? CalibratedAtUtcMs { get; set; }

    [JsonPropertyName("magneticDeclinationAppliedDeg")]
    public float? MagneticDeclinationAppliedDeg { get; set; }

    [JsonPropertyName("tapeCalibrationScale")]
    public float? TapeCalibrationScale { get; set; }

    /// <summary>Opaque compass / sensor calibration blob (soft iron, bias, etc.).</summary>
    [JsonPropertyName("compassCalibrationJson")]
    public JsonElement CompassCalibrationJson { get; set; }
}

/// <summary>One on-device AI label attached to a shot, station, sketch, or project.</summary>
public sealed class SurveyAiClassificationTag
{
    [JsonPropertyName("entityType")]
    public string? EntityType { get; set; }

    [JsonPropertyName("entityRef")]
    public string? EntityRef { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("labelLocale")]
    public string? LabelLocale { get; set; }

    [JsonPropertyName("confidence")]
    public float? Confidence { get; set; }

    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }

    [JsonPropertyName("modelVersion")]
    public string? ModelVersion { get; set; }
}

/// <summary>Per-station environmental sample (may differ from leg-attached shot sensors).</summary>
public sealed class StationEnvironmentSnapshot
{
    [JsonPropertyName("stationName")]
    public string StationName { get; set; } = "";

    [JsonPropertyName("capturedAtUtcMs")]
    public long? CapturedAtUtcMs { get; set; }

    [JsonPropertyName("ambientBleTempCelsius")]
    public float? AmbientBleTempCelsius { get; set; }

    [JsonPropertyName("manualAmbientTempCelsius")]
    public float? ManualAmbientTempCelsius { get; set; }

    [JsonPropertyName("ambientBleRelativeHumidityPct")]
    public float? AmbientBleRelativeHumidityPct { get; set; }

    [JsonPropertyName("manualRelativeHumidityPct")]
    public float? ManualRelativeHumidityPct { get; set; }

    [JsonPropertyName("barometricPressureHpa")]
    public float? BarometricPressureHpa { get; set; }

    [JsonPropertyName("co2Ppm")]
    public float? Co2Ppm { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}
