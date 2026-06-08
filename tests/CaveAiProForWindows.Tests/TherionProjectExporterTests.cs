using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class TherionProjectExporterTests
{
    [TestMethod]
    public void ExportProjectFolder_writes_survey_and_plan_th2()
    {
        var project = new CaveProjectDocument
        {
            Name = "Test Cave",
            Date = "2026-01-01",
            Shots =
            [
                new ShotRecord { FromStation = "1", ToStation = "2", Distance = 5, Azimuth = 90, Clino = 0 },
            ],
        };

        var dir = Path.Combine(Path.GetTempPath(), "caveai-therion-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = TherionProjectExporter.ExportProjectFolder(project, dir);
            Assert.IsTrue(result.WrittenFiles.Any(f => f.EndsWith("test_cave.th", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.WrittenFiles.Any(f => f.EndsWith("test_cave-plan.th2", StringComparison.OrdinalIgnoreCase)));
            var th2 = File.ReadAllText(result.WrittenFiles.First(f => f.Contains("-plan.th2")));
            StringAssert.Contains(th2, "map test_cave-plan");
            StringAssert.Contains(th2, "survey test_cave");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void TherionSymbolMapper_maps_water_icon()
    {
        var type = TherionSymbolMapper.ToTherionPointType("caveai:waterpool", null, null);
        Assert.AreEqual("water-pond", type);
    }
}
