using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AndroidSurveySyncServiceTests
{
    [TestMethod]
    public void TryLoadBundle_merges_database_and_export_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai_sync_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, AndroidSurveySyncService.DatabaseFileName),
                """
                [
                  {
                    "name": "Demo Cave",
                    "shots": [
                      { "fromStation": "A", "toStation": "B", "distance": 5, "notes": "Suspect misread", "azimuth": 0, "clino": 0 }
                    ]
                  }
                ]
                """);

            File.WriteAllText(
                Path.Combine(dir, AndroidSurveySyncService.ExportFileName),
                """
                {
                  "projectName": "Demo Cave",
                  "leadPredictions": [
                    { "station": "B", "text": "Draft opening continues east" }
                  ]
                }
                """);

            var bundle = AndroidSurveySyncService.TryLoadBundle(dir, "Demo Cave");
            Assert.IsNotNull(bundle);
            Assert.IsTrue(bundle!.Context.HasObservations);
            Assert.IsTrue(bundle.Context.ForStation("A").Any(o => o.Text.Contains("misread", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(bundle.Context.ForStation("B").Any(o => o.Text.Contains("Draft", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void ResolveSyncFolder_prefers_configured_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai_sync_cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, AndroidSurveySyncService.ExportFileName), "{}");
            var resolved = AndroidSurveySyncService.ResolveSyncFolder(dir, null);
            Assert.AreEqual(Path.GetFullPath(dir), resolved);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
