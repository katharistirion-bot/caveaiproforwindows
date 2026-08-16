using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

/// <summary>Plan-frame station position override (metres). Persisted as <c>planStationPositionOverrides</c>.</summary>
public readonly record struct PlanStationPositionOverride(
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("z")] float Z);

/// <summary>Android <c>CaveProject</c> Gson shape for backup <c>data.json</c> — explicit fields preserve 1:1 JSON mapping; overflow still in <see cref="ExtensionData"/>.</summary>
public sealed class CaveProjectDocument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("startTime")]
    public string? StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public string? EndTime { get; set; }

    /// <summary>Entrance latitude (degrees); Gson may emit a JSON number or a numeric string.</summary>
    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    /// <summary>Entrance longitude (degrees).</summary>
    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    [JsonPropertyName("alt")]
    public double Alt { get; set; }

    [JsonPropertyName("exitLat")]
    public double? ExitLat { get; set; }

    [JsonPropertyName("exitLon")]
    public double? ExitLon { get; set; }

    [JsonPropertyName("exitAlt")]
    public double? ExitAlt { get; set; }

    [JsonPropertyName("shots")]
    public List<ShotRecord> Shots { get; set; } = new();

    [JsonPropertyName("vectorLines")]
    public JsonElement? VectorLines { get; set; }

    [JsonPropertyName("rocks")]
    public JsonElement? Rocks { get; set; }

    [JsonPropertyName("fieldCatalogEntries")]
    public JsonElement? FieldCatalogEntries { get; set; }

    [JsonPropertyName("sketches")]
    public JsonElement Sketches { get; set; }

    /// <summary>Alternate plan sketch list (free-hand walls / floor detail) — Gson may use this instead of or in addition to <c>sketches</c>.</summary>
    [JsonPropertyName("sketchLayer")]
    public JsonElement SketchLayer { get; set; }

    /// <summary>Mixed plan objects: polyline strokes and/or placed symbols — survey-metre coordinates like <c>sketches</c>.</summary>
    [JsonPropertyName("mapObjects")]
    public JsonElement MapObjects { get; set; }

    [JsonPropertyName("sectionSketches")]
    public JsonElement SectionSketches { get; set; }

    [JsonPropertyName("mapSymbols")]
    public JsonElement MapSymbols { get; set; }

    [JsonPropertyName("trackPoints")]
    public JsonElement TrackPoints { get; set; }

    [JsonPropertyName("linkedLibraryCaveId")]
    public string? LinkedLibraryCaveId { get; set; }

    /// <summary>Stable per-project id (Android <c>projectId</c>) for Survey Cloud round-trip.</summary>
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    /// <summary>
    /// Cross-platform site class: <c>CAVE</c>, <c>MINE</c>, <c>POTHOLE</c>, <c>SPRING</c>
    /// (mirrors Android <c>KnownCave.type</c> / Firestore <c>caveType</c>).
    /// </summary>
    [JsonPropertyName("surveySiteType")]
    public string? SurveySiteType { get; set; }

    [JsonPropertyName("surveyEventLog")]
    public JsonElement SurveyEventLog { get; set; }

    [JsonPropertyName("visitBaselineFingerprintJson")]
    public string? VisitBaselineFingerprintJson { get; set; }

    [JsonPropertyName("excludeEntranceCoordsFromPublicPublish")]
    public bool? ExcludeEntranceCoordsFromPublicPublish { get; set; }

    [JsonPropertyName("requireVehicleParkStep")]
    public bool? RequireVehicleParkStep { get; set; }

    [JsonPropertyName("vehicleParkCoords")]
    public JsonElement VehicleParkCoords { get; set; }

    [JsonPropertyName("vehicleParkNotes")]
    public string? VehicleParkNotes { get; set; }

    [JsonPropertyName("returnCarLat")]
    public string? ReturnCarLat { get; set; }

    [JsonPropertyName("returnCarLon")]
    public string? ReturnCarLon { get; set; }

    [JsonPropertyName("returnBaseLat")]
    public string? ReturnBaseLat { get; set; }

    [JsonPropertyName("returnBaseLon")]
    public string? ReturnBaseLon { get; set; }

    [JsonPropertyName("surfaceLidarRaster")]
    public JsonElement SurfaceLidarRaster { get; set; }

    [JsonPropertyName("publicLibraryCartographyUris")]
    public JsonElement PublicLibraryCartographyUris { get; set; }

    [JsonPropertyName("cartographyTlsMeshObjUri")]
    public string? CartographyTlsMeshObjUri { get; set; }

    [JsonPropertyName("surveyArchiveSchemaVersion")]
    public string? SurveyArchiveSchemaVersion { get; set; }

    [JsonPropertyName("surveyArchivedAtMs")]
    public long? SurveyArchivedAtMs { get; set; }

    [JsonPropertyName("depthSpanAnnotations")]
    public JsonElement DepthSpanAnnotations { get; set; }

    [JsonPropertyName("brackets")]
    public JsonElement Brackets { get; set; }

    /// <summary>Schema v2: handset / OS / app build captured at export.</summary>
    [JsonPropertyName("exportDeviceContext")]
    public SurveyExportDeviceContext? ExportDeviceContext { get; set; }

    /// <summary>Schema v2: active compass / sensor calibration profile.</summary>
    [JsonPropertyName("surveyCalibrationProfile")]
    public SurveyCalibrationProfileSnapshot? SurveyCalibrationProfile { get; set; }

    /// <summary>Schema v2: on-device AI classifications for QC / office pipelines.</summary>
    [JsonPropertyName("surveyAiClassifications")]
    public List<SurveyAiClassificationTag> SurveyAiClassifications { get; set; } = new();

    /// <summary>Schema v2: per-station environmental snapshots.</summary>
    [JsonPropertyName("stationEnvironmentSnapshots")]
    public List<StationEnvironmentSnapshot> StationEnvironmentSnapshots { get; set; } = new();

    /// <summary>
    /// Cave AI / on-device geology analysis result (free-form text or markdown). Android exporters use a few
    /// different key names for this — all are mapped here so the offline GEOLOGY tab can render the analysis
    /// without re-running cloud calls.
    /// </summary>
    [JsonPropertyName("caveAiGeologyAnalysisText")]
    public string? CaveAiGeologyAnalysisText { get; set; }

    /// <inheritdoc cref="CaveAiGeologyAnalysisText"/>
    [JsonPropertyName("caveAiGeologyAnalysis")]
    public string? CaveAiGeologyAnalysis { get; set; }

    /// <inheritdoc cref="CaveAiGeologyAnalysisText"/>
    [JsonPropertyName("caveAiAnalysisText")]
    public string? CaveAiAnalysisText { get; set; }

    /// <inheritdoc cref="CaveAiGeologyAnalysisText"/>
    [JsonPropertyName("aiGeologyAnalysisText")]
    public string? AiGeologyAnalysisText { get; set; }

    /// <inheritdoc cref="CaveAiGeologyAnalysisText"/>
    [JsonPropertyName("cloudGeologyAnalysisText")]
    public string? CloudGeologyAnalysisText { get; set; }

    /// <summary>Optional raw Cave AI geology JSON payload (for forward compatibility with newer exporters).</summary>
    [JsonPropertyName("caveAiGeologyAnalysisJson")]
    public JsonElement CaveAiGeologyAnalysisJson { get; set; }

    /// <summary>
    /// X-Ray / satellite backdrop image (path inside the ZIP, sibling file, or http(s) URL) captured on Android
    /// when the user ran the cloud satellite snapshot. The deserializer is case-insensitive, so
    /// <c>xrayBackdropImageUri</c> and <c>xRayBackdropImageUri</c> both map here.
    /// </summary>
    [JsonPropertyName("xrayBackdropImageUri")]
    public string? XrayBackdropImageUri { get; set; }

    /// <inheritdoc cref="XrayBackdropImageUri"/>
    [JsonPropertyName("caveAiSatelliteImageUri")]
    public string? CaveAiSatelliteImageUri { get; set; }

    /// <inheritdoc cref="XrayBackdropImageUri"/>
    [JsonPropertyName("satelliteSnapshotImageUri")]
    public string? SatelliteSnapshotImageUri { get; set; }

    /// <inheritdoc cref="XrayBackdropImageUri"/>
    [JsonPropertyName("cloudSatelliteSnapshotUri")]
    public string? CloudSatelliteSnapshotUri { get; set; }

    /// <summary>
    /// Optional geographic bounding box of <see cref="XrayBackdropImageUri"/> (NE/SW or min/max lat/lon shape, see
    /// <see cref="XRayBackdropMetadataParser"/>). When present, the offline X-Ray tab geo-aligns the survey so each
    /// station sits on the correct terrain pixel of the satellite snapshot — exactly like the Android X-Ray.
    /// </summary>
    [JsonPropertyName("xrayBackdropImageBounds")]
    public JsonElement XrayBackdropImageBounds { get; set; }

    /// <summary>
    /// Optional list of geology / Cave AI photo references the Android exporter chose for this project (paths
    /// inside the ZIP, sibling files, http URLs, or <c>data:image</c> base64). Read alongside <see cref="Rocks"/>.
    /// </summary>
    [JsonPropertyName("caveAiGeologyPhotoUris")]
    public List<string>? CaveAiGeologyPhotoUris { get; set; }

    /// <inheritdoc cref="CaveAiGeologyPhotoUris"/>
    [JsonPropertyName("aiGeologyPhotoUris")]
    public List<string>? AiGeologyPhotoUris { get; set; }

    public int RocksCount =>
        Rocks is { ValueKind: JsonValueKind.Array } r ? r.GetArrayLength() : 0;

    public int FieldCatalogEntryCount =>
        FieldCatalogEntries is { ValueKind: JsonValueKind.Array } arr ? arr.GetArrayLength() : 0;

    /// <summary>Any Gson members not listed above (forward-compatible with newer Android builds).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public string? LoadedFromFile { get; set; }

    private Dictionary<string, PlanStationPositionOverride> _planStationPositionOverrides =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Absolute plan-frame XYZ (metres) applied after traverse reduction.
    /// Shared with Android / web as <c>planStationPositionOverrides</c>.
    /// </summary>
    [JsonPropertyName("planStationPositionOverrides")]
    public Dictionary<string, PlanStationPositionOverride> PlanStationPositionOverrides
    {
        get => _planStationPositionOverrides;
        set
        {
            _planStationPositionOverrides = new Dictionary<string, PlanStationPositionOverride>(StringComparer.OrdinalIgnoreCase);
            if (value == null)
                return;
            foreach (var kv in value)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;
                _planStationPositionOverrides[kv.Key] = kv.Value;
            }
        }
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Date) ? Name : $"{Name}  ({Date})";
}
