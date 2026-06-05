using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class IntegrationFixtureTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [TestMethod]
    public void Fixture_bundle_loads_observations_and_gps()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai_fixture_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.Copy(FixturePath("caveai_database_v1_fixture.json"),
                Path.Combine(dir, AndroidSurveySyncService.DatabaseFileName));
            File.Copy(FixturePath("android_export_fixture.json"),
                Path.Combine(dir, AndroidSurveySyncService.ExportFileName));

            var bundle = AndroidSurveySyncService.TryLoadBundle(dir, "Fixture Cave");
            Assert.IsNotNull(bundle);
            Assert.IsTrue(bundle!.Context.HasObservations);
            Assert.IsTrue(bundle.Context.ForStation("1").Any());

            var dbJson = File.ReadAllText(FixturePath("caveai_database_v1_fixture.json"));
            var projects = JsonSerializer.Deserialize<List<CaveProjectDocument>>(dbJson);
            Assert.IsNotNull(projects);
            var gps = SurveyStationGpsCatalog.Build(projects![0]);
            Assert.IsTrue(gps.Count >= 1, "Expected at least one GPS fix from fixture project");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
