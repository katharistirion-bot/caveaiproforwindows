using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
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

    [TestMethod]
    public void BuildPayload_null_project_has_no_survey_overlays()
    {
        var payload = SurfaceMapProjectBridge.BuildPayload(null, null, WorkspaceSessionReset.FreshSurfaceMapState());
        Assert.IsNull(payload.SurveyCorridor);
        Assert.IsNull(payload.Lat);
        Assert.IsNull(payload.Lon);
        Assert.IsNull(payload.SurfaceLidarRaster);
    }

    [TestMethod]
    public void UnloadThenLoadProjectB_doesNotRetainProjectA_corridor()
    {
        var projectA = BuildProjectWithEntrance("Cave A", 40.12, 22.45);
        var projectB = BuildProjectWithEntrance("Cave B", 38.02, 23.72);

        var corridorA = SurfaceMapCorridorGeometry.TryBuildCorridor(projectA);
        var payloadB = SurfaceMapProjectBridge.BuildPayload(
            projectB,
            null,
            WorkspaceSessionReset.FreshSurfaceMapState());

        Assert.IsNotNull(corridorA);
        Assert.IsNotNull(payloadB.SurveyCorridor);

        var startA = corridorA!.Features[0].Geometry.Coordinates[0];
        var startB = payloadB.SurveyCorridor!.Features[0].Geometry.Coordinates[0];
        Assert.AreNotEqual(startA[0], startB[0], 1e-4);
        Assert.AreNotEqual(startA[1], startB[1], 1e-4);
    }

    private static CaveProjectDocument BuildProjectWithEntrance(string name, double lat, double lon) =>
        new()
        {
            Name = name,
            Lat = lat,
            Lon = lon,
            Shots =
            [
                new ShotRecord { FromStation = "A1", ToStation = "A2", Azimuth = 0, Clino = 0, Distance = 25 },
                new ShotRecord { FromStation = "A2", ToStation = "A3", Azimuth = 90, Clino = 0, Distance = 30 },
            ],
        };
}