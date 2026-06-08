using System.Text.Json;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class PhaseFCompetitive3DTests
{
    private static CaveProjectDocument SampleProjectWithBranch() =>
        new()
        {
            Name = "BranchCave",
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
                    Clino = 0,
                    L = 1f,
                    R = 2f,
                    U = 1f,
                    D = 1f,
                },
                new ShotRecord
                {
                    FromStation = "B",
                    ToStation = "D",
                    Distance = 6,
                    Azimuth = 180,
                    Clino = 0,
                    L = 1.2f,
                    R = 1.2f,
                    U = 1f,
                    D = 1f,
                },
            ],
        };

    [TestMethod]
    public void MeshPlaneClipper_reduces_triangle_count_for_horizontal_cut()
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(0, 0, 0));
        mesh.Positions.Add(new Point3D(1, 0, 0));
        mesh.Positions.Add(new Point3D(0, 1, 0));
        mesh.Positions.Add(new Point3D(0, 0, 2));
        mesh.Positions.Add(new Point3D(1, 0, 2));
        mesh.Positions.Add(new Point3D(0, 1, 2));
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);
        mesh.TriangleIndices.Add(4);
        mesh.TriangleIndices.Add(5);

        var bounds = new Rect3D(0, 0, 0, 1, 1, 2);
        var cut = new Viewport3DSectionCutOptions(
            Viewport3DSectionCutAxis.HorizontalZ,
            NormalizedPosition: 0.5,
            Enabled: true);
        var clipped = Viewport3DSectionCutBuilder.ClipMesh(mesh, bounds, cut);
        Assert.IsNotNull(clipped);
        Assert.IsTrue(clipped!.TriangleIndices.Count < mesh.TriangleIndices.Count);
    }

    [TestMethod]
    public void SplayWallSurfaceBuilder_builds_fan_mesh_from_segments()
    {
        var segments = new List<(Point3D A, Point3D B)>
        {
            (new Point3D(0, 0, 0), new Point3D(2, 0, 0)),
            (new Point3D(0, 0, 0), new Point3D(0, 2, 0)),
            (new Point3D(0, 0, 0), new Point3D(-1.5, 0, 0)),
        };
        var mesh = SplayWallSurfaceBuilder.BuildStationFanMesh(segments);
        Assert.IsNotNull(mesh);
        Assert.IsTrue(mesh!.Positions.Count >= 3);
        Assert.IsTrue(mesh.TriangleIndices.Count >= 3);
    }

    [TestMethod]
    public void FlyThroughPathBuilder_covers_branch_stations()
    {
        var project = SampleProjectWithBranch();
        var path = CaveViewport3DFlyThroughPathBuilder.BuildPath(project);
        Assert.IsTrue(path.Count >= 4);
        Assert.IsTrue(CaveViewport3DFlyThroughPathBuilder.TrySampleTunnelCamera(
            path, 0.5, 0.03, out _, out var look));
        Assert.IsTrue(look.LengthSquared > 0.9);
    }

    [TestMethod]
    public void Viewport3DDisplayOptions_Competitive_uses_high_quality_and_splays()
    {
        var competitive = Viewport3DDisplayOptions.Competitive;
        Assert.AreEqual(TubeMeshQuality.High, competitive.TubeQuality);
        Assert.IsTrue(competitive.ShowSplines);
        Assert.IsFalse(competitive.ShowTopographyGrid);
        Assert.IsFalse(competitive.ShowDemSurface);
        Assert.IsTrue(competitive.ResolvedAnnotations.ShowStationNames);
        Assert.IsFalse(competitive.ResolvedAnnotations.ShowLegDetails);
    }

    [TestMethod]
    public void ResolveStrokeColorArgb_reads_hex_string()
    {
        using var doc = JsonDocument.Parse("""{"strokeColor":"#FF112233"}""");
        var argb = SurveyStationGeometry.ResolveStrokeColorArgb(doc.RootElement);
        Assert.IsNotNull(argb);
        Assert.AreEqual(0xFF, (byte)((argb!.Value >> 24) & 0xFF));
        Assert.AreEqual(0x11, (byte)((argb.Value >> 16) & 0xFF));
    }
}
