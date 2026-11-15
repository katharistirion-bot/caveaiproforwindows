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

    [TestMethod]
    public void Parse_imports_equates_fix_flags_and_comment_nplot()
    {
        const string svx = """
            *begin meta_cave
            *title Meta Cave
            *units tape metres
            *units compass degrees
            *units clino degrees
            *equate A1 A_alias
            *fix A1 0 0 100
            *fix C1 10 0 100
            *data normal from to tape compass clino
            *flags surface
            A1 A2 5 0 0
            *flags not surface
            *flags duplicate
            A2 A3 4 90 0
            *flags not duplicate
            A3 A4 3 180 0 ; NOPLOT DONTADJUST
            *end meta_cave
            """;
        var imported = SurvexImporter.Parse(svx);
        Assert.AreEqual(1, imported.Project.StationEquates!.Count);
        Assert.AreEqual("A1", imported.Project.StationEquates[0].StationA);
        Assert.AreEqual("A_alias", imported.Project.StationEquates[0].StationB);
        Assert.AreEqual(100f, imported.Project.FixedStationPositions!["A1"].Z, 0.001f);
        Assert.AreEqual(10f, imported.Project.FixedStationPositions["C1"].X, 0.001f);
        Assert.IsTrue(SurveyLegFlags.Has(imported.Project.Shots[0].LegFlags, SurveyLegFlags.Surface));
        Assert.IsTrue(SurveyLegFlags.Has(imported.Project.Shots[1].LegFlags, SurveyLegFlags.Duplicate));
        Assert.IsTrue(SurveyLegFlags.IsNoPlot(imported.Project.Shots[2].LegFlags));
        Assert.IsTrue(SurveyLegFlags.IsHeld(imported.Project.Shots[2].LegFlags));
        Assert.IsFalse(imported.Project.Shots[2].IsPlottedTraverse);
    }

    [TestMethod]
    public void Parse_round_trips_exporter_with_anchors_and_flags()
    {
        var project = new CaveProjectDocument
        {
            Name = "Anchors RT",
            Alt = 50,
            StationEquates = [new StationEquate { StationA = "A", StationB = "A2" }],
            FixedStationPositions = new Dictionary<string, PlanStationPositionOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["C"] = new PlanStationPositionOverride(5f, 6f, 7f),
            },
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10f, Azimuth = 0f, Clino = 0f, LegFlags = "SURFACE" },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 8f, Azimuth = 90f, Clino = 0f, LegFlags = "NOPLOT" },
            ],
        };
        var text = Encoding.UTF8.GetString(SurvexExporter.BuildSvxUtf8Bom(project, new SurvexExportOptions
        {
            FixStation = "A",
            FixEasting = 0,
            FixNorthing = 0,
            FixElevation = 50,
            IncludeSplays = false,
            ProvisionalFix = false,
            IncludeLrudPassage = false,
        }).AsSpan(3..));
        var imported = SurvexImporter.Parse(text);
        Assert.AreEqual(1, imported.Project.StationEquates!.Count);
        Assert.IsTrue(imported.Project.FixedStationPositions!.ContainsKey("C"));
        Assert.IsTrue(SurveyLegFlags.Has(imported.Project.Shots[0].LegFlags, SurveyLegFlags.Surface));
        Assert.IsTrue(SurveyLegFlags.IsNoPlot(imported.Project.Shots[1].LegFlags));
    }
}
