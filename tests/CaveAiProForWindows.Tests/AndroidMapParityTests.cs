using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Services.SketchAssist;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AndroidMapParityTests
{
    private static JsonElement LoadFixtureRoot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "android-vector-map-symbols-sample.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static JsonElement LoadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static CaveProjectDocument ProjectFromFixture(string fileName = "android-vector-map-symbols-sample.json")
    {
        var root = LoadFixture(fileName);
        var project = new CaveProjectDocument
        {
            Name = "ParityFixture",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 5, Azimuth = 90, Clino = 0 },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 4, Azimuth = 0, Clino = 0 },
            ],
        };
        if (root.TryGetProperty("vectorLines", out var vl))
            project.VectorLines = vl.Clone();
        if (root.TryGetProperty("mapSymbols", out var ms))
            project.MapSymbols = ms.Clone();
        if (root.TryGetProperty("mapObjects", out var mo))
            project.MapObjects = mo.Clone();
        if (root.TryGetProperty("symbolsLayer", out var sl))
        {
            project.ExtensionData = new Dictionary<string, JsonElement>
            {
                ["symbolsLayer"] = sl.Clone(),
            };
        }
        return project;
    }

    private static CaveProjectDocument ProjectFromFixture()
        => ProjectFromFixture("android-vector-map-symbols-sample.json");

    [TestMethod]
    public void ParseVectorLines_omitted_viewMode_included_for_plan_only()
    {
        var lines = JsonDocument.Parse(
            """
            [
              {"points":[[0,0],[1,1]],"type":"planSketch"},
              {"viewMode":1,"points":[[2,2],[3,3]],"type":"sectionOnly"}
            ]
            """).RootElement;
        var plan = SurveyStationGeometry.ParseVectorLinesForViewMode(lines, SurveyStationGeometry.AndroidViewModePlan);
        var section = SurveyStationGeometry.ParseVectorLinesForViewMode(lines, SurveyStationGeometry.AndroidViewModeSection);
        Assert.AreEqual(1, plan.Count);
        Assert.AreEqual("planSketch", plan[0].Type);
        Assert.AreEqual(1, section.Count);
        Assert.AreEqual("sectionOnly", section[0].Type);
    }

    [TestMethod]
    public void ParseVectorLines_fixture_filters_by_viewMode()
    {
        var project = ProjectFromFixture();
        var plan = SurveyStationGeometry.ParseVectorLinesForViewMode(
            project.VectorLines, SurveyStationGeometry.AndroidViewModePlan).ToList();
        var section = SurveyStationGeometry.ParseVectorLinesForViewMode(
            project.VectorLines, SurveyStationGeometry.AndroidViewModeSection).ToList();
        var profile = SurveyStationGeometry.ParseVectorLinesForViewMode(
            project.VectorLines, SurveyStationGeometry.AndroidViewModeLongProfile).ToList();

        Assert.AreEqual(2, plan.Count, "plan + omitted viewMode");
        Assert.AreEqual(1, section.Count);
        Assert.AreEqual("sectionSketch", section[0].Type);
        Assert.AreEqual(3, section[0].Points.Count);
        Assert.AreEqual(1, profile.Count);
        Assert.AreEqual(3, profile[0].Points.Count);
        Assert.AreEqual(7f, profile[0].Points[2].x, 1e-4f);
    }

    [TestMethod]
    public void ParseVectorLines_reads_coordinates_stroke_alias_and_geometry()
    {
        var project = ProjectFromFixture();
        var plan = SurveyStationGeometry.ParseVectorLinesForViewMode(
            project.VectorLines, SurveyStationGeometry.AndroidViewModePlan).ToList();
        var wall = plan.First(p => p.Type == "wallOutline");
        Assert.AreEqual(3, wall.Points.Count);
        Assert.IsTrue(wall.Closed);
        Assert.IsTrue(wall.PreferSharpPolyline);
        Assert.AreEqual(14f, wall.Points[2].x, 1e-4f);
    }

    [TestMethod]
    public void ParseVectorLines_pen_stroke_sets_prefer_sharp_polyline()
    {
        var lines = JsonDocument.Parse(
            """[{"stroke":"penStroke","points":[[0,0],[2,1]]}]""")
            .RootElement;
        var list = SurveyStationGeometry.ParsePlanVectorLines(lines).ToList();
        Assert.AreEqual(1, list.Count);
        Assert.IsTrue(list[0].PreferSharpPolyline);
    }

    [TestMethod]
    public void ParsePlanMapSymbols_fixture_counts_positions_and_view_modes()
    {
        var project = ProjectFromFixture();
        var plan = SurveyStationGeometry.ParsePlanMapSymbols(project).ToList();
        var section = SurveyStationGeometry.ParseSectionMapSymbols(project).ToList();

        Assert.AreEqual(3, plan.Count);
        Assert.AreEqual(2.5f, plan[0].X, 1e-4f);
        Assert.AreEqual(1.25f, plan[0].Y, 1e-4f);
        Assert.AreEqual(1.8f, plan[0].ScaleSurveyMetres!.Value, 1e-4f);
        Assert.AreEqual(1.2f, plan[0].Scale, 1e-4f);
        Assert.AreEqual("sand", plan.Single(s => s.Label == "sand").Label);
        Assert.AreEqual("__UIS__helictite__", plan.Single(s => s.IconKey == "__UIS__helictite__").IconKey);

        Assert.AreEqual(1, section.Count);
        Assert.AreEqual("ladder", section[0].SymbolId);
        Assert.AreEqual(8f, section[0].X, 1e-4f);
    }

    [TestMethod]
    public void ParsePlanMapSymbols_mixed_mapObjects_from_extension_dict()
    {
        var root = LoadFixtureRoot();
        var ext = new Dictionary<string, JsonElement>
        {
            ["mapObjects"] = root.GetProperty("mapObjects").Clone(),
        };
        var list = SurveyStationGeometry.ParsePlanMapSymbols(ext).ToList();
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("sand", list[0].Label);
        Assert.AreEqual(2f, list[0].ScaleSurveyMetres!.Value, 1e-4f);
    }

    [TestMethod]
    public void ParsePlanSketches_and_symbols_split_fixture_mapObjects()
    {
        var project = ProjectFromFixture();
        var sketches = SurveyStationGeometry.ParsePlanSketches(project).ToList();
        var symbols = SurveyStationGeometry.ParsePlanMapSymbols(project).ToList();
        Assert.AreEqual(1, sketches.Count);
        Assert.AreEqual(3, sketches[0].Points.Count);
        Assert.IsTrue(sketches[0].PreferSharpPolyline);
        Assert.IsTrue(symbols.Any(s => s.Label == "sand"));
    }

    [TestMethod]
    public void LongProfileSceneBuilder_includes_viewMode_3_vectors()
    {
        var project = ProjectFromFixture();
        var scene = LongProfileSceneBuilder.TryBuild(project);
        Assert.IsNotNull(scene);
        Assert.AreEqual(1, scene!.VectorPolylines.Count);
        Assert.IsTrue(scene.VectorPolylines[0].Points.Count >= 2);
    }

    [TestMethod]
    public void AndroidSketchSymbolKindMapper_resolves_uis_slugs_and_pool_water()
    {
        Assert.AreEqual(SketchEditorSymbolKind.StalactiteSpeleothem,
            AndroidSketchSymbolKindMapper.Resolve("helictite", null, null));
        Assert.AreEqual(SketchEditorSymbolKind.WaterPool,
            AndroidSketchSymbolKindMapper.Resolve("pool_water", null, null));
    }

    [TestMethod]
    public void ParseVectorLines_stroke_colors_fixture_reads_argb_and_hex()
    {
        var project = ProjectFromFixture("android-vector-stroke-colors.json");
        var plan = SurveyStationGeometry.ParseVectorLinesForViewMode(
            project.VectorLines, SurveyStationGeometry.AndroidViewModePlan).ToList();
        var section = SurveyStationGeometry.ParseVectorLinesForViewMode(
            project.VectorLines, SurveyStationGeometry.AndroidViewModeSection).ToList();
        var profile = SurveyStationGeometry.ParseVectorLinesForViewMode(
            project.VectorLines, SurveyStationGeometry.AndroidViewModeLongProfile).ToList();

        Assert.AreEqual(2, plan.Count);
        Assert.AreEqual(unchecked((int)0xFF0000FF), plan[0].StrokeColorArgb);
        Assert.AreEqual(unchecked((int)0xFF804020), plan[1].StrokeColorArgb);

        Assert.AreEqual(1, section.Count);
        Assert.AreEqual(0xFF4CAF50L, (long)(uint)section[0].StrokeColorArgb!.Value);

        Assert.AreEqual(1, profile.Count);
        Assert.AreEqual(unchecked((int)0xFF00FF00), profile[0].StrokeColorArgb);
    }

    [TestMethod]
    public void ParsePlanSketches_mapObjects_fixture_reads_strokeColorArgb()
    {
        var project = ProjectFromFixture("android-mapobjects-strokes-roundtrip.json");
        var sketches = SurveyStationGeometry.ParsePlanSketches(project).ToList();
        var symbols = SurveyStationGeometry.ParsePlanMapSymbols(project).ToList();

        Assert.AreEqual(2, sketches.Count);
        Assert.AreEqual(unchecked((int)0xFF0000FF), sketches[0].StrokeColorArgb);
        Assert.AreEqual(0xFF336699L, (long)(uint)sketches[1].StrokeColorArgb!.Value);
        Assert.IsTrue(sketches[1].Closed);
        Assert.AreEqual(1, symbols.Count);
        Assert.AreEqual("sand", symbols[0].SymbolId);
    }

    [TestMethod]
    public void DesignLayerMapObjectsSerializer_reparse_preserves_strokeColorArgb_on_sketches()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var layout = new PlanCanvasSurveyLayout(0, 10, 0, 10, 50, 50, 2);
            var canvas = new System.Windows.Controls.Canvas { Width = 200, Height = 200 };
            var meta = new DesignLayerInkMetadata
            {
                Source = "designLayer",
                StrokeWidthPx = 2,
                StrokeColorArgb = 0xFFAA5500,
                BrushProfile = SketchStrokeStyleDefaults.DefaultBrushProfile,
                LayerIndex = SketchStrokeStyleDefaults.UserLayerIndex,
                LayerZOrder = SketchStrokeStyleDefaults.UserLayerZOrder,
                LayerName = "User ink",
            };
            var poly = DesignLayerProceduralApplicator.CreatePolyline(
                new SketchStrokeModel
                {
                    Points = [(1f, 1f), (4f, 1f), (4f, 3f)],
                    Source = SketchStrokeStyleDefaults.DesignLayerSource,
                },
                layout,
                meta);
            canvas.Children.Add(poly);

            var project = new CaveProjectDocument { Name = "StrokeColorRoundTrip" };
            Assert.IsTrue(DesignLayerMapObjectsSerializer.TryApplyDesignLayerToProject(project, canvas, layout));

            var strokeObj = project.MapObjects.EnumerateArray().First(e =>
                e.TryGetProperty("kind", out var k) && k.GetString() == "stroke");
            Assert.AreEqual(0xFFAA5500L, strokeObj.GetProperty("strokeColorArgb").GetInt64());
            Assert.AreEqual(unchecked((int)0xFFAA5500),
                SurveyStationGeometry.ResolveStrokeColorArgb(strokeObj));

            var moOnly = new CaveProjectDocument
            {
                Name = "StrokeColorRoundTrip",
                MapObjects = project.MapObjects,
            };
            var reparsed = SurveyStationGeometry.ParsePlanSketches(moOnly).ToList();
            Assert.AreEqual(1, reparsed.Count);
            Assert.AreEqual(unchecked((int)0xFFAA5500), reparsed[0].StrokeColorArgb);
            Assert.AreEqual(3, reparsed[0].Points.Count);
        });
    }

    [TestMethod]
    public void DesignLayerMapObjectsSerializer_android_fields_reparse_via_ParsePlanMapSymbols()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var layout = new PlanCanvasSurveyLayout(0, 10, 0, 10, 50, 50, 2);
            var canvas = new System.Windows.Controls.Canvas { Width = 200, Height = 200 };
            var stamp = new System.Windows.Controls.Viewbox
            {
                Width = SketchSymbolDefinitions.StampDisplaySize,
                Height = SketchSymbolDefinitions.StampDisplaySize,
                Child = new System.Windows.Shapes.Path
                {
                    Data = SketchSymbolDefinitions.Get(SketchEditorSymbolKind.WaterPool).Geometry,
                },
            };
            System.Windows.Controls.Canvas.SetLeft(stamp, 80);
            System.Windows.Controls.Canvas.SetTop(stamp, 120);
            canvas.Children.Add(stamp);

            var project = new CaveProjectDocument { Name = "RoundTripParity" };
            Assert.IsTrue(DesignLayerMapObjectsSerializer.TryApplyDesignLayerToProject(project, canvas, layout));

            var el = project.MapObjects;
            Assert.AreEqual(JsonValueKind.Array, el.ValueKind);
            var symObj = el.EnumerateArray().First(e => e.TryGetProperty("symbolId", out _));
            Assert.AreEqual(0, symObj.GetProperty("viewMode").GetInt32());
            Assert.AreEqual("CaveAiProForWindows", symObj.GetProperty("sourceClient").GetString());
            Assert.AreEqual(1f, symObj.GetProperty("scale").GetSingle(), 1e-4f);

            var parsed = SurveyStationGeometry.ParsePlanMapSymbols(project).ToList();
            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("water", parsed[0].SymbolId);
            Assert.AreEqual(1f, parsed[0].Scale, 1e-4f);
        });
    }
}
