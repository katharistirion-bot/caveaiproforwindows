using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.Security;

/// <summary>Ensures default <see cref="JsonElement"/> fields serialize for bundle export.</summary>
internal static class CaveProjectDocumentJsonDefaults
{
    private static readonly JsonElement EmptyArray = Create("[]");
    private static readonly JsonElement EmptyObject = Create("{}");

    public static CaveProjectDocument PrepareForSerialization(CaveProjectDocument source)
    {
        var clone = new CaveProjectDocument
        {
            Name = source.Name,
            Date = source.Date,
            StartTime = source.StartTime,
            EndTime = source.EndTime,
            Lat = source.Lat,
            Lon = source.Lon,
            Alt = source.Alt,
            ExitLat = source.ExitLat,
            ExitLon = source.ExitLon,
            ExitAlt = source.ExitAlt,
            Shots = source.Shots,
            VectorLines = source.VectorLines,
            Rocks = source.Rocks,
            FieldCatalogEntries = source.FieldCatalogEntries,
            LinkedLibraryCaveId = source.LinkedLibraryCaveId,
            VisitBaselineFingerprintJson = source.VisitBaselineFingerprintJson,
            ExcludeEntranceCoordsFromPublicPublish = source.ExcludeEntranceCoordsFromPublicPublish,
            RequireVehicleParkStep = source.RequireVehicleParkStep,
            VehicleParkNotes = source.VehicleParkNotes,
            CartographyTlsMeshObjUri = source.CartographyTlsMeshObjUri,
            ExtensionData = source.ExtensionData,
            Sketches = Coalesce(source.Sketches, EmptyArray),
            SketchLayer = Coalesce(source.SketchLayer, EmptyArray),
            MapObjects = Coalesce(source.MapObjects, EmptyArray),
            SectionSketches = Coalesce(source.SectionSketches, EmptyArray),
            MapSymbols = Coalesce(source.MapSymbols, EmptyArray),
            TrackPoints = Coalesce(source.TrackPoints, EmptyArray),
            SurveyEventLog = Coalesce(source.SurveyEventLog, EmptyArray),
            VehicleParkCoords = Coalesce(source.VehicleParkCoords, EmptyObject),
            SurfaceLidarRaster = Coalesce(source.SurfaceLidarRaster, EmptyObject),
            PublicLibraryCartographyUris = Coalesce(source.PublicLibraryCartographyUris, EmptyArray),
            DepthSpanAnnotations = Coalesce(source.DepthSpanAnnotations, EmptyArray),
            Brackets = Coalesce(source.Brackets, EmptyArray),
            CaveAiGeologyAnalysisJson = Coalesce(source.CaveAiGeologyAnalysisJson, EmptyObject),
            XrayBackdropImageBounds = Coalesce(source.XrayBackdropImageBounds, EmptyObject),
            SurveyArchiveSchemaVersion = source.SurveyArchiveSchemaVersion,
            SurveyArchivedAtMs = source.SurveyArchivedAtMs,
            ExportDeviceContext = source.ExportDeviceContext,
            SurveyCalibrationProfile = source.SurveyCalibrationProfile,
            SurveyAiClassifications = source.SurveyAiClassifications,
            StationEnvironmentSnapshots = source.StationEnvironmentSnapshots,
            CaveAiGeologyAnalysisText = source.CaveAiGeologyAnalysisText,
            CaveAiGeologyAnalysis = source.CaveAiGeologyAnalysis,
            CaveAiAnalysisText = source.CaveAiAnalysisText,
            AiGeologyAnalysisText = source.AiGeologyAnalysisText,
            CloudGeologyAnalysisText = source.CloudGeologyAnalysisText,
            XrayBackdropImageUri = source.XrayBackdropImageUri,
            CaveAiSatelliteImageUri = source.CaveAiSatelliteImageUri,
            SatelliteSnapshotImageUri = source.SatelliteSnapshotImageUri,
            CloudSatelliteSnapshotUri = source.CloudSatelliteSnapshotUri,
            CaveAiGeologyPhotoUris = source.CaveAiGeologyPhotoUris,
            AiGeologyPhotoUris = source.AiGeologyPhotoUris,
        };

        return clone;
    }

    private static JsonElement Coalesce(JsonElement value, JsonElement fallback) =>
        value.ValueKind == JsonValueKind.Undefined ? fallback : value;

    private static JsonElement Create(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
