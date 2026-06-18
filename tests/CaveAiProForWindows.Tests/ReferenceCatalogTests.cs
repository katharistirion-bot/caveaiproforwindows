using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CaveAiProForWindows.Services.FieldTrip;
using CaveAiProForWindows.Services.ReferenceCatalog;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class ReferenceCatalogTests
{
    [TestMethod]
    public void IsDiskIndexStale_WhenUpdatedAtDiffers_ReturnsTrue()
    {
        var disk = new ReferenceIndexFile { UpdatedAt = "2026-01-01", Entries = new List<ReferenceCaveIndexEntry> { new() { Id = "a", Name = "A", Lat = 1, Lon = 2 } } };
        var meta = new ReferenceCatalogMeta { UpdatedAt = "2026-06-01", CaveCount = 100 };
        Assert.IsTrue(ReferenceCatalogFetchService.IsDiskIndexStale(disk, meta));
    }

    [TestMethod]
    public void Search_MultiToken_FiltersByName()
    {
        var entries = new List<ReferenceCaveIndexEntry>
        {
            new() { Id = "1", Name = "Melissani Cave", Country = "Greece", Lat = 38.2, Lon = 20.6 },
            new() { Id = "2", Name = "Blue Grotto", Country = "Italy", Lat = 40.6, Lon = 14.2 },
        };
        var hits = ReferenceCatalogSearch.Filter(entries, "melissani greece", "Greece", null, null);
        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("1", hits[0].Id);
    }

    [TestMethod]
    public void MatchService_FindsNearbySameName()
    {
        var project = new CaveProjectDocument { Name = "Melissani", Lat = 38.22, Lon = 20.62 };
        var index = new List<ReferenceCaveIndexEntry>
        {
            new() { Id = "ref-1", Name = "Melissani Cave", Lat = 38.221, Lon = 20.621 },
        };
        var matches = ReferenceCatalogMatchService.FindMatches(project, index);
        Assert.IsTrue(matches.Count > 0);
        Assert.IsTrue(matches[0].DistanceKm < 1);
    }

    [TestMethod]
    public void Search_SortsByName_WhenNotNearMe()
    {
        var entries = new List<ReferenceCaveIndexEntry>
        {
            new() { Id = "2", Name = "Zeta Cave", Country = "Greece", Lat = 38.2, Lon = 20.6 },
            new() { Id = "1", Name = "Alpha Cave", Country = "Greece", Lat = 38.3, Lon = 20.7 },
        };
        var hits = ReferenceCatalogSearch.Filter(entries, null, null, null, null);
        Assert.AreEqual("Alpha Cave", hits[0].Name);
    }

    [TestMethod]
    public void Geolocation_IsValidCoordinate_rejectsNullIsland()
    {
        Assert.IsFalse(ReferenceCatalogGeolocation.IsValidCoordinate(0, 0));
        Assert.IsTrue(ReferenceCatalogGeolocation.IsValidCoordinate(38.22, 20.62));
    }

    [TestMethod]
    public void ShareUrl_IncludesCountrySlug()
    {
        var entry = new ReferenceCaveIndexEntry { Id = "abc-123", Country = "Greece", Lat = 1, Lon = 2 };
        var url = ReferenceCatalogShareUrls.BuildShareUrl(entry);
        StringAssert.Contains(url, "abc-123");
        StringAssert.Contains(url, "country=greece");
    }

    [TestMethod]
    public void SurveyStartUrl_IncludesActionSurvey()
    {
        var entry = new ReferenceCaveIndexEntry { Id = "osm-node-42", Country = "Greece", Lat = 38.2, Lon = 20.6 };
        var url = ReferenceCatalogShareUrls.BuildSurveyStartUrl(entry);
        StringAssert.Contains(url, "/cave/ref/osm-node-42");
        StringAssert.Contains(url, "action=survey");
        StringAssert.Contains(url, "country=greece");
    }

    [TestMethod]
    public void NearbyPins_FiltersByBounds()
    {
        var index = new List<ReferenceCaveIndexEntry>
        {
            new() { Id = "1", Name = "Inside", Lat = 38.2, Lon = 20.6 },
            new() { Id = "2", Name = "Outside", Lat = 50, Lon = 10 },
        };
        var pins = ReferenceCatalogNearbyPins.FindInBounds(index, 38, 39, 20, 21);
        Assert.AreEqual(1, pins.Count);
        Assert.AreEqual("Inside", pins[0].Name);
    }

    [TestMethod]
    public void PlanPins_GeoToSurveyMetres_UsesEntranceOrigin()
    {
        var originLat = 38.2;
        var originLon = 20.6;
        var metres = ReferenceCatalogPlanPins.TryGeoToSurveyMetres(originLat + 0.001, originLon, originLat, originLon);
        Assert.IsNotNull(metres);
        Assert.IsTrue(metres.Value.Y > 100);
        Assert.IsTrue(Math.Abs(metres.Value.X) < 5);
    }

    [TestMethod]
    public void PlanPins_Collect_IncludesLinkedReference()
    {
        var project = new CaveProjectDocument
        {
            Name = "Test",
            Lat = 38.2,
            Lon = 20.6,
            ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
            {
                [ReferenceSurveyLinkService.ExtensionKey] = System.Text.Json.JsonSerializer.SerializeToElement(new ReferenceSurveyLink
                {
                    Id = "ref-1",
                    Name = "Linked Cave",
                    Lat = 38.21,
                    Lon = 20.61,
                }),
            },
        };
        var pins = ReferenceCatalogPlanPins.CollectPins(project);
        Assert.IsTrue(pins.Any(p => p.IsLinkedReference && p.Id == "ref-1"));
    }

    [TestMethod]
    public void SurveyCompare_BuildsReport_WhenLinkPresent()
    {
        var project = new CaveProjectDocument
        {
            Name = "Survey",
            Lat = 38.22,
            Lon = 20.62,
            Shots = new List<ShotRecord>
            {
                new() { FromStation = "A", ToStation = "B", Distance = 50, Azimuth = 0, Clino = 0 },
            },
            ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
            {
                [ReferenceSurveyLinkService.ExtensionKey] = System.Text.Json.JsonSerializer.SerializeToElement(new ReferenceSurveyLink
                {
                    Id = "ref-1",
                    Name = "Ref Cave",
                    Lat = 38.221,
                    Lon = 20.621,
                    Country = "Greece",
                }),
            },
        };
        var entry = new ReferenceCaveIndexEntry { Id = "ref-1", Name = "Ref Cave", Lat = 38.221, Lon = 20.621, DepthM = 120, LengthM = 800 };
        Assert.IsTrue(ReferenceSurveyCompareService.TryBuildReport(project, out var report, entry));
        StringAssert.Contains(report, "SURVEY vs REFERENCE COMPARE");
        StringAssert.Contains(report, "Entrance distance");
        StringAssert.Contains(report, "Catalog depth");
    }
}

[TestClass]
public class FieldTripExportTests
{
    [TestMethod]
    public void ExportGpx_ContainsRoutePoints()
    {
        var trip = new FieldTripDocument
        {
            Name = "Test trip",
            Stops =
            [
                new FieldTripStop { Name = "Cave A", Lat = 38.1, Lon = 20.5 },
                new FieldTripStop { Name = "Cave B", Lat = 38.2, Lon = 20.6 },
            ],
        };
        var gpx = FieldTripExportService.ExportGpx(trip);
        StringAssert.Contains(gpx, "<gpx");
        StringAssert.Contains(gpx, "Cave A");
        StringAssert.Contains(gpx, "Cave B");
        StringAssert.Contains(gpx, "rtept");
    }

    [TestMethod]
    public void BuildGoogleMapsUrl_requires_two_stops()
    {
        var one = new FieldTripDocument
        {
            Stops = [new FieldTripStop { Lat = 1, Lon = 2 }],
        };
        Assert.IsNull(FieldTripExportService.BuildGoogleMapsDirectionsUrl(one));

        var two = new FieldTripDocument
        {
            Stops =
            [
                new FieldTripStop { Lat = 38.1, Lon = 20.5 },
                new FieldTripStop { Lat = 38.2, Lon = 20.6 },
            ],
        };
        var url = FieldTripExportService.BuildGoogleMapsDirectionsUrl(two);
        Assert.IsNotNull(url);
        StringAssert.Contains(url!, "google.com/maps/dir");
    }
}
