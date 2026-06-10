using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SurveyAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class UnifiedQcReportExporterTests
{
    private static CaveProjectDocument LoopProject() =>
        new()
        {
            Name = "LoopCave",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10, Azimuth = 0, Clino = 0, L = 1, R = 1, U = 1, D = 1 },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 10, Azimuth = 90, Clino = 0, L = 1, R = 1, U = 1, D = 1 },
                new ShotRecord { FromStation = "C", ToStation = "D", Distance = 10, Azimuth = 180, Clino = 0, L = 1, R = 1, U = 1, D = 1 },
                new ShotRecord { FromStation = "D", ToStation = "A", Distance = 10, Azimuth = 270, Clino = 0, L = 1, R = 1, U = 1, D = 1 },
            ],
        };

    [TestMethod]
    public void ExportToFolder_writes_summary_anomalies_and_loops()
    {
        var project = LoopProject();
        var dir = Path.Combine(Path.GetTempPath(), "caveai_qc_" + Guid.NewGuid().ToString("N"));
        try
        {
            var count = UnifiedQcReportExporter.ExportToFolder(project, dir);
            Assert.IsTrue(count >= 3);
            var files = Directory.GetFiles(dir);
            Assert.IsTrue(files.Any(f => f.EndsWith("_qc_summary.txt", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(files.Any(f => f.EndsWith("_anomalies.csv", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(files.Any(f => f.EndsWith("_loops.csv", StringComparison.OrdinalIgnoreCase)));

            var csv = File.ReadAllText(files.First(f => f.EndsWith("_anomalies.csv", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(csv.Contains("Kind,StationOrLeg", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Thresholds_match_loop_severity_classifier()
    {
        Assert.AreEqual(SurveyAnomalyThresholds.LoopMisclosureSkipMetres, LoopClosureSeverityClassifier.GoodThresholdMetres);
        Assert.AreEqual(SurveyAnomalyThresholds.LoopMisclosureCriticalMetres, LoopClosureSeverityClassifier.LargeThresholdMetres);
    }
}
