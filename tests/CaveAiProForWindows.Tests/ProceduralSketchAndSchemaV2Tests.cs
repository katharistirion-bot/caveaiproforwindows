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
