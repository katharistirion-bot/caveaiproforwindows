using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class ShotImportNormalizerTests
{
    [TestMethod]
    public void NormalizeProject_pulls_lrud_from_nested_extension_object()
    {
        const string json = """
            [{
              "name": "Test",
              "date": "2024-01-01",
              "alt": 0,
              "shots": [
                {
                  "fromStation": "A",
                  "toStation": "B",
                  "distance": 5,
                  "azimuth": 0,
                  "clino": 0,
                  "l": 0,
                  "r": 0,
                  "u": 0,
                  "d": 0,
                  "customBlock": { "left": 1.25, "right": 2.5, "up": 0.8, "down": 1.1 }
                }
              ]
            }]
            """;
        var projects = ExplorationDataLoader.DeserializeProjectsFromText(json);
        Assert.AreEqual(1, projects.Count);
        var shot = projects[0].Shots[0];
        Assert.AreEqual(1.25f, shot.L, 1e-4f);
        Assert.AreEqual(2.5f, shot.R, 1e-4f);
        Assert.AreEqual(0.8f, shot.U, 1e-4f);
        Assert.AreEqual(1.1f, shot.D, 1e-4f);
    }

    [TestMethod]
    public void NormalizeProject_pulls_planRadials_array_from_extension()
    {
        const string json = """
            [{
              "name": "R",
              "date": "2024-01-01",
              "alt": 0,
              "shots": [
                {
                  "fromStation": "A",
                  "toStation": "B",
                  "distance": 3,
                  "azimuth": 90,
                  "clino": 0,
                  "l": 0,
                  "r": 0,
                  "u": 0,
                  "d": 0,
                  "planRadials": [1,2,3,4,5,6,7,8,9,10,11,12]
                }
              ]
            }]
            """;
        var projects = ExplorationDataLoader.DeserializeProjectsFromText(json);
        var shot = projects[0].Shots[0];
        Assert.AreEqual(12, shot.Radials.Count);
        var (l, r, u, d) = shot.EffectivePlanLrud();
        Assert.AreEqual(10f, l, 1e-4f);
        Assert.AreEqual(4f, r, 1e-4f);
        Assert.AreEqual(1f, u, 1e-4f);
        Assert.AreEqual(7f, d, 1e-4f);
    }
}
