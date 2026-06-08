using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyCompareServiceTests
{
    [TestMethod]
    public void Compare_detects_added_and_removed_legs()
    {
        var a = new CaveProjectDocument
        {
            Name = "Cave",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 1, Azimuth = 0, Clino = 0 },
            ],
        };
        var b = new CaveProjectDocument
        {
            Name = "Cave",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 1, Azimuth = 0, Clino = 0 },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 2, Azimuth = 90, Clino = 0 },
            ],
        };

        var result = SurveyCompareService.Compare(a, b);
        Assert.AreEqual(1, result.LegsAdded);
        Assert.AreEqual(0, result.LegsRemoved);
    }
}
