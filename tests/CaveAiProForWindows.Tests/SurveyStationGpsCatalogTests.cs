using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyStationGpsCatalogTests
{
    [TestMethod]
    public void Build_reads_stationEnvironmentSnapshots_lat_lon()
    {
        var p = new CaveProjectDocument
        {
            StationEnvironmentSnapshots =
            {
                new StationEnvironmentSnapshot { StationName = "A", Lat = 40.1, Lon = 22.2 },
            },
        };

        var map = SurveyStationGpsCatalog.Build(p);
        Assert.AreEqual(1, map.Count);
        Assert.IsTrue(map.TryGetValue("A", out var fix));
        Assert.AreEqual(40.1, fix.Lat, 1e-9);
        Assert.AreEqual(22.2, fix.Lon, 1e-9);
    }

    [TestMethod]
    public void Build_reads_extensionData_stationGps_array()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "stationGps": [
                { "stationName": "B", "latitude": 39.5, "longitude": 21.0 }
              ]
            }
            """);
        var p = new CaveProjectDocument
        {
            ExtensionData = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone()),
        };

        var map = SurveyStationGpsCatalog.Build(p);
        Assert.IsTrue(map.ContainsKey("B"));
        Assert.AreEqual(39.5, map["B"].Lat, 1e-9);
    }
}
