using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Services.SketchAssist;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class ProceduralSketchAndSchemaV2Tests
{
    [TestMethod]
    public void ProceduralSketchGenerator_builds_lrud_wall_strokes_from_traverse()
    {
        var project = MinimalTraverseProject();
        project.Shots[0].L = 2;
        project.Shots[0].R = 1.5f;

        var ok = ProceduralSketchGenerator.TryGenerate(project, new ProceduralSketchOptions
        {
            IncludeMapSymbols = false,
            IncludeFieldCatalogPins = false,
        }, out var result);

        Assert.IsTrue(ok);
        Assert.IsTrue(result.Strokes.Count > 0);
        Assert.IsTrue(result.Strokes.All(s =>
            string.Equals(s.Source, SketchStrokeStyleDefaults.ProceduralSource, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ProceduralSketchGenerator_includes_splay_segments_when_requested()
    {
        var project = MinimalTraverseProject();
        project.Shots.Add(new ShotRecord
        {
            FromStation = "A",
            ToStation = "-",
            Distance = 3,
            Azimuth = 90,
            Clino = 0,
        });

        var ok = ProceduralSketchGenerator.TryGenerate(project, new ProceduralSketchOptions
        {
            IncludeWallOutlines = false,
            IncludeMapSymbols = false,
            IncludeFieldCatalogPins = false,
            IncludeSplayOutlines = true,
        }, out var result);

        Assert.IsTrue(ok);
        Assert.IsTrue(result.Strokes.Any(s => s.Points.Count == 2));
    }

    [TestMethod]
    public void ProceduralSketchGenerator_skips_persisted_mapObjects_sketch_walls()
    {
        var project = MinimalTraverseProject();
        project.Shots[0].L = 2;
        project.Shots[0].R = 1.5f;
        project.MapObjects = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                kind = "stroke",
                viewMode = 0,
                closed = false,
                points = new[] { new[] { 0.0, 0.0 }, new[] { 5.0, 0.0 }, new[] { 5.0, 3.0 } },
            },
        });

        Assert.IsTrue(ProceduralSketchGenerator.TryGenerate(project, new ProceduralSketchOptions
        {
            IncludeMapSymbols = false,
            IncludeFieldCatalogPins = false,
        }, out var result));

        Assert.IsTrue(result.Strokes.Count > 0);
        Assert.IsTrue(result.Strokes.All(s => s.Points.Count >= 3 || s.Points.Count == 2));
    }

    [TestMethod]
    public void DesignLayerSurveyConverter_ignores_procedural_preview_when_extracting()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var layout = new PlanCanvasSurveyLayout(0, 10, 0, 10, 50, 50, 5);
            var canvas = new System.Windows.Controls.Canvas { Width = 400, Height = 400 };
            canvas.Children.Add(DesignLayerProceduralApplicator.CreatePolyline(
                new SketchStrokeModel { Points = [(0f, 0f), (5f, 0f)], Source = SketchStrokeStyleDefaults.ProceduralSource },
                layout,
                DesignLayerInkMetadata.ForProceduralWall()));

            var (strokes, _) = DesignLayerSurveyConverter.ExtractUserGeometry(canvas, layout);
            Assert.AreEqual(0, strokes.Count);
        });
    }

    [TestMethod]
    public void SketchStrokeStyleMerger_round_trips_strokeWidthPx()
    {
        var map = new Dictionary<string, object?>
        {
            ["points"] = new[] { new[] { 0.0, 0.0 } },
        };
        SketchStrokeStyleMerger.MergeInto(map, DesignLayerInkMetadata.ForUserStroke(2.5));
        Assert.AreEqual(2.5, map["strokeWidthPx"]);

        var json = JsonSerializer.SerializeToElement(map);
        var meta = SketchStrokeStyleMerger.TryParseFromJson(json);
        Assert.IsNotNull(meta);
        Assert.AreEqual(2.5, meta!.StrokeWidthPx!.Value, 1e-6);
    }

    [TestMethod]
    public void DesignLayerMapObjectsSerializer_emits_schema_v2_stroke_fields()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var layout = new PlanCanvasSurveyLayout(0, 10, 0, 10, 50, 50, 5);
            var canvas = new System.Windows.Controls.Canvas { Width = 400, Height = 400 };
            var poly = DesignLayerProceduralApplicator.CreatePolyline(
                new SketchStrokeModel
                {
                    Points = [(0f, 0f), (5f, 0f)],
                    Source = SketchStrokeStyleDefaults.DesignLayerSource,
                },
                layout,
                DesignLayerInkMetadata.ForUserStroke(2));
            canvas.Children.Add(poly);

            var el = DesignLayerMapObjectsSerializer.SerializeDesignLayer(canvas, layout);
            Assert.AreEqual(JsonValueKind.Array, el.ValueKind);
            var stroke = el[0];
            Assert.IsTrue(stroke.TryGetProperty("strokeWidthPx", out _));
            Assert.IsTrue(stroke.TryGetProperty("strokeColorArgb", out _));
            Assert.IsTrue(stroke.TryGetProperty("brushProfile", out _));
        });
    }

    [TestMethod]
    public void CloudPublishService_serialize_applies_normalizer_and_schema_version()
    {
        var project = MinimalTraverseProject();
        project.Sketches = default;

        var bytes = CloudPublishService.SerializeProjectJsonUtf8(project);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.IsTrue(text.Contains("\"surveyArchiveSchemaVersion\"", StringComparison.Ordinal));
        Assert.AreEqual("2", project.SurveyArchiveSchemaVersion);
    }

    [TestMethod]
    public void SketchSymbolPaletteCatalog_covers_all_editor_symbol_kinds()
    {
        var catalogKinds = SketchSymbolPaletteCatalog.All.Select(e => e.Kind).ToHashSet();
        foreach (SketchEditorSymbolKind kind in Enum.GetValues(typeof(SketchEditorSymbolKind)))
            Assert.IsTrue(catalogKinds.Contains(kind), $"Missing palette entry for {kind}");
    }

    [TestMethod]
    public void AndroidSketchSymbolKindMapper_resolves_uis_slugs()
    {
        Assert.AreEqual(SketchEditorSymbolKind.AvenShaftUp, AndroidSketchSymbolKindMapper.Resolve("aven", null, null));
        Assert.AreEqual(SketchEditorSymbolKind.BatGuano, AndroidSketchSymbolKindMapper.Resolve("guano", null, null));
        Assert.AreEqual(SketchEditorSymbolKind.ArchaeologyBones, AndroidSketchSymbolKindMapper.Resolve("bones", null, null));
    }

    [TestMethod]
    public void Procedural_lrud_walls_use_solid_uis_wall_profile()
    {
        var meta = DesignLayerInkMetadata.ForProceduralWall();
        Assert.AreEqual(SketchWallInkProfiles.Wall, meta.BrushProfile);
        Assert.IsNull(SketchWallInkStyle.ResolveDash(meta.BrushProfile));
    }

    [TestMethod]
    public void Procedural_splay_strokes_tagged_wall_estimated()
    {
        var project = MinimalTraverseProject();
        project.Shots.Add(new ShotRecord
        {
            FromStation = "A",
            ToStation = "-",
            Distance = 3,
            Azimuth = 90,
            Clino = 0,
        });

        Assert.IsTrue(ProceduralSketchGenerator.TryGenerate(project, new ProceduralSketchOptions
        {
            IncludeWallOutlines = false,
            IncludeMapSymbols = false,
            IncludeFieldCatalogPins = false,
            IncludeSplayOutlines = true,
        }, out var result));

        var splay = result.Strokes.First(s => s.Points.Count == 2);
        Assert.AreEqual(SketchWallInkProfiles.WallEstimated, splay.Metadata?.BrushProfile);
    }

    private static CaveProjectDocument MinimalTraverseProject() =>
        new()
        {
            Name = "Test",
            Date = "2026-01-01",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A",
                    ToStation = "B",
                    Distance = 10,
                    Azimuth = 0,
                    Clino = 0,
                },
            ],
        };
}
