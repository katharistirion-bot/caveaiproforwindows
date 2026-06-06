using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Services.SketchAssist;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class ProjectPersistenceTests
{
    [TestMethod]
    public void DesignLayerMapObjectsSerializer_round_trips_strokes_and_symbols()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var layout = new PlanCanvasSurveyLayout(0, 100, 0, 100, 50, 50, 2);
            var canvas = new Canvas { Width = 400, Height = 400 };

            canvas.Children.Add(new Polyline
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Points = new PointCollection { new(50, 250), new(150, 250), new(150, 150) },
            });

            var rockInk = SketchSymbolDefinitions.Get(SketchEditorSymbolKind.RockBlock);
            var stamp = new Viewbox
            {
                Width = SketchSymbolDefinitions.StampDisplaySize,
                Height = SketchSymbolDefinitions.StampDisplaySize,
                Child = new System.Windows.Shapes.Path { Data = rockInk.Geometry, Fill = Brushes.Gray },
            };
            Canvas.SetLeft(stamp, 90);
            Canvas.SetTop(stamp, 190);
            canvas.Children.Add(stamp);

            var project = new CaveProjectDocument { Name = "RoundTrip" };
            Assert.IsTrue(DesignLayerMapObjectsSerializer.TryApplyDesignLayerToProject(project, canvas, layout));
            Assert.AreEqual(JsonValueKind.Array, project.MapObjects.ValueKind);
            Assert.AreEqual(2, project.MapObjects.GetArrayLength());
            Assert.IsTrue(project.ExtensionData!.ContainsKey("windowsLastEditedAtMs"));
            Assert.AreEqual(JsonValueKind.Array, project.Sketches.ValueKind);
            Assert.AreEqual(1, project.Sketches.GetArrayLength());

            var sketches = SurveyStationGeometry.ParsePlanSketches(project).ToList();
            Assert.AreEqual(2, sketches.Count);
            Assert.AreEqual(3, sketches[0].Points.Count);

            var symbols = SurveyStationGeometry.ParsePlanMapSymbols(project).ToList();
            Assert.AreEqual(1, symbols.Count);
            Assert.AreEqual("rock", symbols[0].SymbolId);
        });
    }

    [TestMethod]
    public void SketchEditorSymbolKindExporter_maps_palette_to_android_ids()
    {
        Assert.AreEqual("water", SketchEditorSymbolKindExporter.ToSymbolId(SketchEditorSymbolKind.WaterPool));
        Assert.AreEqual("sand", SketchEditorSymbolKindExporter.ToSymbolId(SketchEditorSymbolKind.SandMudFloor));
        Assert.AreEqual("stalactite", SketchEditorSymbolKindExporter.ToSymbolId(SketchEditorSymbolKind.StalactiteSpeleothem));
    }

    [TestMethod]
    public void MergeWindowsSketches_preserves_android_strokes()
    {
        var existing = JsonDocument.Parse(
            """
            [
              {"viewMode":0,"closed":false,"points":[[0,0],[1,1]],"sourceClient":"CaveAiProAndroid"},
              {"viewMode":0,"closed":false,"points":[[9,9]],"sourceClient":"CaveAiProForWindows"}
            ]
            """).RootElement;
        var windows = JsonDocument.Parse(
            """
            [{"viewMode":0,"closed":false,"points":[[2,2],[3,3]],"sourceClient":"CaveAiProForWindows"}]
            """).RootElement;

        var merged = DesignLayerMapObjectsSerializer.MergeWindowsSketches(existing, windows);
        Assert.AreEqual(2, merged.GetArrayLength());
        Assert.AreEqual("CaveAiProAndroid", merged[0].GetProperty("sourceClient").GetString());
    }

    [TestMethod]
    public void ProjectPersistenceService_overwrites_json_backup()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caveai_save_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var jsonPath = System.IO.Path.Combine(dir, "survey.json");
        try
        {
            var project = new CaveProjectDocument
            {
                Name = "SaveTest",
                Shots = [new ShotRecord { FromStation = "A", ToStation = "B", Distance = 1, Azimuth = 0, Clino = 0 }],
            };
            File.WriteAllText(jsonPath,
                """
                [{"name":"SaveTest","shots":[{"fromStation":"A","toStation":"B","distance":1,"azimuth":0,"clino":0}]}]
                """);

            project.MapObjects = JsonDocument.Parse(
                """[{"kind":"stroke","viewMode":0,"closed":false,"points":[[0,0],[1,1]],"sourceClient":"CaveAiProForWindows"}]""")
                .RootElement.Clone();

            ProjectPersistenceService.Save(new ProjectPersistenceService.SaveRequest
            {
                Projects = [project],
                PrimarySourcePath = jsonPath,
            });

            var reloaded = ExplorationDataLoader.LoadFromJsonFile(jsonPath);
            Assert.AreEqual(1, reloaded.Count);
            Assert.AreEqual(JsonValueKind.Array, reloaded[0].MapObjects.ValueKind);
            Assert.AreEqual(1, reloaded[0].MapObjects.GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
