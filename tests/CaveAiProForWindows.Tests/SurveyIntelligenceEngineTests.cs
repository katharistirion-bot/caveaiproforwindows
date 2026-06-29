using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyIntelligenceEngineTests
{
    [TestMethod]
    public void BuildHealth_counts_stations_legs_and_depth()
    {
        var p = new CaveProjectDocument
        {
            Name = "Demo Cave",
            Alt = 1000,
            Shots = new List<ShotRecord>
            {
                new() { FromStation = "A", ToStation = "B", Distance = 10, Clino = -10, Azimuth = 0, Depth = 5, L = 1, R = 1, U = 1, D = 1 },
                new() { FromStation = "B", ToStation = "C", Distance = 8, Clino = -5, Azimuth = 90, Depth = 8, L = 1, R = 1, U = 1, D = 1 },
                new() { FromStation = "C", ToStation = "-", Distance = 3, Clino = 0, Azimuth = 0, Depth = 8 },
            },
        };

        var health = SurveyIntelligenceEngine.BuildHealth(p);

        Assert.AreEqual(3, health.StationCount);
        Assert.AreEqual(2, health.TraverseLegCount);
        Assert.AreEqual(1, health.SplayCount);
        Assert.IsTrue(health.TraverseLengthM > 0);
        Assert.AreEqual(0, health.MissingLrudLegCount);
    }

    [TestMethod]
    public void BuildHealth_flags_missing_lrud_and_no_gps()
    {
        var p = new CaveProjectDocument
        {
            Name = "T",
            Shots = new List<ShotRecord>
            {
                new() { FromStation = "A", ToStation = "B", Distance = 5, Clino = 0, Azimuth = 0 },
            },
        };

        var health = SurveyIntelligenceEngine.BuildHealth(p);
        Assert.AreEqual(1, health.MissingLrudLegCount);
        Assert.IsFalse(health.EntranceGpsLocked);
    }

    [TestMethod]
    public void BuildInsights_includes_gps_warning_when_no_entrance()
    {
        var p = new CaveProjectDocument
        {
            Name = "T",
            Shots = new List<ShotRecord>
            {
                new() { FromStation = "A", ToStation = "B", Distance = 5, Clino = 0, Azimuth = 0, L = 1, R = 1, U = 1, D = 1 },
            },
        };

        var insights = SurveyIntelligenceEngine.BuildInsights(p);
        Assert.IsTrue(insights.Any(i => i.Category == "GPS"));
    }

    [TestMethod]
    public void BuildCombinedFindings_includes_volume_and_qc_rows()
    {
        var p = new CaveProjectDocument
        {
            Name = "T",
            Shots = new List<ShotRecord>
            {
                new()
                {
                    FromStation = "A", ToStation = "B", Distance = 10,
                    L = 1, R = 1, U = 0.5f, D = 0.5f, Azimuth = 0, Clino = 0,
                    CompassSampleVarianceDeg2 = 25f,
                },
            },
        };

        var rows = SurveyIntelligenceEngine.BuildCombinedFindings(p);
        Assert.IsTrue(rows.Any(r => r.Metric.Contains("Project health", StringComparison.Ordinal)));
        Assert.IsTrue(rows.Any(r => r.Metric.Contains("Passage volume", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CompareWithPreviousBackup_returns_no_comparison_with_one_zip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai_intel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "CaveAI_Backup_only.zip"), "x");
            var p = new CaveProjectDocument { Name = "T" };
            var diff = SurveyIntelligenceEngine.CompareWithPreviousBackup(p, dir);
            Assert.IsFalse(diff.HasComparison);
            StringAssert.Contains(diff.Summary, "Only one backup");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void Expedition_report_exporter_writes_markdown_sections()
    {
        var health = new SurveyHealthSnapshot { ProjectName = "T", StationCount = 2, TraverseLegCount = 1, HealthSummary = "demo" };
        var bytes = AiAnalyticsExpeditionReportExporter.BuildMarkdownUtf8(
            health,
            [new SurveyIntelligenceInsight { Category = "Overview", Message = "demo", Severity = AiAnalyticsAlertLevel.Info }],
            new SurveyBackupDiff { Summary = "none" },
            [],
            []);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        StringAssert.Contains(text, "# CaveAI Survey Intelligence");
        StringAssert.Contains(text, "## Project health");
        StringAssert.Contains(text, "## Offline insights");
    }
}