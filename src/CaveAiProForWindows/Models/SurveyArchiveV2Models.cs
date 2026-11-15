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

    /// <summary>Mean gravity residual from the last DistoX solve written into the project.</summary>
    [JsonPropertyName("meanGravityError")]
    public float? MeanGravityError { get; set; }

    /// <summary>Mean compass residual from the last DistoX solve written into the project.</summary>
    [JsonPropertyName("meanMagneticError")]
    public float? MeanMagneticError { get; set; }

    /// <summary><c>good</c>, <c>usable</c>, or <c>weak</c>.</summary>
    [JsonPropertyName("fitQuality")]
    public string? FitQuality { get; set; }

    [JsonPropertyName("sampleCount")]
    public int? SampleCount { get; set; }

    [JsonPropertyName("groupCount")]
    public int? GroupCount { get; set; }
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

    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }
}
