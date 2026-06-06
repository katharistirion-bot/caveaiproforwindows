using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AndroidSurveyAnalyticsImporterTests
{
    [TestMethod]
    public void BuildFromProject_extracts_shot_notes_and_environment_snapshots()
    {
        var p = new CaveProjectDocument
        {
            Name = "Demo",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A1",
                    ToStation = "A2",
                    Distance = 5,
                    Notes = "Suspect compass misread",
                    Azimuth = 0,
                    Clino = 0,
                },
            ],
            StationEnvironmentSnapshots =
            [
                new StationEnvironmentSnapshot { StationName = "A2", Notes = "Draft blows east" },
            ],
        };

        var ctx = AndroidSurveyAnalyticsImporter.BuildFromProject(p, "test");
        Assert.IsTrue(ctx.HasObservations);
        Assert.IsTrue(ctx.ForStation("A1").Any(o => o.Text.Contains("misread", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(ctx.ForStation("A2").Any(o => o.Text.Contains("Draft", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void TryImportFromJson_reads_leadPredictions_extension_array()
    {
        var json = """
            [
              {
                "name": "Cave",
                "shots": [],
                "leadPredictions": [
                  { "station": "B3", "text": "Open passage continues", "confidence": 0.82 }
                ]
              }
            ]
            """;

        var ctx = AndroidSurveyAnalyticsImporter.TryImportFromJson(json, "data.json");
        Assert.IsTrue(ctx.HasObservations);
        var lead = ctx.LeadPredictions().First();
        Assert.AreEqual("B3", lead.StationName);
        Assert.IsTrue(lead.Text.Contains("continues", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TryImportFromJson_survey_event_log_with_lead_type()
    {
        var json = """
            [
              {
                "name": "Cave",
                "shots": [],
                "surveyEventLog": [
                  { "stationName": "C5", "type": "leadHint", "message": "Promising lead to north" }
                ]
              }
            ]
            """;

        var ctx = AndroidSurveyAnalyticsImporter.TryImportFromJson(json, "data.json");
        Assert.IsTrue(ctx.LeadPredictions().Any(o => o.StationName == "C5"));
    }

    [TestMethod]
    public void BuildFromProject_extracts_field_catalog_brackets_and_map_symbols()
    {
        var p = new CaveProjectDocument
        {
            Name = "Demo",
            Shots =
            [
                new ShotRecord { FromStation = "A1", ToStation = "A2", Distance = 5, Azimuth = 0, Clino = 0 },
            ],
            FieldCatalogEntries = JsonDocument.Parse(
                """
                [{"kind":"mineral","name":"Calcite crust","stationTag":"A1","note":"Wall coating"}]
                """).RootElement,
            Brackets = JsonDocument.Parse(
                """
                [{"viewMode":0,"x":0,"y":0,"description":"Cold draft","temperature":"8°C"}]
                """).RootElement,
            MapSymbols = JsonDocument.Parse(
                """
                [{"viewMode":0,"surveyX":0,"surveyY":0,"symbolId":"pool_water","label":"Sump pool"}]
                """).RootElement,
        };

        var ctx = AndroidSurveyAnalyticsImporter.BuildFromProject(p, "test");
        Assert.IsTrue(ctx.ForStation("A1").Any(o =>
            o.Category.Equals("GeologicalMarker", StringComparison.OrdinalIgnoreCase) ||
            o.Category.Equals("FieldObservation", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(ctx.Observations.Any(o =>
            o.Source == "brackets" && o.Text.Contains("draft", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(ctx.Observations.Any(o =>
            o.Category.Equals("Water", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildFromProject_extracts_shot_airflow_extension_as_field_observation()
    {
        var p = new CaveProjectDocument
        {
            Name = "Demo",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "B1",
                    ToStation = "B2",
                    Distance = 3,
                    Azimuth = 0,
                    Clino = 0,
                    ExtensionData = new Dictionary<string, JsonElement>
                    {
                        ["draftDirection"] = JsonSerializer.SerializeToElement("NE"),
                        ["airFlowMps"] = JsonSerializer.SerializeToElement(1.2f),
                    },
                },
            ],
        };

        var ctx = AndroidSurveyAnalyticsImporter.BuildFromProject(p, "test");
        Assert.IsTrue(ctx.ForStation("B1").Any(o =>
            o.Category.Equals("Airflow", StringComparison.OrdinalIgnoreCase)));
    }
}
