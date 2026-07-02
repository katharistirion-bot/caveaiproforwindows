using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Favorites;
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
    public void Search_RichMetadataOnly_FiltersSparseEntries()
    {
        var entries = new List<ReferenceCaveIndexEntry>
        {
            new() { Id = "1", Name = "Rich Cave", Country = "Greece", Lat = 38.2, Lon = 20.6, DepthM = 120 },
            new() { Id = "2", Name = "Sparse Cave", Country = "Greece", Lat = 38.3, Lon = 20.7 },
        };
        var hits = ReferenceCatalogSearch.Filter(entries, null, null, null, null, richMetadataOnly: true);
        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("1", hits[0].Id);
        Assert.IsTrue(ReferenceCatalogSearch.ShouldRunSearch(null, null, false, richMetadataOnly: true));
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
    public void Display_ListSummary_IncludesDepthAndRegion()
    {
        var entry = new ReferenceCaveIndexEntry
        {
            Id = "1",
            Name = "Test Cave",
            Country = "Greece",
            Region = "Arcadia, Peloponnese",
            DepthM = 120,
            LengthM = 800,
            Lat = 37.5,
            Lon = 22.3,
        };
        var summary = ReferenceCatalogDisplay.ListSummary(entry);
        StringAssert.Contains(summary, "Greece");
        StringAssert.Contains(summary, "Arcadia");
        StringAssert.Contains(summary, "120");
        StringAssert.Contains(summary, "800");
        Assert.AreEqual(ReferenceCatalogDisplay.RichOsmBadge, ReferenceCatalogDisplay.ListBadge(entry));
    }

    [TestMethod]
    public void Display_Preview_UsesSynthesizedTextWhenPreviewMissing()
    {
        var entry = new ReferenceCaveIndexEntry
        {
            Id = "1",
            Name = "Melissani Cave",
            Country = "Greece",
            Region = "Kefalonia, Ionian Islands",
            Lat = 38.2,
            Lon = 20.6,
        };
        var preview = ReferenceCatalogDisplay.PreviewText(entry);
        StringAssert.Contains(preview, "Melissani Cave");
        StringAssert.Contains(preview, "OpenStreetMap");
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

    [TestMethod]
    public void MapDepthColors_TiersMatchContract()
    {
        Assert.AreEqual(
            ReferenceCatalogMapDepthColors.Shallow,
            ReferenceCatalogMapDepthColors.ReferenceFillColor(12));
        Assert.AreEqual(
            ReferenceCatalogMapDepthColors.Medium,
            ReferenceCatalogMapDepthColors.ReferenceFillColor(80));
        Assert.AreEqual(
            ReferenceCatalogMapDepthColors.Deep,
            ReferenceCatalogMapDepthColors.ReferenceFillColor(200));
        Assert.AreEqual(
            ReferenceCatalogMapDepthColors.Unknown,
            ReferenceCatalogMapDepthColors.ReferenceFillColor(null));
    }

    [TestMethod]
    public void SimilarCaves_RanksByProximityAndDepthBand()
    {
        var source = new ReferenceCaveIndexEntry
        {
            Id = "a",
            Name = "Source",
            Lat = 38,
            Lon = 23,
            Country = "Greece",
            Region = "Attica",
            DepthM = 120,
        };
        var index = new List<ReferenceCaveIndexEntry>
        {
            source,
            new() { Id = "b", Name = "Near shallow", Lat = 38.01, Lon = 23.01, Country = "Greece", DepthM = 10 },
            new() { Id = "c", Name = "Near similar depth", Lat = 38.02, Lon = 23.02, Country = "Greece", DepthM = 130 },
        };
        var hits = ReferenceCatalogSimilarCaves.FindSimilar(source, index);
        Assert.AreEqual(2, hits.Count);
        Assert.AreEqual("c", hits[0].Entry.Id);
    }
}

[TestClass]
public sealed class CaveFavoriteIdMergeTests
{
    [TestMethod]
    public void Merge_includes_both_favorites_and_saved_caves()
    {
        var merged = CaveFavoriteIdMerge.Merge(
            new[] { "pub-1", "pub-2" },
            new[] { "ref-gr-1", "pub-2" });

        CollectionAssert.AreEquivalent(
            new[] { "pub-1", "pub-2", "ref-gr-1" },
            merged.ToArray());
    }

    [TestMethod]
    public void Merge_empty_inputs_returns_empty_set()
    {
        Assert.AreEqual(0, CaveFavoriteIdMerge.Merge(Array.Empty<string>(), Array.Empty<string>()).Count);
    }
}
