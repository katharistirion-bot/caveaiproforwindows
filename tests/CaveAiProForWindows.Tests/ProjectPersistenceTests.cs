using System.IO.Compression;
using System.Text;
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

            // SurveyCanvas parsers skip Windows-authored ink; DesignLayer hydrator restores stroke + stamp.
            Assert.AreEqual(0, SurveyStationGeometry.ParsePlanSketches(project).Count);
            Assert.AreEqual(0, SurveyStationGeometry.ParsePlanMapSymbols(project).Count);

            var restored = new Canvas { Width = 400, Height = 400 };
            var added = DesignLayerMapObjectsHydrator.TryHydrate(restored, layout, project);
            Assert.AreEqual(2, added);
            Assert.AreEqual(2, restored.Children.Count);
            Assert.IsTrue(restored.Children.OfType<Polyline>().Any());
            Assert.IsTrue(restored.Children.OfType<Viewbox>().Any());

            var strokeObj = project.MapObjects.EnumerateArray()
                .First(e => e.TryGetProperty("kind", out var k) && k.GetString() == "stroke");
            Assert.AreEqual(3, strokeObj.GetProperty("points").GetArrayLength());
            var symObj = project.MapObjects.EnumerateArray().First(e => e.TryGetProperty("symbolId", out _));
            Assert.AreEqual("rock", symObj.GetProperty("symbolId").GetString());
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
    public void SyncWindowsInkToVectorLines_merges_without_dropping_android_lines()
    {
        var project = new CaveProjectDocument
        {
            Name = "Vectors",
            VectorLines = JsonDocument.Parse(
                """
                [{"type":"WATER","viewMode":0,"points":[[0,0],[1,0]]}]
                """).RootElement.Clone(),
            MapObjects = JsonDocument.Parse(
                """
                [{"kind":"stroke","viewMode":0,"closed":false,"points":[[2,2],[3,3]],"sourceClient":"CaveAiProForWindows"}]
                """).RootElement.Clone(),
        };

        DesignLayerVectorLinesSerializer.SyncWindowsInkToVectorLines(project);
        Assert.AreEqual(JsonValueKind.Array, project.VectorLines!.Value.ValueKind);
        Assert.AreEqual(2, project.VectorLines.Value.GetArrayLength());
        Assert.AreEqual("WATER", project.VectorLines.Value[0].GetProperty("type").GetString());
        Assert.AreEqual("WINDOWS_SKETCH", project.VectorLines.Value[1].GetProperty("type").GetString());
    }

    [TestMethod]
    public void SyncWindowsInkToVectorLines_keeps_section_sheet_for_the_phone()
    {
        var project = new CaveProjectDocument
        {
            Name = "Section",
            VectorLines = JsonDocument.Parse(
                """
                [
                  {"type":"WATER","viewMode":0,"points":[{"first":0,"second":0},{"first":1,"second":0}]},
                  {"type":"WINDOWS_SECTION","viewMode":3,"sourceClient":"CaveAiProForWindows","coordinateFrame":"extendedElevation","closed":false,"points":[{"first":4,"second":5},{"first":8,"second":6}]}
                ]
                """).RootElement.Clone(),
            MapObjects = JsonDocument.Parse(
                """
                [{"kind":"stroke","viewMode":0,"closed":false,"points":[[2,2],[3,3]],"sourceClient":"CaveAiProForWindows"}]
                """).RootElement.Clone(),
        };

        DesignLayerVectorLinesSerializer.SyncWindowsInkToVectorLines(project);
        var lines = project.VectorLines!.Value.EnumerateArray().ToList();
        Assert.AreEqual(3, lines.Count);
        var section = lines.Single(el => el.GetProperty("type").GetString() == "WINDOWS_SECTION");
        Assert.AreEqual(3, section.GetProperty("viewMode").GetInt32());
        Assert.AreEqual(4, section.GetProperty("points")[0].GetProperty("first").GetDouble(), 0.01);
        Assert.AreEqual(5, section.GetProperty("points")[0].GetProperty("second").GetDouble(), 0.01);
        Assert.AreEqual("WATER", lines[0].GetProperty("type").GetString());
        Assert.IsTrue(lines.Any(el => el.GetProperty("type").GetString() == "WINDOWS_SKETCH"));
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

    [TestMethod]
    public void ProjectPersistenceService_rewrites_zip_integrity_manifest()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caveai_zip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = System.IO.Path.Combine(dir, "survey.zip");
        try
        {
            var stale = Encoding.UTF8.GetBytes("{\"name\":\"Old\"}");
            var notes = Encoding.UTF8.GetBytes("keep");
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["data.json"] = BackupZipManifestWriter.Sha256Hex(stale),
                ["notes.txt"] = BackupZipManifestWriter.Sha256Hex(notes),
            };
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                WriteZipEntry(zip, "data.json", stale);
                WriteZipEntry(zip, "notes.txt", notes);
                WriteZipEntry(zip, "integrity_manifest.json", BackupZipManifestWriter.BuildIntegrityManifest(hashes));
            }

            var project = new CaveProjectDocument
            {
                Name = "SaveTest",
                Shots = [new ShotRecord { FromStation = "A", ToStation = "B", Distance = 1, Azimuth = 0, Clino = 0 }],
            };
            ProjectPersistenceService.Save(new ProjectPersistenceService.SaveRequest
            {
                Projects = [project],
                PrimarySourcePath = zipPath,
            });

            var report = IntegrityVerifier.VerifyZip(zipPath);
            Assert.IsTrue(report.IsCompleteSuccess, string.Join("\n", report.Mismatches));
            Assert.IsTrue(report.FilesChecked >= 2);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private static void WriteZipEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }
}
