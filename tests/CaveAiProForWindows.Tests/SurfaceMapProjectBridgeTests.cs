using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurfaceMap;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurfaceMapProjectBridgeTests
{
    [TestMethod]
    public void BuildPayload_includes_surface_lidar_from_local_maps_folder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CaveAiProSurfaceLidarTest_" + Guid.NewGuid().ToString("N"));
        var mapsDir = Path.Combine(tempRoot, "maps", "xray");
        Directory.CreateDirectory(mapsDir);
        var rasterPath = Path.Combine(mapsDir, "surface_dsm.png");
        File.WriteAllBytes(rasterPath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        try
        {
            var project = new CaveProjectDocument
            {
                Name = "Lidar Test Cave",
                Lat = 38.1,
                Lon = 23.7,
                LoadedFromFile = Path.Combine(tempRoot, "data.json"),
            };
            project.SurfaceLidarRaster = JsonDocument.Parse(
                """
                {
                  "imageUri": "maps/xray/surface_dsm.png",
                  "southWestLat": 38.0,
                  "southWestLon": 23.6,
                  "northEastLat": 38.2,
                  "northEastLon": 23.8,
                  "opacity": 0.55
                }
                """).RootElement.Clone();

            var payload = SurfaceMapProjectBridge.BuildPayload(project, null, new SurfaceMapPersistedState());
            Assert.IsNotNull(payload.SurfaceLidarRaster);
            Assert.IsNotNull(payload.SurfaceLidarRaster!.ImageUrl);
            StringAssert.StartsWith(payload.SurfaceLidarRaster.ImageUrl, "https://caveai-surface-cache.local/");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void BuildPayload_sets_lidar_status_hint_when_file_missing()
    {
        var project = new CaveProjectDocument
        {
            Name = "Missing raster",
            Lat = 38.1,
            Lon = 23.7,
        };
        project.SurfaceLidarRaster = JsonDocument.Parse(
            """
            {
              "imageUri": "maps/surface_lidar.png",
              "southWestLat": 38.0,
              "southWestLon": 23.6,
              "northEastLat": 38.2,
              "northEastLon": 23.8
            }
            """).RootElement.Clone();

        var payload = SurfaceMapProjectBridge.BuildPayload(project, null, new SurfaceMapPersistedState());
        Assert.IsNull(payload.SurfaceLidarRaster);
        Assert.AreEqual("LiDAR image failed to load", payload.LidarStatusHint);
    }
}