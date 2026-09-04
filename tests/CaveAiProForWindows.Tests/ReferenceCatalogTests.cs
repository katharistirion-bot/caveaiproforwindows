using System.Text.Json;
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
    public void ExploreTerrainUrl_IncludesViewportAndPreset()
    {
        var entry = new ReferenceCaveIndexEntry
        {
            Id = "1",
            Name = "Test Cave",
            Country = "Greece",
            Lat = 38.22,
            Lon = 20.62,
        };
        var url = ReferenceCatalogShareUrls.BuildExploreTerrainUrl(entry);
        StringAssert.Contains(url, "view=explore");
        StringAssert.Contains(url, "preset=terrain");
        StringAssert.Contains(url, "country=greece");
        StringAssert.Contains(url, "lat=38.22");
        StringAssert.Contains(url, "lon=20.62");
        StringAssert.Contains(url, "ref=cave");
        StringAssert.Contains(url, "id=1");
        StringAssert.Contains(url, "name=Test");
    }

    [TestMethod]
    public void ExploreTerrainUrlForStops_FocusesFirstStop()
    {
        var stops = new List<FieldTripStop>
        {
            new()
            {
                ReferenceId = "osm-node-1",
                Name = "First Cave",
                Lat = 38.0,
                Lon = 20.0,
                Country = "Greece",
            },
            new()
            {
                ReferenceId = "osm-node-2",
                Name = "Second Cave",
                Lat = 38.5,
                Lon = 20.5,
                Country = "Greece",
            },
        };
        var url = ReferenceCatalogShareUrls.BuildExploreTerrainUrlForStops(stops);
        StringAssert.Contains(url, "view=explore");
        StringAssert.Contains(url, "ref=cave");
        StringAssert.Contains(url, "id=osm-node-1");
        StringAssert.Contains(url, "name=First");
        StringAssert.Contains(url, "stops=2");
        StringAssert.Contains(url, "ft=");
        // First-stop center (web/Android parity) — not bbox midpoint 38.25
        StringAssert.Contains(url, "lat=38");
        StringAssert.Contains(url, "lon=20");
        Assert.IsFalse(url.Contains("lat=38.25", StringComparison.Ordinal), url);
        StringAssert.Contains(url, "38.00000");
        StringAssert.Contains(url, "20.00000");
        StringAssert.Contains(url, "38.50000");
        StringAssert.Contains(url, "20.50000");
    }

    [TestMethod]
    public void ExploreTerrainUrlForStops_EncodesNotesInPack()
    {
        var stops = new List<FieldTripStop>
        {
            new()
            {
                ReferenceId = "a",
                Name = "A",
                Lat = 35.20825,
                Lon = 24.82894,
                Country = "Greece",
            },
            new()
            {
                ReferenceId = "b",
                Name = "B",
                Lat = 35.16286,
                Lon = 25.44506,
                Country = "Greece",
            },
        };
        var url = ReferenceCatalogShareUrls.BuildExploreTerrainUrlForStops(stops, notes: "Respect show-cave rules");
        StringAssert.Contains(url, "stops=2");
        StringAssert.Contains(url, "ft=");
        StringAssert.Contains(url, "35.20825");
        StringAssert.Contains(url, "notes=");
        StringAssert.Contains(url, "Respect");
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
    public void CaveAiWebUrl_FromReference_IncludesContextParams()
    {
        var entry = new ReferenceCaveIndexEntry
        {
            Id = "osm-node-42",
            Name = "Melissani Cave",
            Country = "Greece",
            Region = "Kefalonia",
            DepthM = 39,
            Lat = 38.2,
            Lon = 20.6,
        };
        var url = CaveAiWebUrls.BuildFromReference(entry);
        StringAssert.Contains(url, "/ai?");
        StringAssert.Contains(url, "refId=osm-node-42");
        StringAssert.Contains(url, "name=Melissani");
        StringAssert.Contains(url, "country=Greece");
        Assert.IsTrue(CaveAiWebUrls.IsCaveAiWebDeepLink(url));
        Assert.IsFalse(CaveAiWebUrls.IsCaveAiWebDeepLink("https://www.caveaipro.com/map"));
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
    [TestMethod]
    public void ExploreCompareTerrainUrl_FramesTwoReferences()
    {
        var a = new ReferenceCaveIndexEntry { Id = "1", Country = "Greece", Lat = 38.0, Lon = 20.0 };
        var b = new ReferenceCaveIndexEntry { Id = "2", Country = "Greece", Lat = 38.5, Lon = 20.5 };
        var url = ReferenceCatalogShareUrls.BuildExploreCompareTerrainUrl(a, b);
        StringAssert.Contains(url, "view=explore");
        StringAssert.Contains(url, "zoom=");
        StringAssert.Contains(url, "country=greece");
    }

    [TestMethod]
    public void ExploreHydrologyScoutUrl_UsesCloserZoom()
    {
        var entry = new ReferenceCaveIndexEntry { Id = "1", Country = "Greece", Lat = 38.22, Lon = 20.62 };
        var url = ReferenceCatalogShareUrls.BuildExploreHydrologyScoutUrl(entry);
        StringAssert.Contains(url, "zoom=14");
        StringAssert.Contains(url, "preset=terrain");
        StringAssert.Contains(url, "layers=hydrology=1,karst=1");
    }

    // Golden vectors — keep aligned with website scripts/cross-platform-url-vectors.mjs
    [TestMethod]
    public void CrossPlatformVector_ExploreViewportTerrain()
    {
        var entry = new ReferenceCaveIndexEntry
        {
            Id = "osm-node-1",
            Name = "Melissani Cave",
            Country = "Greece",
            Lat = 38.22,
            Lon = 20.62,
        };
        var url = ReferenceCatalogShareUrls.BuildExploreTerrainUrl(entry, preset: "terrain", zoom: 13);
        AssertCrossPlatformVector(
            url,
            "view=explore",
            "preset=terrain",
            "lat=38.22",
            "lon=20.62",
            "zoom=13",
            "country=greece",
            "ref=cave",
            "id=osm-node-1",
            "name=Melissani");
    }

    [TestMethod]
    public void CrossPlatformVector_ExploreHydrologyScout()
    {
        var entry = new ReferenceCaveIndexEntry
        {
            Id = "osm-node-1",
            Name = "Melissani Cave",
            Country = "Greece",
            Lat = 38.22,
            Lon = 20.62,
        };
        var url = ReferenceCatalogShareUrls.BuildExploreHydrologyScoutUrl(entry);
        AssertCrossPlatformVector(
            url,
            "view=explore",
            "preset=terrain",
            "zoom=14",
            "lat=38.22",
            "lon=20.62",
            "country=greece",
            "layers=hydrology=1,karst=1",
            "ref=cave",
            "id=osm-node-1");
    }

    [TestMethod]
    public void CrossPlatformVector_ExploreCompareTerrain()
    {
        var a = new ReferenceCaveIndexEntry { Id = "1", Country = "Greece", Lat = 38.0, Lon = 20.0 };
        var b = new ReferenceCaveIndexEntry { Id = "2", Country = "Greece", Lat = 38.5, Lon = 20.5 };
        var url = ReferenceCatalogShareUrls.BuildExploreCompareTerrainUrl(a, b);
        AssertCrossPlatformVector(url, "view=explore", "preset=terrain", "lat=38.25", "lon=20.25", "zoom=10", "country=greece");
    }

    [TestMethod]
    public void CrossPlatformVector_SurfaceWatchExpedition()
    {
        var url = ReferenceCatalogShareUrls.BuildSurfaceWatchUrl(38.22, 20.62, "Melissani Cave");
        AssertCrossPlatformVector(url, "view=watch", "lat=38.22", "lon=20.62", "name=Melissani");
        Assert.IsFalse(url.Contains("zoom=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ArchaeologicalNotes_FromPinShardFields()
    {
        var pin = new ReferenceCavePin
        {
            Id = "ref-arch",
            Name = "Franchthi Cave",
            Period = "Upper Palaeolithic",
            Significance = "Long Mesolithic sequence",
            Finds = "Marine shells",
            Heritage = "Protected site",
        };
        var notes = ReferenceCatalogDisplay.ArchaeologicalNotes(pin);
        Assert.AreEqual(4, notes.Count);
        Assert.AreEqual("Period", notes[0].Label);
    }

    [TestMethod]
    public void AuthorizedHttp_LocksIndexShardsFeatured_NotMetaOrManifest()
    {
        Assert.IsTrue(ReferenceCatalogAuthorizedHttp.IsEntitlementLockedDataUrl(
            "https://www.caveaipro.com/data/reference-caves-search-index.json"));
        Assert.IsTrue(ReferenceCatalogAuthorizedHttp.IsEntitlementLockedDataUrl(
            "https://www.caveaipro.com/data/featured-reference-caves.json"));
        Assert.IsTrue(ReferenceCatalogAuthorizedHttp.IsEntitlementLockedDataUrl(
            "https://www.caveaipro.com/data/reference-shards/greece.json?v=1"));
        Assert.IsFalse(ReferenceCatalogAuthorizedHttp.IsEntitlementLockedDataUrl(
            "https://www.caveaipro.com/data/reference-catalog-meta.json"));
        Assert.IsFalse(ReferenceCatalogAuthorizedHttp.IsEntitlementLockedDataUrl(
            "https://www.caveaipro.com/data/reference-shards/manifest.json"));
        Assert.IsFalse(ReferenceCatalogAuthorizedHttp.IsEntitlementLockedDataUrl(
            "https://caveaipro-5950e.web.app/data/reference-shards/manifest.json"));
    }

    private static void AssertCrossPlatformVector(string url, params string[] needles)
    {
        foreach (var needle in needles)
            StringAssert.Contains(url, needle);
    }

    [TestMethod]
    public void OsmIdJsonConverter_ignores_string_values()
    {
        const string json = """{"caves":[{"id":"x","caveName":"Test","lat":38.2,"lon":20.6,"osmId":"GR-THESEAS-R1"}]}""";
        var shard = JsonSerializer.Deserialize<ReferenceCountryShardFile>(json);
        Assert.IsNotNull(shard);
        Assert.AreEqual(1, shard!.Caves.Count);
        Assert.IsNull(shard.Caves[0].OsmId);
    }

    [TestMethod]
    public void ReferenceSurveyLinkService_create_survey_project_sets_link_and_coords()
    {
        var entry = new ReferenceCaveIndexEntry
        {
            Id = "ref-gr-test",
            Name = "Melissani",
            Lat = 38.22,
            Lon = 20.62,
            Country = "Greece",
        };
        var project = ReferenceSurveyLinkService.CreateSurveyProject(entry);
        Assert.AreEqual("Melissani", project.Name);
        Assert.AreEqual(38.22, project.Lat);
        Assert.IsTrue(ReferenceSurveyLinkService.TryGetLink(project, out var link));
        Assert.AreEqual("ref-gr-test", link!.Id);
        Assert.AreEqual("2", project.SurveyArchiveSchemaVersion);
        Assert.AreEqual("CAVE", project.SurveySiteType);
        Assert.IsFalse(string.IsNullOrWhiteSpace(project.LinkedLibraryCaveId));
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(project.Date, @"^\d{2}/\d{2}/\d{4}$"));
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(project.StartTime ?? "", @"^\d{2}:\d{2}$"));
        Assert.AreEqual(false, project.RequireVehicleParkStep);

        var mineEntry = new ReferenceCaveIndexEntry
        {
            Id = "ref-mine",
            Name = "Old Mine",
            Lat = 38,
            Lon = 22,
            CaveType = "historic mine",
        };
        var mine = ReferenceSurveyLinkService.CreateSurveyWorkspace(mineEntry);
        Assert.AreEqual("MINE", mine.Project.SurveySiteType);
        Assert.AreEqual(mine.Project.LinkedLibraryCaveId, mine.LibraryCard.Id);
        Assert.AreEqual("MINE", mine.LibraryCard.Type);
        Assert.IsFalse(string.IsNullOrWhiteSpace(mine.Project.ProjectId));
    }

    [TestMethod]
    public void FindExistingProject_matchesReferenceCatalogLinkId()
    {
        var entry = new ReferenceCaveIndexEntry
        {
            Id = "osm-node-42",
            Name = "Resume Cave",
            Lat = 38.2,
            Lon = 20.6,
            Country = "Greece",
        };
        var created = ReferenceSurveyLinkService.CreateSurveyWorkspace(entry);
        var other = new CaveProjectDocument { Name = "Other" };
        var found = ReferenceSurveyLinkService.FindExistingProject(
            new[] { other, created.Project },
            "OSM-NODE-42");
        Assert.AreSame(created.Project, found);
        Assert.IsNull(ReferenceSurveyLinkService.FindExistingProject(new[] { other }, "osm-node-42"));
    }

    [TestMethod]
    public void CaveLibraryJson_roundTripsWorkspaceCard()
    {
        var entry = new ReferenceCaveIndexEntry
        {
            Id = "ref-card",
            Name = "Card Cave",
            Lat = 39.1,
            Lon = 22.5,
            Country = "Greece",
            Region = "Thessaly",
        };
        var workspace = ReferenceSurveyLinkService.CreateSurveyWorkspace(entry);
        var bytes = CaveLibraryJsonLoader.SerializeToUtf8(new[] { workspace.LibraryCard });
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        var loaded = CaveLibraryJsonLoader.TryDeserializeKnownCaves(json);
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(workspace.LibraryCard.Id, loaded[0].Id);
        Assert.AreEqual("Card Cave", loaded[0].Name);
        Assert.AreEqual("Thessaly · Greece", loaded[0].Area);
    }

    [TestMethod]
    public void Greece_shard_deserializes_from_website_data()
    {
        var websiteRoot = Environment.GetEnvironmentVariable("CAVEAIPRO_WEBSITE_ROOT")
            ?? @"D:\CaveAIpro website";
        var path = Path.Combine(websiteRoot, "public", "data", "reference-shards", "greece.json");
        if (!File.Exists(path))
        {
            Assert.Inconclusive($"Greece shard not found at {path}");
            return;
        }

        var json = File.ReadAllText(path);
        var shard = JsonSerializer.Deserialize<ReferenceCountryShardFile>(json);
        Assert.IsNotNull(shard);
        Assert.IsTrue(shard!.Caves.Count > 500);
        Assert.IsTrue(shard.Caves.All(c => c.OsmId == null || c.OsmId > 0));
    }

    [TestMethod]
    public void IsCorruptIndexJson_detects_invalid_payload()
    {
        Assert.IsTrue(ReferenceCatalogFetchService.IsCorruptIndexJson("not json"));
        Assert.IsTrue(ReferenceCatalogFetchService.IsCorruptIndexJson("{\"version\":2,\"entries\":[]}"));
        Assert.IsFalse(ReferenceCatalogFetchService.IsCorruptIndexJson(
            "{\"version\":2,\"entries\":[{\"id\":\"a\",\"name\":\"A\",\"lat\":38.2,\"lon\":20.6}]}"));
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
