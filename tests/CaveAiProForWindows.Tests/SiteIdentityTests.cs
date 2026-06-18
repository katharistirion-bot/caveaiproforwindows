using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SiteIdentityTests
{
    [TestMethod]
    public void ResolveActiveProject_cave_includesSiteTypeLabel()
    {
        var project = new CaveProjectDocument
        {
            Name = "Demo Cave",
            SurveySiteType = "CAVE",
        };

        var identity = SiteIdentity.ResolveActiveProject(project);

        Assert.AreEqual("CAVE", identity.SiteTypeToken);
        Assert.AreEqual("Cave", identity.SiteTypeLabel);
        Assert.AreEqual("Cave", identity.PrimaryMapLabel);
        StringAssert.Contains(identity.SummaryLine, "active CaveAI Pro field survey (Cave).");
    }

    [TestMethod]
    public void ResolveActiveProject_mine_includesSiteTypeLabel()
    {
        var project = new CaveProjectDocument
        {
            Name = "Old Quarry",
            SurveySiteType = "MINE",
        };

        var identity = SiteIdentity.ResolveActiveProject(project);

        Assert.AreEqual("MINE", identity.SiteTypeToken);
        Assert.AreEqual("Mine", identity.SiteTypeLabel);
        StringAssert.Contains(identity.SummaryLine, "(Mine).");
    }

    [TestMethod]
    public void ResolveActiveProject_empty_defaultsToCave()
    {
        var project = new CaveProjectDocument { Name = "Untyped" };

        var identity = SiteIdentity.ResolveActiveProject(project);

        Assert.AreEqual("CAVE", identity.SiteTypeToken);
        Assert.AreEqual("Cave", identity.SiteTypeLabel);
        StringAssert.Contains(identity.SummaryLine, "(Cave).");
    }

    [TestMethod]
    public void ResolveActiveProject_null_project_reportsNoActiveProject()
    {
        var identity = SiteIdentity.ResolveActiveProject(null);

        Assert.AreEqual("", identity.SiteTypeToken);
        Assert.AreEqual("Site identity: no active project loaded.", identity.SummaryLine);
    }

    [TestMethod]
    public void ResolveActiveProject_includesReferenceCatalogLink()
    {
        var link = new ReferenceSurveyLink
        {
            Id = "osm-1",
            Name = "Old Mine",
            Country = "Greece",
            Lat = 38.0,
            Lon = 23.0,
        };
        var project = new CaveProjectDocument
        {
            Name = "Test Cave",
            SurveySiteType = "MINE",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                [ReferenceSurveyLinkService.ExtensionKey] = JsonSerializer.SerializeToElement(link),
            },
        };

        var line = SiteIdentity.SummarizeActiveProject(project);

        StringAssert.Contains(line, "Mine");
        StringAssert.Contains(line, "osm-1");
        StringAssert.Contains(line, "Old Mine");
    }
}
