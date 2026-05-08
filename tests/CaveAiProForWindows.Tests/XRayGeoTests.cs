using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class XRayGeoTests
{
    [TestMethod]
    public void XRayBackdropMetadataParser_reads_explicit_min_max_object()
    {
        var p = new CaveProjectDocument
        {
            XrayBackdropImageBounds = JsonDocument.Parse(
                """{"minLat":40.0,"maxLat":40.01,"minLon":22.0,"maxLon":22.02}""").RootElement,
        };
        var md = XRayBackdropMetadataParser.TryRead(p);
        Assert.IsNotNull(md);
        Assert.IsTrue(md!.IsValid);
        Assert.AreEqual(40.0, md.MinLat, 1e-9);
        Assert.AreEqual(22.02, md.MaxLon, 1e-9);
    }

    [TestMethod]
    public void XRayBackdropMetadataParser_reads_northEast_southWest_corners()
    {
        var p = new CaveProjectDocument
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["xrayBackdropImageBounds"] = JsonDocument.Parse(
                    """{"northEast":{"lat":40.5,"lon":22.5},"southWest":{"lat":40.4,"lon":22.4}}""").RootElement,
            },
        };
        var md = XRayBackdropMetadataParser.TryRead(p);
        Assert.IsNotNull(md);
        Assert.AreEqual(40.4, md!.MinLat, 1e-9);
        Assert.AreEqual(40.5, md.MaxLat, 1e-9);
        Assert.AreEqual(22.4, md.MinLon, 1e-9);
        Assert.AreEqual(22.5, md.MaxLon, 1e-9);
    }

    [TestMethod]
    public void XRayBackdropMetadataParser_supports_center_zoom_image_size()
    {
        var p = new CaveProjectDocument
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["xrayBackdropCenterLat"] = JsonDocument.Parse("40.0").RootElement,
                ["xrayBackdropCenterLon"] = JsonDocument.Parse("22.0").RootElement,
                ["xrayBackdropZoom"] = JsonDocument.Parse("18").RootElement,
                ["xrayBackdropImageWidthPx"] = JsonDocument.Parse("1024").RootElement,
                ["xrayBackdropImageHeightPx"] = JsonDocument.Parse("1024").RootElement,
            },
        };
        var md = XRayBackdropMetadataParser.TryRead(p);
        Assert.IsNotNull(md);
        Assert.IsTrue(md!.IsValid);
        // Image is centered → center should round-trip.
        Assert.AreEqual(40.0, md.CenterLat, 1e-6);
        Assert.AreEqual(22.0, md.CenterLon, 1e-6);
        // At zoom 18, ~0.6 m/px at lat 40 — image is roughly 600×600 m.
        var spanMetres = md.SpanLatDeg * XRayProjection.MetresPerDegreeLatitude;
        Assert.IsTrue(spanMetres is > 400 and < 800, $"spanMetres={spanMetres}");
    }

    [TestMethod]
    public void XRayProjection_geo_aligned_entrance_lands_on_center_when_bbox_centered_on_entrance()
    {
        var bbox = new XRayBackdropMetadata(
            MinLat: 39.999,
            MaxLat: 40.001,
            MinLon: 21.999,
            MaxLon: 22.001,
            SourceLabel: "test");
        var displayRect = new Rect(50, 30, 800, 800);
        var geo = XRayProjection.Build(40.0, 22.0, bbox, 1024, 1024, displayRect);

        var entrance = geo.WorldMetresToCanvas(0, 0);
        Assert.AreEqual(displayRect.X + displayRect.Width / 2, entrance.X, 1e-6);
        Assert.AreEqual(displayRect.Y + displayRect.Height / 2, entrance.Y, 1e-6);
    }

    [TestMethod]
    public void XRayProjection_north_offset_moves_canvas_y_upwards()
    {
        var bbox = new XRayBackdropMetadata(MinLat: 39.99, MaxLat: 40.01, MinLon: 21.99, MaxLon: 22.01, "t");
        var displayRect = new Rect(0, 0, 1000, 1000);
        var geo = XRayProjection.Build(40.0, 22.0, bbox, 1000, 1000, displayRect);
        var entrance = geo.WorldMetresToCanvas(0, 0);
        var north100 = geo.WorldMetresToCanvas(0, 100);
        Assert.IsTrue(north100.Y < entrance.Y, "+100 m north should move pixel UP (smaller Y).");
        var east100 = geo.WorldMetresToCanvas(100, 0);
        Assert.IsTrue(east100.X > entrance.X, "+100 m east should move pixel RIGHT.");
    }

    [TestMethod]
    public void XRayProjection_letterbox_rect_handles_wide_host()
    {
        var rect = XRayProjection.ComputeImageDisplayRectInCanvas(
            imagePxW: 1000, imagePxH: 1000, hostW: 2000, hostH: 1000);
        Assert.AreEqual(1000, rect.Width, 1e-6);
        Assert.AreEqual(1000, rect.Height, 1e-6);
        Assert.AreEqual(500, rect.X, 1e-6);
        Assert.AreEqual(0, rect.Y, 1e-6);
    }

    [TestMethod]
    public void XRayProjection_letterbox_rect_handles_tall_host()
    {
        var rect = XRayProjection.ComputeImageDisplayRectInCanvas(
            imagePxW: 1000, imagePxH: 500, hostW: 800, hostH: 1000);
        Assert.AreEqual(800, rect.Width, 1e-6);
        Assert.AreEqual(400, rect.Height, 1e-6);
        Assert.AreEqual(0, rect.X, 1e-6);
        Assert.AreEqual(300, rect.Y, 1e-6);
    }
}
