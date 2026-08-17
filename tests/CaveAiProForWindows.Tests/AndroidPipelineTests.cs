using System.Collections.Generic;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AndroidPipelineTests
{
    [TestMethod]
    public void AndroidSketchSymbolKindMapper_maps_common_labels()
    {
        Assert.AreEqual(SketchEditorSymbolKind.WaterPool, AndroidSketchSymbolKindMapper.Resolve("water_pool", null));
        Assert.AreEqual(SketchEditorSymbolKind.RockBlock, AndroidSketchSymbolKindMapper.Resolve(null, "icon_rock"));
        Assert.AreEqual(SketchEditorSymbolKind.StalactiteSpeleothem, AndroidSketchSymbolKindMapper.Resolve("unknown", null));
        Assert.AreEqual(SketchEditorSymbolKind.FlowstoneCurtain, AndroidSketchSymbolKindMapper.Resolve("flowstone", null, null));
        Assert.AreEqual(SketchEditorSymbolKind.FixedAid, AndroidSketchSymbolKindMapper.Resolve(symbolId: "rope", label: null, iconKey: null));
    }

    [TestMethod]
    public void ParsePlanMapSymbols_reads_scale_rotation_and_aliases()
    {
        var ext = new Dictionary<string, JsonElement>
        {
            ["mapSymbols"] = JsonDocument.Parse(
                """
                [
                  {"viewMode":0,"surveyX":10,"surveyY":20,"z":-5,"symbol":"Water","scale":1.5,"rotation":30},
                  {"x":1,"y":2,"type":"ROCK","iconScale":0.8}
                ]
                """).RootElement,
        };
        var list = SurveyStationGeometry.ParsePlanMapSymbols(ext).ToList();
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10f, list[0].X, 1e-5f);
        Assert.AreEqual(20f, list[0].Y, 1e-5f);
        Assert.AreEqual(-5f, list[0].Z, 1e-5f);
        Assert.AreEqual(1.5f, list[0].Scale, 1e-5f);
        Assert.AreEqual(30f, list[0].RotationDegrees, 1e-5f);
        Assert.AreEqual(1f, list[1].X, 1e-5f);
        Assert.AreEqual(0.8f, list[1].Scale, 1e-5f);
    }

    [TestMethod]
    public void ParsePlanMapSymbols_reads_symbolsLayer_symbolId_and_survey_span()
    {
        var ext = new Dictionary<string, JsonElement>
        {
            ["symbolsLayer"] = JsonDocument.Parse(
                """
                [
                  {"surveyX":3,"surveyY":4,"symbolId":"pit","scale":1,"widthSurveyM":2.5},
                  {"viewMode":0,"x":0,"y":0,"symbol":"mud"}
                ]
                """).RootElement,
        };
        var list = SurveyStationGeometry.ParsePlanMapSymbols(ext).ToList();
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("pit", list[0].SymbolId);
        Assert.AreEqual(2.5f, list[0].ScaleSurveyMetres!.Value, 1e-5f);
        Assert.AreEqual(SketchEditorSymbolKind.PitOrShaft,
            AndroidSketchSymbolKindMapper.Resolve(list[0].SymbolId, list[0].Label, list[0].IconKey));
    }

    [TestMethod]
    public void ParseSectionMapSymbols_filters_view_mode()
    {
        var doc = new CaveProjectDocument();
        doc.ExtensionData = new Dictionary<string, JsonElement>
        {
            ["sketchObjects"] = JsonDocument.Parse(
                """
                [
                  {"viewMode":0,"surveyX":1,"surveyY":2,"symbolId":"water_pool"},
                  {"viewMode":1,"surveyX":10,"surveyY":20,"symbolId":"ladder"}
                ]
                """).RootElement,
        };
        var plan = SurveyStationGeometry.ParsePlanMapSymbols(doc).ToList();
        var sec = SurveyStationGeometry.ParseSectionMapSymbols(doc).ToList();
        Assert.AreEqual(1, plan.Count);
        Assert.AreEqual("water_pool", plan[0].SymbolId);
        Assert.AreEqual(1, sec.Count);
        Assert.AreEqual("ladder", sec[0].SymbolId);
    }

    [TestMethod]
    public void ParsePlanSketches_merges_sketchLayer_coordinates_and_sets_sharp_rendering()
    {
        var doc = new CaveProjectDocument();
        doc.Sketches = JsonDocument.Parse("[]").RootElement;
        doc.SketchLayer = JsonDocument.Parse(
            """
            [{"type":"wallOutline","coordinates":[[1,2],[3,4],[5,6]]}]
            """).RootElement;
        var list = SurveyStationGeometry.ParsePlanSketches(doc).ToList();
        Assert.AreEqual(1, list.Count);
        Assert.IsTrue(list[0].PreferSharpPolyline, "Layer arrays use vertex-faithful paths.");
        Assert.AreEqual(3, list[0].Points.Count);
        Assert.AreEqual(5f, list[0].Points[2].x, 1e-5f);
    }

    [TestMethod]
    public void ParsePlanSketches_and_symbols_split_mapObjects_mixed_rows()
    {
        var doc = new CaveProjectDocument();
        doc.MapSymbols = JsonDocument.Parse("[]").RootElement;
        doc.MapObjects = JsonDocument.Parse(
            """
            [
              {"kind":"stroke","points":[[0,0],[2,0],[2,2]]},
              {"surveyX":9,"surveyY":9,"symbolId":"sand"}
            ]
            """).RootElement;
        var sketches = SurveyStationGeometry.ParsePlanSketches(doc).ToList();
        Assert.AreEqual(1, sketches.Count);
        Assert.AreEqual(3, sketches[0].Points.Count);
        var syms = SurveyStationGeometry.ParsePlanMapSymbols(doc).ToList();
        Assert.AreEqual(1, syms.Count);
        Assert.AreEqual("sand", syms[0].SymbolId);
    }

    [TestMethod]
    public void ParsePlanMapSymbols_reads_android_icon_field()
    {
        var doc = new CaveProjectDocument();
        doc.MapSymbols = JsonDocument.Parse(
            """
            [
              {"icon":"__UIS__helictite__","x":5,"y":6,"viewMode":0},
              {"icon":"💧","x":1,"y":2,"viewMode":0}
            ]
            """).RootElement;
        var list = SurveyStationGeometry.ParsePlanMapSymbols(doc).ToList();
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("__UIS__helictite__", list[0].IconKey);
        Assert.AreEqual("💧", list[1].IconKey);
    }

    [TestMethod]
    public void MapSymbolIconResolver_resolves_uis_and_emoji_icons()
    {
        var uis = new SurveyStationGeometry.PlanMapSymbol(0, 0, 0, null, 1, 0, "__UIS__rimstone__", null, null);
        var uisResolved = MapSymbolIconResolver.Resolve(uis, highContrast: false);
        Assert.AreEqual(MapSymbolIconResolver.RenderMode.VectorPath, uisResolved.Mode);
        Assert.IsNotNull(uisResolved.Geometry);
        Assert.IsTrue(uisResolved.NormalizedUisSpace);
        Assert.AreEqual("Rimstone / gour (UIS)", MapSymbolIconResolver.ExportLabel(uis));

        var emoji = new SurveyStationGeometry.PlanMapSymbol(0, 0, 0, null, 1, 0, "🌊", null, null);
        var emojiResolved = MapSymbolIconResolver.Resolve(emoji, highContrast: false);
        Assert.AreEqual(MapSymbolIconResolver.RenderMode.VectorPath, emojiResolved.Mode);
        Assert.AreEqual("Water pool (UIS)", MapSymbolIconResolver.ExportLabel(emoji));
    }

    [TestMethod]
    public void UisCaveSymbolGeometryCatalog_covers_android_palette()
    {
        Assert.IsTrue(UisCaveSymbolGeometryCatalog.AllSlugs.Count >= 38);
        Assert.IsTrue(UisCaveSymbolGeometryCatalog.TryGetGeometry("stalactite", out _));
        Assert.IsTrue(UisCaveSymbolGeometryCatalog.TryParseStoredIcon("__UIS__ladder__", out var slug));
        Assert.AreEqual("ladder", slug);
    }

    [TestMethod]
    public void ShotImportNormalizer_reads_nested_angles_object()
    {
        var s = new ShotRecord
        {
            Azimuth = 0,
            Clino = 0,
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["angles"] = JsonDocument.Parse("""{"azimuth": 12.25, "clino": -3.5}""").RootElement,
            },
        };
        var proj = new CaveProjectDocument { Shots = new List<ShotRecord> { s } };
        ShotImportNormalizer.NormalizeProjectShots(proj);
        Assert.AreEqual(12.25f, s.Azimuth, 1e-4f);
        Assert.AreEqual(-3.5f, s.Clino, 1e-4f);
    }

    [TestMethod]
    public void ImportedStationCoordinatesBootstrap_merges_explicit_xyz()
    {
        var p = new CaveProjectDocument
        {
            Name = "t",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["stationPlanCoordinates"] = JsonDocument.Parse(
                    """
                    [{"name":"A1","x":1,"y":2,"z":100},{"station":"B2","east":3,"north":4,"elevation":101}]
                    """).RootElement,
            },
        };
        ImportedStationCoordinatesBootstrap.TryApply(p);
        Assert.IsTrue(p.PlanStationPositionOverrides.TryGetValue("A1", out var a));
        Assert.AreEqual(1f, a.X, 1e-5f);
        Assert.AreEqual(2f, a.Y, 1e-5f);
        Assert.AreEqual(100f, a.Z, 1e-5f);
        Assert.IsTrue(p.PlanStationPositionOverrides.TryGetValue("B2", out var b));
        Assert.AreEqual(3f, b.X, 1e-5f);
        Assert.AreEqual(4f, b.Y, 1e-5f);
        Assert.AreEqual(101f, b.Z, 1e-5f);
    }

    [TestMethod]
    public void PlanStationPositionOverrides_roundTrip_objectMap()
    {
        const string json = """
            [{
              "name":"t",
              "date":"2026-01-01",
              "shots":[],
              "planStationPositionOverrides":{"B2":{"x":12.345,"y":-3.21,"z":101.5}}
            }]
            """;
        var loaded = ExplorationDataLoader.DeserializeProjectsFromText(json);
        Assert.AreEqual(1, loaded.Count);
        Assert.IsTrue(loaded[0].PlanStationPositionOverrides.TryGetValue("b2", out var o));
        Assert.AreEqual(12.345f, o.X, 1e-4f);
        Assert.AreEqual(-3.21f, o.Y, 1e-4f);
        Assert.AreEqual(101.5f, o.Z, 1e-4f);
        var blob = JsonSerializer.Serialize(loaded[0].PlanStationPositionOverrides);
        StringAssert.Contains(blob, "12.345");
        var dict = JsonSerializer.Deserialize<Dictionary<string, PlanStationPositionOverride>>(blob);
        Assert.IsNotNull(dict);
        Assert.IsTrue(dict!.TryGetValue("B2", out var o2));
        Assert.AreEqual(12.345f, o2.X, 1e-4f);
    }

    [TestMethod]
    public void ImportedStationCoordinatesBootstrap_merges_planStationPositionOverrides_objectMap()
    {
        var p = new CaveProjectDocument
        {
            Name = "t",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["planStationPositionOverrides"] = JsonDocument.Parse(
                    """{"B2":{"x":12.3,"y":-3.2,"z":101.5}}""").RootElement,
            },
        };
        ImportedStationCoordinatesBootstrap.TryApply(p);
        Assert.IsFalse(p.ExtensionData!.ContainsKey("planStationPositionOverrides"));
        Assert.IsTrue(p.PlanStationPositionOverrides.TryGetValue("B2", out var b));
        Assert.AreEqual(12.3f, b.X, 1e-4f);
        Assert.AreEqual(-3.2f, b.Y, 1e-4f);
        Assert.AreEqual(101.5f, b.Z, 1e-4f);
    }

    [TestMethod]
    public void ExplorationDataLoader_deserializes_survey_archive_v2_enrichment()
    {
        const string json = """
            [{
              "name":"TestCave",
              "date":"2026-01-01",
              "shots":[],
              "surveyArchiveSchemaVersion":"2",
              "exportDeviceContext":{"manufacturer":"ACME","model":"X1","androidSdkInt":34,"appVersionName":"1.9.0"},
              "surveyCalibrationProfile":{"profileId":"p1","profileName":"Field A","magneticDeclinationAppliedDeg":2.5,
                "compassCalibrationJson":{"biasMicroT":[1,2,3]}},
              "surveyAiClassifications":[{"entityType":"project","label":"karst corridor","confidence":0.88}],
              "stationEnvironmentSnapshots":[{"stationName":"A1","capturedAtUtcMs":1000,"ambientBleTempCelsius":12.3}],
              "sketches":[{"points":[],"strokeWidthPx":4,"strokeColorArgb":4294901760,"layerIndex":0,"textAnnotations":[]}],
              "sectionSketches":[],
              "mapSymbols":[],
              "trackPoints":[]
            }]
            """;
        var list = ExplorationDataLoader.DeserializeProjectsFromText(json);
        Assert.AreEqual(1, list.Count);
        var p = list[0];
        Assert.AreEqual("2", p.SurveyArchiveSchemaVersion);
        Assert.IsNotNull(p.ExportDeviceContext);
        Assert.AreEqual("ACME", p.ExportDeviceContext!.Manufacturer);
        Assert.AreEqual(34, p.ExportDeviceContext.AndroidSdkInt);
        Assert.IsNotNull(p.SurveyCalibrationProfile);
        Assert.AreEqual("Field A", p.SurveyCalibrationProfile!.ProfileName);
        Assert.AreEqual(JsonValueKind.Object, p.SurveyCalibrationProfile.CompassCalibrationJson.ValueKind);
        Assert.AreEqual(1, p.SurveyAiClassifications.Count);
        Assert.AreEqual("karst corridor", p.SurveyAiClassifications[0].Label);
        Assert.AreEqual(1, p.StationEnvironmentSnapshots.Count);
        Assert.AreEqual("A1", p.StationEnvironmentSnapshots[0].StationName);
        Assert.AreEqual(12.3f, p.StationEnvironmentSnapshots[0].AmbientBleTempCelsius!.Value, 1e-3f);
        Assert.IsTrue(p.Sketches.GetRawText().Contains("strokeWidthPx", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ExplorationDataLoader_coerces_numeric_strings_for_schema_and_nested_ids()
    {
        const string json = """
            [{
              "name":"N","date":"d",
              "surveyArchiveSchemaVersion":2,
              "linkedLibraryCaveId":9001,
              "shots":[{"fromStation":"A","toStation":"B","distance":1,"azimuth":0,"clino":0,"l":0,"r":0,"u":0,"d":0,"id":42}],
              "exportDeviceContext":{"appVersionCode":99,"exportEngineVersion":1.25},
              "surveyCalibrationProfile":{"profileId":7,"profileName":"P","compassCalibrationJson":{}},
              "surveyAiClassifications":[{"entityRef":100,"modelVersion":3,"label":"x"}],
              "sketches":[],"sectionSketches":[],"mapSymbols":[],"trackPoints":[],
              "surveyEventLog":[],"vehicleParkCoords":[],"surfaceLidarRaster":[],"publicLibraryCartographyUris":[],
              "depthSpanAnnotations":[],"brackets":[]
            }]
            """;
        var list = ExplorationDataLoader.DeserializeProjectsFromText(json);
        Assert.AreEqual(1, list.Count);
        var p = list[0];
        Assert.AreEqual("2", p.SurveyArchiveSchemaVersion);
        Assert.AreEqual("9001", p.LinkedLibraryCaveId);
        Assert.AreEqual("42", p.Shots[0].Id);
        Assert.IsNotNull(p.ExportDeviceContext);
        Assert.AreEqual("1.25", p.ExportDeviceContext!.ExportEngineVersion);
        Assert.IsNotNull(p.SurveyCalibrationProfile);
        Assert.AreEqual("7", p.SurveyCalibrationProfile!.ProfileId);
        Assert.AreEqual(1, p.SurveyAiClassifications.Count);
        Assert.AreEqual("100", p.SurveyAiClassifications[0].EntityRef);
        Assert.AreEqual("3", p.SurveyAiClassifications[0].ModelVersion);
    }

    [TestMethod]
    public void CaveProjectDocument_deserializes_exitAlt_as_number_and_latLon_flexible()
    {
        const string json = """
            [{
              "name":"X","date":"d","alt":100,
              "lat":"37.5","lon":"22.25",
              "exitLat":38,"exitLon":23,"exitAlt":150.5,
              "shots":[],"sketches":[],"sectionSketches":[],"mapSymbols":[],"trackPoints":[],
              "surveyEventLog":[],"vehicleParkCoords":[],"surfaceLidarRaster":[],"publicLibraryCartographyUris":[],
              "depthSpanAnnotations":[],"brackets":[]
            }]
            """;
        var list = ExplorationDataLoader.DeserializeProjectsFromText(json);
        Assert.AreEqual(1, list.Count);
        var p = list[0];
        Assert.AreEqual(37.5, p.Lat!.Value, 1e-9);
        Assert.AreEqual(22.25, p.Lon!.Value, 1e-9);
        Assert.AreEqual(38.0, p.ExitLat!.Value, 1e-9);
        Assert.AreEqual(23.0, p.ExitLon!.Value, 1e-9);
        Assert.AreEqual(150.5, p.ExitAlt!.Value, 1e-9);
    }

    [TestMethod]
    public void CaveProjectDocument_deserializes_empty_string_exit_gps_as_null()
    {
        const string json = """
            [{
              "name":"X","date":"d","alt":100,
              "lat":"","lon":"  ",
              "exitLat":"","exitLon":"","exitAlt":"",
              "shots":[],"sketches":[],"sectionSketches":[],"mapSymbols":[],"trackPoints":[],
              "surveyEventLog":[],"vehicleParkCoords":[],"surfaceLidarRaster":[],"publicLibraryCartographyUris":[],
              "depthSpanAnnotations":[],"brackets":[]
            }]
            """;
        var list = ExplorationDataLoader.DeserializeProjectsFromText(json);
        Assert.AreEqual(1, list.Count);
        var p = list[0];
        Assert.IsNull(p.Lat);
        Assert.IsNull(p.Lon);
        Assert.IsNull(p.ExitLat);
        Assert.IsNull(p.ExitLon);
        Assert.IsNull(p.ExitAlt);
    }

    [TestMethod]
    public void CaveProjectDocument_deserializes_non_scalar_exitLat_as_null_without_throw()
    {
        const string json = """
            [{
              "name":"X","date":"d","alt":100,
              "exitLat":true,"exitLon":{},"exitAlt":[],
              "shots":[],"sketches":[],"sectionSketches":[],"mapSymbols":[],"trackPoints":[],
              "surveyEventLog":[],"vehicleParkCoords":[],"surfaceLidarRaster":[],"publicLibraryCartographyUris":[],
              "depthSpanAnnotations":[],"brackets":[]
            }]
            """;
        var list = ExplorationDataLoader.DeserializeProjectsFromText(json);
        Assert.AreEqual(1, list.Count);
        var p = list[0];
        Assert.IsNull(p.ExitLat);
        Assert.IsNull(p.ExitLon);
        Assert.IsNull(p.ExitAlt);
    }

    [TestMethod]
    public void ShotRecord_deserializes_instrument_qc_fields()
    {
        const string json = """
            {"fromStation":"A","toStation":"B","distance":5,"azimuth":90,"clino":0,"l":0,"r":0,"u":0,"d":0,
             "compassSampleVarianceDeg2":40,"clinoSampleVarianceDeg2":1,"measurementStartedUtcMs":10,"measurementCompletedUtcMs":20}
            """;
        var s = JsonSerializer.Deserialize<ShotRecord>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(s);
        Assert.AreEqual(40f, s!.CompassSampleVarianceDeg2!.Value, 1e-5f);
        Assert.AreEqual(10L, s.MeasurementStartedUtcMs);
    }
}
