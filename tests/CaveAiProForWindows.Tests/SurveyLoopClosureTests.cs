using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class SurveyLoopClosureTests
{
    [TestMethod]
    public void Detect_FindsLoopClosingLeg()
    {
        var project = new CaveProjectDocument
        {
            Name = "Loop cave",
            Shots =
            [
                new ShotRecord { FromStation = "1", ToStation = "2", Distance = 10, Azimuth = 0, Clino = 0 },
                new ShotRecord { FromStation = "2", ToStation = "3", Distance = 10, Azimuth = 90, Clino = 0 },
                new ShotRecord { FromStation = "3", ToStation = "1", Distance = 10, Azimuth = 225, Clino = 0 },
            ],
        };

        var loops = SurveyLoopClosureHighlighter.Detect(project);

        Assert.IsTrue(loops.Count > 0);
        Assert.IsTrue(loops.Any(l =>
            (l.FromStation == "3" && l.ToStation == "1") ||
            (l.FromStation == "1" && l.ToStation == "3")));
    }
}
