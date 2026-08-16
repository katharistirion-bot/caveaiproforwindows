using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurfaceMap;
using CaveAiProForWindows.Services.SurveyCloud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class OfflinePackAndSurveyCloudTests
{
    [TestMethod]
    public void BuildOfflinePackUrls_includesOsmHillshadeDemAndGlyphs()
    {
        var urls = SurfaceMapTileCacheService.BuildOfflinePackUrls(
            south: 40.0, west: 22.0, north: 40.05, east: 22.05,
            zoomMin: 11, zoomMax: 12);
        Assert.IsTrue(urls.Exists(u => u.Contains("tile.openstreetmap.org", StringComparison.Ordinal)));
        Assert.IsTrue(urls.Exists(u => u.Contains("tiles.wmflabs.org/hillshading", StringComparison.Ordinal)));
        Assert.IsTrue(urls.Exists(u => u.Contains("elevation-tiles-prod/terrarium", StringComparison.Ordinal)));
        Assert.IsTrue(urls.Exists(u => u.Contains("demotiles.maplibre.org/font", StringComparison.Ordinal)));
        Assert.IsTrue(urls.Exists(u => u.EndsWith(".pbf", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void EnsureProjectId_assignsStableIdWhenMissing()
    {
        var p = new CaveProjectDocument { Name = "t" };
        var a = SurveyCloudProjectService.EnsureProjectId(p);
        var b = SurveyCloudProjectService.EnsureProjectId(p);
        Assert.AreEqual(a, b);
        Assert.AreEqual(32, a.Length);
        Assert.AreEqual(a, p.ProjectId);
    }
}
