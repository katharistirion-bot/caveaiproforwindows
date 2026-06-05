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
}
