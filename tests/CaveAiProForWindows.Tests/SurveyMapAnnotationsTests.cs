using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyMapAnnotationsTests
{
    [TestMethod]
    public void BuildLegLabels_includes_tape_az_clino_lrud_and_delta_z()
    {
        var project = new CaveProjectDocument
        {
            Name = "Test",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "1",
                    ToStation = "2",
                    Distance = 12.5f,
                    Azimuth = 185f,
                    Clino = -12f,
                    L = 2f,
                    R = 3f,
                    U = 0.5f,
                    D = 1.2f,
                    ManualAmbientTempCelsius = 18.2f,
                    ManualRelativeHumidityPct = 92f,
                },
            ],
        };

        var scene = PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan)!;
        var ann = SurveyMapAnnotationsBuilder.Build(project, scene, 0, legDetails: true, stationEnvironment: true);

        Assert.AreEqual(1, ann.LegLabels.Count);
        StringAssert.Contains(ann.LegLabels[0].PrimaryLine, "12.5 m");
        StringAssert.Contains(ann.LegLabels[0].SecondaryLine!, "185");
        StringAssert.Contains(ann.LegLabels[0].LrudLine!, "L2");
        var st1 = ann.StationEnvironment.FirstOrDefault(e => e.StationName == "1");
        Assert.IsNotNull(st1);
        StringAssert.Contains(st1!.Lines[0], "18.2");
    }

    [TestMethod]
    public void GetDisplayName_uses_name_then_extension_fallback()
    {
        Assert.AreEqual("Goura Cave", CaveProjectDisplayNames.GetDisplayName(new CaveProjectDocument { Name = "Goura Cave" }));

        var ext = new CaveProjectDocument
        {
            ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["caveName"] = System.Text.Json.JsonDocument.Parse("\"Katafygi\"").RootElement,
            },
        };
        Assert.AreEqual("Katafygi", CaveProjectDisplayNames.GetDisplayName(ext));
    }

    [TestMethod]
    public void BuildDepthSpans_and_brackets_respect_view_mode()
    {
        var project = new CaveProjectDocument
        {
            Name = "Spans",
            DepthSpanAnnotations = System.Text.Json.JsonDocument.Parse(
                """
                [{"viewMode":0,"x1":0,"y1":0,"x2":5,"y2":0,"depthMeters":8.5}]
                """).RootElement,
            Brackets = System.Text.Json.JsonDocument.Parse(
                """
                [{"viewMode":1,"x":2,"y":3,"description":"Cold pool","temperature":"4°C"}]
                """).RootElement,
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 5f, Azimuth = 0, Clino = 0 },
            ],
        };

        var scene = PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan)!;
        var plan = SurveyMapAnnotationsBuilder.Build(project, scene, 0, true, false);
        var section = SurveyMapAnnotationsBuilder.Build(project, scene, 1, true, false);

        Assert.AreEqual(1, plan.DepthSpans.Count);
        Assert.AreEqual("8.5 m", plan.DepthSpans[0].Label);
        Assert.AreEqual(0, plan.Brackets.Count);

        Assert.AreEqual(0, section.DepthSpans.Count);
        Assert.AreEqual(1, section.Brackets.Count);
        StringAssert.Contains(section.Brackets[0].Text, "Cold pool");
    }
}
