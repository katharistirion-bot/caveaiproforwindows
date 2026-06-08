using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class CaveViewport3DQualityTests
{
    private static CaveProjectDocument SampleProject() =>
        new()
        {
            Name = "TestCave",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A",
                    ToStation = "B",
                    Distance = 10,
                    Azimuth = 0,
                    Clino = 0,
                    L = 2f,
                    R = 1.5f,
                    U = 1.2f,
                    D = 0.8f,
                },
                new ShotRecord
                {
                    FromStation = "B",
                    ToStation = "C",
                    Distance = 8,
                    Azimuth = 90,
                    Clino = -15,
                    L = 1f,
                    R = 2f,
                    U = 2f,
                    D = 1f,
                },
            ],
        };

    [TestMethod]
    public void TubeMeshQuality_scales_ellipse_segments_and_sample_spacing()
    {
        Assert.AreEqual(20, TubeMeshQualityResolver.EllipseSegments(TubeMeshQuality.Standard));
        Assert.AreEqual(24, TubeMeshQualityResolver.EllipseSegments(TubeMeshQuality.High));
        Assert.AreEqual(0.35f, TubeMeshQualityResolver.CenterlineSampleM(TubeMeshQuality.Standard));
        Assert.AreEqual(0.22f, TubeMeshQualityResolver.CenterlineSampleM(TubeMeshQuality.High));
        Assert.AreEqual(TubeMeshQualityResolver.StandardEllipseSegments, CaveSurveyTubeMeshBuilder.DefaultEllipseSegments);
    }

    [TestMethod]
    public void High_quality_tube_has_more_vertices_than_standard()
    {
        var project = SampleProject();
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var standard = CaveSurveyTubeMeshBuilder.BuildTubeMesh(project.Shots, coords, TubeMeshQuality.Standard);
        var high = CaveSurveyTubeMeshBuilder.BuildTubeMesh(project.Shots, coords, TubeMeshQuality.High);
        Assert.IsNotNull(standard);
        Assert.IsNotNull(high);
        Assert.IsTrue(high!.Positions.Count > standard!.Positions.Count);
    }

    [TestMethod]
    public void FlyThroughPathBuilder_uses_ordered_traverse_walks()
    {
        var project = SampleProject();
        var path = CaveViewport3DFlyThroughPathBuilder.BuildPath(project);
        Assert.IsTrue(path.Count >= 3);
        Assert.IsTrue(path.All(s => s.Tangent.LengthSquared > 0.5));
        Assert.IsTrue(CaveViewport3DFlyThroughPathBuilder.TrySampleAt(path, 0.5, out var pos, out var tangent));
        Assert.IsTrue(pos.X != 0 || pos.Y != 0);
        Assert.IsTrue(tangent.LengthSquared > 0.9);
    }

    [TestMethod]
    public void TubeMeshQualityResolver_maps_Rich_cartographic_to_High()
    {
        Assert.AreEqual(
            TubeMeshQuality.High,
            TubeMeshQualityResolver.Resolve("", CartographicIntensity.Rich.ToString()));
        Assert.AreEqual(
            TubeMeshQuality.Standard,
            TubeMeshQualityResolver.Resolve("", CartographicIntensity.Balanced.ToString()));
        Assert.AreEqual(
            TubeMeshQuality.High,
            TubeMeshQualityResolver.Resolve("High", CartographicIntensity.Balanced.ToString()));
    }

    [TestMethod]
    public void Viewport3DDisplayOptions_exposes_quality_derived_mesh_params()
    {
        var high = new Viewport3DDisplayOptions(TubeQuality: TubeMeshQuality.High);
        var standard = new Viewport3DDisplayOptions(TubeQuality: TubeMeshQuality.Standard);
        Assert.AreEqual(24, high.EllipseSegments);
        Assert.AreEqual(20, standard.EllipseSegments);
        Assert.IsTrue(high.CenterlineSampleM < standard.CenterlineSampleM);
    }

    [TestMethod]
    public void Wysiwyg_export_returns_null_for_non_framework_visual()
    {
        Assert.IsNull(PlanMapRasterExporter.TryCaptureViewport3DPngWysiwyg(null!));
    }
}
