using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class CaveViewport3DAnnotationsTests
{
    [TestMethod]
    public void Build_includes_splay_segments_and_leg_labels()
    {
        var project = new CaveProjectDocument
        {
            Name = "Demo Cave",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "1",
                    ToStation = "2",
                    Distance = 10f,
                    Azimuth = 90,
                    Clino = -5,
                    L = 1,
                    R = 1,
                    U = 0.5f,
                    D = 0.5f,
                },
                new ShotRecord
                {
                    FromStation = "1",
                    ToStation = "-",
                    Azimuth = 0,
                    Clino = 0,
                    L = 2f,
                },
            ],
        };

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var (lines, labels) = CaveViewport3DAnnotationsBuilder.Build(project, coords);

        Assert.IsTrue(lines.SplaySegments.Count >= 1);
        Assert.IsTrue(labels.Any(l => l.Kind == Viewport3DLabelKind.Station));
        Assert.IsTrue(labels.Any(l => l.Kind == Viewport3DLabelKind.Leg));
    }
}
