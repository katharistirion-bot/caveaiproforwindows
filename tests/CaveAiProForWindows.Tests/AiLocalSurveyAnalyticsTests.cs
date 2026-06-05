using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AiLocalSurveyAnalyticsTests
{
    [TestMethod]
    public void Volume_sums_lrud_prism_per_traverse_leg()
    {
        var p = new CaveProjectDocument
        {
            Name = "T",
            Shots = new List<ShotRecord>
            {
                new()
                {
                    FromStation = "A",
                    ToStation = "B",
                    Distance = 10,
                    L = 1,
                    R = 1,
                    U = 0.5f,
                    D = 0.5f,
                    Azimuth = 0,
                    Clino = 0,
                },
            },
        };
        var rows = AiLocalSurveyAnalytics.BuildResultRows("Volume", p);
        var total = rows.First(r => r.Metric.StartsWith("Total", StringComparison.Ordinal)).Value;
        Assert.AreEqual("20.0 m³", total);
    }

    [TestMethod]
    public void Qc_counts_shots_over_std_threshold()
    {
        var p = new CaveProjectDocument
        {
            Name = "T",
            Shots = new List<ShotRecord>
            {
                new()
                {
                    FromStation = "A",
                    ToStation = "B",
                    Distance = 1,
                    CompassSampleVarianceDeg2 = 1f,
                    Azimuth = 0,
                    Clino = 0,
                },
                new()
                {
                    FromStation = "B",
                    ToStation = "C",
                    Distance = 1,
                    CompassSampleVarianceDeg2 = 25f,
                    Azimuth = 0,
                    Clino = 0,
                },
            },
        };
        var rows = AiLocalSurveyAnalytics.BuildResultRows("Qc", p);
        var crit = rows.First(r => r.Metric.Contains("geometry QC warnings", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.AreEqual("2", crit);
    }

    [TestMethod]
    public void Lead_finds_degree_one_endpoints()
    {
        var p = new CaveProjectDocument
        {
            Name = "T",
            Shots = new List<ShotRecord>
            {
                new() { FromStation = "A", ToStation = "B", Distance = 5, Azimuth = 0, Clino = 0 },
                new() { FromStation = "B", ToStation = "C", Distance = 5, Azimuth = 0, Clino = 0 },
            },
        };
        var rows = AiLocalSurveyAnalytics.BuildResultRows("Lead", p);
        var count = rows.First(r => r.Metric.Contains("endpoint", StringComparison.OrdinalIgnoreCase)).Value;
        var names = rows.First(r => r.Metric.Contains("Station names", StringComparison.Ordinal)).Value;
        Assert.AreEqual("2", count);
        Assert.IsTrue(names.Contains("A", StringComparison.Ordinal));
        Assert.IsTrue(names.Contains("C", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Qc_corroborates_geometry_warnings_with_android_notes()
    {
        var p = new CaveProjectDocument
        {
            Name = "T",
            Shots =
            [
                new()
                {
                    FromStation = "A",
                    ToStation = "B",
                    Distance = 1,
                    CompassSampleVarianceDeg2 = 25f,
                    Notes = "Suspect blunder on this leg",
                    Azimuth = 0,
                    Clino = 0,
                },
            ],
        };
        var ctx = AndroidSurveyAnalyticsImporter.BuildFromProject(p, "test");
        var rows = AiLocalSurveyAnalytics.BuildResultRows("Qc", p, ctx);
        Assert.IsTrue(rows.Any(r =>
            r.Station == "A" &&
            !string.IsNullOrEmpty(r.GeometryContext) &&
            r.IsPriorityAlert));
    }

    [TestMethod]
    public void Lead_confirms_open_endpoint_when_android_hint_present()
    {
        var p = new CaveProjectDocument
        {
            Name = "T",
            Shots =
            [
                new() { FromStation = "A", ToStation = "B", Distance = 5, Azimuth = 0, Clino = 0 },
                new() { FromStation = "B", ToStation = "C", Distance = 5, Azimuth = 0, Clino = 0 },
            ],
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["leadPredictions"] = JsonSerializer.SerializeToElement(new[]
                {
                    new { station = "A", text = "Open lead — draft continues" },
                }),
            },
        };
        var ctx = AndroidSurveyAnalyticsImporter.BuildFromProject(p, "test");
        var rows = AiLocalSurveyAnalytics.BuildResultRows("Lead", p, ctx);
        Assert.IsTrue(rows.Any(r =>
            r.Station == "A" &&
            r.Metric.Contains("Confirmed lead", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(rows.Any(r =>
            r.Station == "A" &&
            r.IsPriorityAlert));
    }

    [TestMethod]
    public void Lead_priority_alert_when_draft_annotation_matches_open_endpoint()
    {
        var p = new CaveProjectDocument
        {
            Name = "T",
            Shots =
            [
                new() { FromStation = "A", ToStation = "B", Distance = 5, Azimuth = 0, Clino = 0 },
                new() { FromStation = "B", ToStation = "C", Distance = 5, Azimuth = 0, Clino = 0 },
            ],
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["stationAnnotations"] = JsonSerializer.SerializeToElement(new[]
                {
                    new { station = "A", text = "Strong draft — opening visible" },
                }),
            },
        };
        var ctx = AndroidSurveyAnalyticsImporter.BuildFromProject(p, "test");
        var rows = AiLocalSurveyAnalytics.BuildResultRows("Lead", p, ctx);
        Assert.IsTrue(rows.Any(r =>
            r.Station == "A" &&
            r.AlertLevel == AiAnalyticsAlertLevel.Critical));
    }
}
