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
        Assert.IsTrue(urls.Exists(u => u.Contains("copernicus_dsm_glo30", StringComparison.Ordinal)));
        Assert.IsTrue(urls.Exists(u => u.Contains("elevation-tiles-prod/terrarium", StringComparison.Ordinal)));
        Assert.IsTrue(urls.Exists(u => u.Contains("demotiles.maplibre.org/font", StringComparison.Ordinal)));
        Assert.IsTrue(urls.Exists(u => u.EndsWith(".pbf", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void GuessContentType_pbfIsProtobufNotPng()
    {
        Assert.AreEqual(
            "application/x-protobuf",
            SurfaceMapTileCacheService.GuessContentType(
                "https://demotiles.maplibre.org/font/Open%20Sans%20Regular/0-255.pbf"));
        Assert.AreEqual(
            "image/png",
            SurfaceMapTileCacheService.GuessContentType("https://tile.openstreetmap.org/12/1/2.png"));
    }

    [TestMethod]
    public void CacheMissPlaceholder_neverServesPngAsGlyphProtobuf()
    {
        var glyph = "https://demotiles.maplibre.org/font/Open%20Sans%20Regular/0-255.pbf";
        Assert.IsFalse(
            SurfaceMapTileCacheService.TryCacheMissPlaceholder(
                glyph, cacheOnly: false, out var onlineStatus, out var onlineBody, out var onlineType));
        Assert.AreEqual(0, onlineStatus);
        Assert.AreEqual(0, onlineBody.Length);
        Assert.AreEqual("application/x-protobuf", onlineType);

        Assert.IsTrue(
            SurfaceMapTileCacheService.TryCacheMissPlaceholder(
                glyph, cacheOnly: true, out var offlineStatus, out var offlineBody, out var offlineType));
        Assert.AreEqual(404, offlineStatus);
        Assert.AreEqual(0, offlineBody.Length);
        Assert.AreEqual("application/x-protobuf", offlineType);
    }

    [TestMethod]
    public void CacheMissPlaceholder_imageTilesStayEmptyPng()
    {
        Assert.IsTrue(
            SurfaceMapTileCacheService.TryCacheMissPlaceholder(
                "https://tile.openstreetmap.org/12/1/2.png",
                cacheOnly: true,
                out var status,
                out var body,
                out var contentType));
        Assert.AreEqual(200, status);
        Assert.AreEqual("image/png", contentType);
        Assert.IsTrue(body.Length > 8);
        Assert.AreEqual(0x89, body[0]);
        Assert.AreEqual(0x50, body[1]);
        Assert.AreEqual(0x4E, body[2]);
        Assert.AreEqual(0x47, body[3]);
    }

    [TestMethod]
    public void CacheMissPlaceholder_demTilesAreNotEmptyPng()
    {
        var dem = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/12/1/2.png";
        Assert.IsTrue(
            SurfaceMapTileCacheService.TryCacheMissPlaceholder(
                dem, cacheOnly: true, out var status, out var body, out var contentType));
        Assert.AreEqual(404, status);
        Assert.AreEqual(0, body.Length);
        Assert.AreEqual("image/png", contentType);
    }

    [TestMethod]
    public void IsValidCachedPayload_rejectsHtmlErrorPagesAndTinyDem()
    {
        var glyph = "https://demotiles.maplibre.org/font/Open%20Sans%20Regular/0-255.pbf";
        Assert.IsFalse(SurfaceMapTileCacheService.IsValidCachedPayload(glyph, "<html>no</html>"u8.ToArray()));
        Assert.IsFalse(SurfaceMapTileCacheService.IsValidCachedPayload(glyph, "{}"u8.ToArray()));
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.IsTrue(
            SurfaceMapTileCacheService.IsValidCachedPayload("https://tile.openstreetmap.org/1/1/1.png", png));
        Assert.IsFalse(
            SurfaceMapTileCacheService.IsValidCachedPayload(glyph, png));
        Assert.IsFalse(
            SurfaceMapTileCacheService.IsValidCachedPayload(
                "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/1/1/1.png",
                png));
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