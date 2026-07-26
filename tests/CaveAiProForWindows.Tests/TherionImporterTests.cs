using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class TherionImporterTests
{
    [TestMethod]
    public void Parse_round_trips_exporter_centerline()
    {
        var project = new CaveProjectDocument
        {
            Name = "Therion Trip",
            Date = "2024-06-01",
            Lat = 38.2,
            Lon = 20.6,
            Alt = 120,
            Shots =
            [
                new ShotRecord { FromStation = "A1", ToStation = "A2", Distance = 5.25f, Azimuth = 90, Clino = -5 },
                new ShotRecord { FromStation = "A2", ToStation = "-", Distance = 1.5f, Azimuth = 45, Clino = 0 },
            ],
        };
        var bytes = TherionExporter.BuildCenterlineThUtf8Bom(project);
        var text = Encoding.UTF8.GetString(bytes.AsSpan(3..));
        var imported = TherionImporter.Parse(text);
        Assert.AreEqual(1, imported.TraverseLegs);
        Assert.AreEqual(1, imported.SplayLegs);
        Assert.AreEqual("A1", imported.Project.Shots[0].FromStation);
        Assert.AreEqual("A2", imported.Project.Shots[0].ToStation);
        Assert.AreEqual(5.25f, imported.Project.Shots[0].Distance, 0.001f);
        Assert.AreEqual("-", imported.Project.Shots[1].ToStation);
        Assert.IsTrue(imported.Project.Lat is 38.2);
        StringAssert.Contains(imported.Project.Name, "Therion");
    }

    [TestMethod]
    public void Parse_throws_when_no_legs()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            TherionImporter.Parse("survey empty -title \"Empty\"\nendsurvey\n"));
    }
}