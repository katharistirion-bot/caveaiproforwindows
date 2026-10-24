using System.Text.Json;
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
public sealed class SectionSheetInkTests
{
    [TestMethod]
    public void ProceduralSketchGenerator_section_uses_extended_elevation()
    {
        var project = new CaveProjectDocument
        {
            Name = "Section",
            Date = "2026-01-01",
            Alt = 80,
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A",
                    ToStation = "B",
                    Distance = 10,
                    Azimuth = 0,
                    Clino = 0,
                    U = 4,
                    D = 2,
                },
            ],
        };

        var ok = ProceduralSketchGenerator.TryGenerate(project, new ProceduralSketchOptions
        {
            IncludeMapSymbols = false,
            IncludeFieldCatalogPins = false,
            ViewMode = SurveyStationGeometry.AndroidViewModeSection,
        }, out var result);

        Assert.IsTrue(ok);
        Assert.IsTrue(result.Strokes.Any(s => s.Points.Any(p => p.Y > 70)));
        Assert.IsTrue(result.Strokes.Any(s => s.Points.Any(p => p.X > 5)));
    }

    [TestMethod]
    public void Section_vector_line_keeps_ceiling_offset_on_the_section_scene()
    {
        var project = Traverse();
        project.Alt = 80;
        project.VectorLines = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                type = "wall",
                viewMode = 1,
                points = new[] { new { first = 0f, second = 0f }, new { first = 10f, second = 0f } },
            },
            new
            {
                type = SurveyStationGeometry.WindowsSectionVectorLineType,
                viewMode = 1,
                closed = false,
                coordinateFrame = SurveyStationGeometry.ExtendedElevationCoordinateFrame,
                points = new[] { new { first = 4f, second = 5f }, new { first = 8f, second = 5f } },
            },
        });

        var strokes = SectionSheetInk.ReadStrokes(project);
        Assert.AreEqual(1, strokes.Count);
        Assert.AreEqual(85f, strokes[0].Points[0].Y, 0.01f);

        var scene = SectionSceneBuilder.TryBuild(project, SurveyVisualizationMode.Standard);
        Assert.IsNotNull(scene);
        Assert.IsTrue(scene!.WallPolylines.Any(pl =>
            pl.Type == "section" && pl.Points.Any(p => Math.Abs(p.y - 85f) < 0.05f)));
        Assert.AreEqual(0, SurveyStationGeometry.ParseSectionMapSymbols(project).Count);
    }

    [TestMethod]
    public void Section_save_round_trips_ink_and_keeps_plan_vector_lines()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var layout = new PlanCanvasSurveyLayout(0, 20, 70, 120, 0, 0, 10);
            var canvas = new Canvas { Width = 400, Height = 400 };
            canvas.Children.Add(DesignLayerProceduralApplicator.CreatePolyline(
                new SketchStrokeModel
                {
                    Closed = false,
                    Points = [(4f, 85f), (8f, 85f)],
                    Metadata = DesignLayerInkMetadata.ForUserStroke(2),
                },
                layout,
                DesignLayerInkMetadata.ForUserStroke(2)));

            var rockInk = SketchSymbolDefinitions.Get(SketchEditorSymbolKind.RockBlock);
            var stamp = new Viewbox
            {
                Width = SketchSymbolDefinitions.StampDisplaySize,
                Height = SketchSymbolDefinitions.StampDisplaySize,
                Child = new System.Windows.Shapes.Path { Data = rockInk.Geometry, Fill = Brushes.Gray },
            };
            var center = layout.WorldToCanvas(6f, 85f);
            Canvas.SetLeft(stamp, center.X - stamp.Width / 2);
            Canvas.SetTop(stamp, center.Y - stamp.Height / 2);
            canvas.Children.Add(stamp);

            var project = Traverse();
            project.Alt = 80;
            project.VectorLines = JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    type = "wall",
                    viewMode = 0,
                    points = new[] { new { first = 1f, second = 2f }, new { first = 3f, second = 4f } },
                },
            });
            project.MapSymbols = JsonSerializer.SerializeToElement(new object[]
            {
                new { icon = "water", x = 1f, y = 2f, viewMode = 0 },
            });

            Assert.IsTrue(SectionSheetInk.TryApply(project, canvas, layout));

            var lines = project.VectorLines!.Value.EnumerateArray().ToList();
            Assert.AreEqual(1, lines.Count(el => el.GetProperty("type").GetString() == "wall"));
            var sectionLines = lines.Where(SurveyStationGeometry.IsWindowsSectionVectorLine).ToList();
            Assert.AreEqual(1, sectionLines.Count);
            Assert.AreEqual(SurveyStationGeometry.AndroidViewModeLongProfile, sectionLines[0].GetProperty("viewMode").GetInt32());
            var storedY = sectionLines[0]
                .GetProperty("points")[0]
                .GetProperty("second")
                .GetDouble();
            Assert.AreEqual(5, storedY, 0.05);

            var symbols = project.MapSymbols.EnumerateArray().ToList();
            Assert.AreEqual(1, symbols.Count(el => el.GetProperty("viewMode").GetInt32() == 0));
            Assert.IsTrue(symbols.Any(el =>
                SurveyStationGeometry.IsExtendedElevationCartography(el)
                && el.GetProperty("viewMode").GetInt32() == SurveyStationGeometry.AndroidViewModeLongProfile
                && el.GetProperty("icon").GetString() == "rock"));

            var restored = new Canvas { Width = 400, Height = 400 };
            var added = DesignLayerMapObjectsHydrator.TryHydrateSection(restored, layout, project);
            Assert.AreEqual(2, added);

            project.ActiveCartographyMapId = "map-1";
            var planCanvas = new Canvas { Width = 400, Height = 400 };
            planCanvas.Children.Add(DesignLayerProceduralApplicator.CreatePolyline(
                new SketchStrokeModel
                {
                    Points = [(0f, 80f), (2f, 80f)],
                    Metadata = DesignLayerInkMetadata.ForUserStroke(1.5),
                },
                layout,
                DesignLayerInkMetadata.ForUserStroke(1.5)));
            Assert.IsTrue(DesignLayerMapObjectsSerializer.TryApplyDesignLayerToProject(project, planCanvas, layout));
            Assert.IsTrue(project.MapSymbols.EnumerateArray().Any(SurveyStationGeometry.IsExtendedElevationCartography));
            Assert.AreEqual(0, SurveyStationGeometry.ParsePlanMapSymbols(project).Count(sym =>
                string.Equals(sym.SymbolId, "rock", StringComparison.Ordinal)));
        });
    }


    [TestMethod]
    public void Section_chainage_maps_slope_tape_onto_horizontal_extended_distance()
    {
        var project = new CaveProjectDocument
        {
            Name = "Pitch",
            Date = "2026-01-01",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A",
                    ToStation = "B",
                    Distance = 10,
                    Azimuth = 0,
                    Clino = 60,
                },
            ],
        };

        Assert.AreEqual(5f, SectionSheetChainage.ToExtendedX(project, 10f), 0.05f);
        Assert.AreEqual(2.5f, SectionSheetChainage.ToExtendedX(project, 5f), 0.05f);
        Assert.AreEqual(10f, SectionSheetChainage.ToSlopeX(project, 5f), 0.05f);
    }
    private static CaveProjectDocument Traverse() =>
        new()
        {
            Name = "Section",
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
