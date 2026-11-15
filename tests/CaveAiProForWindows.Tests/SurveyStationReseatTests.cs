using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class SurveyStationReseatTests
{
    [TestMethod]
    public void ShotIntoAMovedStation_StartsFromThatStation()
    {
        var project = new CaveProjectDocument
        {
            Name = "Loop",
            Alt = 100,
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10, Azimuth = 0, Clino = 0 },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 10, Azimuth = 90, Clino = 0 },
                new ShotRecord { FromStation = "C", ToStation = "A", Distance = 10, Azimuth = 180, Clino = 0 },
            ],
        };
        var moved = SurveyStationGeometry.CalculatePlanCoordinates(project);
        project.PlanStationPositionOverrides["C"] = new PlanStationPositionOverride(moved["C"].X + 2f, moved["C"].Y, moved["C"].Z);
        project.Shots.Add(new ShotRecord { FromStation = "D", ToStation = "C", Distance = 5, Azimuth = 90, Clino = 0 });

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        Assert.AreEqual(coords["C"].X - 5f, coords["D"].X, 0.08f);
        Assert.AreEqual(coords["C"].Y, coords["D"].Y, 0.08f);
    }

    [TestMethod]
    public void OverrideOnALaterStation_DoesNotDragTheEntrance()
    {
        var project = new CaveProjectDocument
        {
            Name = "Chain",
            Alt = 100,
            Shots =
            [
                new ShotRecord { FromStation = "A1", ToStation = "A2", Distance = 10, Azimuth = 90, Clino = 0 },
            ],
            PlanStationPositionOverrides =
            {
                ["a2"] = new PlanStationPositionOverride(1f, 2f, 103f),
            },
        };

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        Assert.AreEqual(0f, coords["A1"].X, 0.05f);
        Assert.AreEqual(0f, coords["A1"].Y, 0.05f);
        Assert.AreEqual(1f, coords["A2"].X, 0.05f);
        Assert.AreEqual(2f, coords["A2"].Y, 0.05f);
        Assert.IsFalse(coords.ContainsKey("a2"));
    }
}
