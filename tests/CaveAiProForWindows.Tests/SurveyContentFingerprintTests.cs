using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SurveyAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyContentFingerprintTests
{
    [TestMethod]
    public void Compute_changes_when_shot_distance_changes()
    {
        var project = new CaveProjectDocument
        {
            Name = "Demo",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 5f, Azimuth = 0, Clino = 0 },
            ],
        };

        var before = SurveyContentFingerprint.Compute(project);
        project.Shots[0].Distance = 6f;
        var after = SurveyContentFingerprint.Compute(project);
        Assert.AreNotEqual(before, after);
    }

    [TestMethod]
    public void TryComputeFromBackupZip_matches_loaded_project()
    {
        var project = new CaveProjectDocument
        {
            Name = "Zip Cave",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10f, Azimuth = 90f, Clino = -5f },
            ],
        };

        var dir = Path.Combine(Path.GetTempPath(), "caveai_fp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "CaveAI_Backup_test.zip");
        try
        {
            AndroidBackupZipExporter.ExportSingleProject(project, zip);
            var fromZip = SurveyContentFingerprint.TryComputeFromBackupZip(zip, project.Name);
            Assert.AreEqual(SurveyContentFingerprint.Compute(project), fromZip);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
