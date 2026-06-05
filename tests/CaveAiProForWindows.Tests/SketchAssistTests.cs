using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SketchAssist;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SketchAssistTests
{
    [TestMethod]
    public void CanvasPointToSurvey_uses_plan_layout_inverse()
    {
        var layout = new PlanCanvasSurveyLayout(0, 100, 0, 100, 50, 50, 2);
        var (wx0, wy0) = DesignLayerSurveyConverter.CanvasPointToSurvey(50, 250, layout);
        var (wx1, wy1) = DesignLayerSurveyConverter.CanvasPointToSurvey(150, 250, layout);

        Assert.AreEqual(0f, wx0, 1e-3f);
        Assert.AreEqual(0f, wy0, 1e-3f);
        Assert.AreEqual(50f, wx1, 1e-3f);
        Assert.AreEqual(0f, wy1, 1e-3f);
    }

    [TestMethod]
    public void CanvasPointToSurvey_round_trips_through_WorldToCanvas()
    {
        var layout = new PlanCanvasSurveyLayout(-10, 90, 5, 105, 40, 60, 3);
        const double cx = 120;
        const double cy = 200;
        var (wx, wy) = DesignLayerSurveyConverter.CanvasPointToSurvey(cx, cy, layout);
        var back = DesignLayerSurveyConverter.SurveyPointToCanvas(wx, wy, layout);
        Assert.AreEqual(cx, back.X, 1e-3);
        Assert.AreEqual(cy, back.Y, 1e-3);
    }

    [TestMethod]
    public void DesignLayerSurveyConverter_maps_polyline_canvas_points_to_survey_metres()
    {
        RunSta(() =>
        {
            var layout = new PlanCanvasSurveyLayout(0, 100, 0, 100, 50, 50, 2);
            var canvas = new Canvas { Width = 400, Height = 400 };
            var poly = new Polyline
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Points = new PointCollection { new(50, 250), new(150, 250) },
            };
            canvas.Children.Add(poly);

            var (strokes, _) = DesignLayerSurveyConverter.ExtractUserGeometry(canvas, layout);
            Assert.AreEqual(1, strokes.Count);
            Assert.AreEqual(2, strokes[0].Points.Count);
            Assert.AreEqual(0f, strokes[0].Points[0].Y, 1e-3f);
            Assert.AreEqual(0f, strokes[0].Points[0].X, 1e-3f);
            Assert.AreEqual(50f, strokes[0].Points[1].X, 1e-3f);
        });
    }

    [TestMethod]
    public void SketchAssistInputBuilder_builds_session_from_minimal_traverse()
    {
        RunSta(() =>
        {
            var project = new CaveProjectDocument
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

            var canvas = new Canvas { Width = 960, Height = 640 };
            var session = SketchAssistInputBuilder.TryBuild(project, canvas, 960, 640);
            Assert.IsNotNull(session);
            Assert.AreEqual("Test", session!.Document.ProjectName);
            Assert.IsTrue(session.Scene.TraverseSegments.Count > 0);
            Assert.AreEqual(960, session.SourceCanvasWidth);
        });
    }

    [TestMethod]
    public void StructureMaskExporter_emits_non_empty_png_for_traverse_project()
    {
        RunSta(() =>
        {
            var project = new CaveProjectDocument
            {
                Name = "Mask",
                Date = "d",
                Shots =
                [
                    new ShotRecord
                    {
                        FromStation = "1",
                        ToStation = "2",
                        Distance = 5,
                        Azimuth = 90,
                        Clino = 0,
                    },
                ],
            };

            var session = SketchAssistInputBuilder.TryBuild(project, new Canvas { Width = 960, Height = 640 }, 960, 640);
            Assert.IsNotNull(session);

            var png = SketchAssistStructureMaskExporter.TryExportStructureMaskPng(session!);
            Assert.IsNotNull(png);
            Assert.IsTrue(png!.Length > 256);
            Assert.AreEqual(0x89, png[0]);
            Assert.AreEqual(0x50, png[1]);
        });
    }

    [TestMethod]
    public void StructureMaskExporter_includes_user_stroke_in_document()
    {
        RunSta(() =>
        {
            var project = new CaveProjectDocument
            {
                Name = "Ink",
                Shots =
                [
                    new ShotRecord
                    {
                        FromStation = "A",
                        ToStation = "B",
                        Distance = 8,
                        Azimuth = 0,
                        Clino = 0,
                    },
                ],
            };

            var session = SketchAssistInputBuilder.TryBuild(project, new Canvas { Width = 960, Height = 640 }, 960, 640);
            Assert.IsNotNull(session);

            session!.Document.UserStrokes.Add(new SketchStrokeModel
            {
                Source = "test",
                Points = [(0f, 0f), (5f, 0f), (5f, 3f)],
            });

            var png = SketchAssistStructureMaskExporter.TryExportStructureMaskPng(session);
            Assert.IsNotNull(png);
            Assert.IsTrue(png!.Length > 256);
        });
    }

    private static void RunSta(Action action) => SketchAssistTestsRunSta.Run(action);
}
