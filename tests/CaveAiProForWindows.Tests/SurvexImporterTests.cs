using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurvexImporterTests
{
    [TestMethod]
    public void Parse_round_trips_exporter_centerline()
    {
        var project = new CaveProjectDocument
        {
            Name = "Round Trip Cave",
            Date = "2024-06-01",
            Alt = 100.5,
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A1",
                    ToStation = "A2",
                    Distance = 5.25f,
                    Azimuth = 90,
                    Clino = -5,
                },
                new ShotRecord
                {
                    FromStation = "A2",
                    ToStation = "-",
                    Distance = 1.5f,
                    Azimuth = 45,
                    Clino = 0,
                },
            ],
        };

        var bytes = SurvexExporter.BuildSvxUtf8Bom(project, new SurvexExportOptions
        {
            FixStation = "A1",
            FixEasting = 0,
            FixNorthing = 0,
            FixElevation = 100.5,
            IncludeSplays = true,
            ProvisionalFix = true,
            IncludeLrudPassage = false,
        });
        var text = Encoding.UTF8.GetString(bytes.AsSpan(3..));
        var imported = SurvexImporter.Parse(text);

        Assert.AreEqual(1, imported.TraverseLegs);
        Assert.AreEqual(1, imported.SplayLegs);
        Assert.AreEqual(2, imported.Project.Shots.Count);
        Assert.AreEqual("A1", imported.Project.Shots[0].FromStation);
        Assert.AreEqual("A2", imported.Project.Shots[0].ToStation);
        Assert.AreEqual(5.25f, imported.Project.Shots[0].Distance, 0.001f);
        Assert.AreEqual(90f, imported.Project.Shots[0].Azimuth, 0.01f);
        Assert.AreEqual(-5f, imported.Project.Shots[0].Clino, 0.01f);
        Assert.AreEqual("-", imported.Project.Shots[1].ToStation);
        StringAssert.Contains(imported.Project.Name, "Round");
    }

    [TestMethod]
    public void Parse_converts_feet_tape_to_metres()
    {
        const string svx = """
            *begin feet_demo
            *title Feet Demo
            *units tape feet
            *units compass degrees
            *units clino degrees
            *data normal from to tape compass clino
            1 2 10 0 0
            *end feet_demo
            """;
        var imported = SurvexImporter.Parse(svx);
        Assert.AreEqual(1, imported.TraverseLegs);
        Assert.AreEqual(3.048f, imported.Project.Shots[0].Distance, 0.001f);
        Assert.IsTrue(imported.Warnings.Count >= 1);
    }

    [TestMethod]
    public void Parse_throws_when_no_legs()
    {
        Assert.ThrowsException<InvalidOperationException>(() => SurvexImporter.Parse("*begin empty\n*end empty\n"));
    }
}