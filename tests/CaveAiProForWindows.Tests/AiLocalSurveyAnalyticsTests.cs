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
        var crit = rows.First(r => r.Metric.Contains("Critical", StringComparison.Ordinal)).Value;
        Assert.AreEqual("1", crit);
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
}
