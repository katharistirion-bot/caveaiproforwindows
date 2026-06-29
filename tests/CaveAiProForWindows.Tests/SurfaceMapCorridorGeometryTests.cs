using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurfaceMap;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurfaceMapCorridorGeometryTests
{
    [TestMethod]
    public void TryBuildCorridor_returns_null_without_entrance()
    {
        var p = new CaveProjectDocument
        {
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Azimuth = 0, Clino = 0, Distance = 10 },
            ],
        };
        Assert.IsNull(SurfaceMapCorridorGeometry.TryBuildCorridor(p));
    }

    [TestMethod]
    public void TryBuildCorridor_returns_null_without_traverse_legs()
    {
        var p = new CaveProjectDocument
        {
            Lat = 40,
            Lon = 22,
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "-", Azimuth = 0, Clino = 0, Distance = 5 },
            ],
        };
        Assert.IsNull(SurfaceMapCorridorGeometry.TryBuildCorridor(p));
    }

    [TestMethod]
    public void OffsetLatLngMeters_north_leg_increases_latitude()
    {
        var from = new SurfaceMapCorridorGeometry.LatLonPoint(40.0, 22.0);
        var to = SurfaceMapCorridorGeometry.OffsetLatLngMeters(from, 0f, 0f, 100f);
        Assert.IsTrue(to.Lat > from.Lat, "North leg should increase latitude.");
        Assert.AreEqual(from.Lon, to.Lon, 1e-4);
        Assert.IsTrue(to.Lat - from.Lat is > 0.0008 and < 0.0010, $"Unexpected dLat={to.Lat - from.Lat}");
    }

    [TestMethod]
    public void OffsetLatLngMeters_applies_declination_to_bearing()
    {
        var from = new SurfaceMapCorridorGeometry.LatLonPoint(40.0, 22.0);
        var noDec = SurfaceMapCorridorGeometry.OffsetLatLngMeters(from, 0f, 0f, 100f);
        var withDec = SurfaceMapCorridorGeometry.OffsetLatLngMeters(from, 10f, 0f, 100f);
        Assert.IsTrue(withDec.Lon > noDec.Lon, "10 deg east declination should push lon east on azimuth 0.");
        Assert.IsTrue(withDec.Lat < noDec.Lat, "10 deg east declination should reduce north component.");
    }

    [TestMethod]
    public void TryBuildCorridor_multi_leg_chain_produces_one_linestring()
    {
        var p = new CaveProjectDocument
        {
            Lat = 40.0,
            Lon = 22.0,
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Azimuth = 0, Clino = 0, Distance = 50 },
                new ShotRecord { FromStation = "B", ToStation = "C", Azimuth = 90, Clino = 0, Distance = 50 },
            ],
        };

        var corridor = SurfaceMapCorridorGeometry.TryBuildCorridor(p);
        Assert.IsNotNull(corridor);
        Assert.AreEqual(1, corridor!.Features.Count);
        Assert.AreEqual(3, corridor.Features[0].Geometry.Coordinates.Count);
        Assert.AreEqual(22.0, corridor.Features[0].Geometry.Coordinates[0][0], 1e-9);
        Assert.AreEqual(40.0, corridor.Features[0].Geometry.Coordinates[0][1], 1e-9);
    }

    [TestMethod]
    public void TryBuildCorridor_skips_disconnected_branch_without_gps_anchor()
    {
        var p = new CaveProjectDocument
        {
            Lat = 40.0,
            Lon = 22.0,
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Azimuth = 0, Clino = 0, Distance = 20 },
                new ShotRecord { FromStation = "X", ToStation = "Y", Azimuth = 180, Clino = 0, Distance = 20 },
            ],
        };

        var corridor = SurfaceMapCorridorGeometry.TryBuildCorridor(p);
        Assert.IsNotNull(corridor);
        Assert.AreEqual(1, corridor!.Features.Count, "Only the entrance-connected chain is georeferenced.");
        Assert.AreEqual(2, corridor.Features[0].Geometry.Coordinates.Count);
    }

    [TestMethod]
    public void TryBuildCorridor_uses_profile_declination()
    {
        var p = new CaveProjectDocument
        {
            Lat = 40.0,
            Lon = 22.0,
            SurveyCalibrationProfile = new SurveyCalibrationProfileSnapshot
            {
                MagneticDeclinationAppliedDeg = 10f,
            },
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Azimuth = 0, Clino = 0, Distance = 100 },
            ],
        };

        var corridor = SurfaceMapCorridorGeometry.TryBuildCorridor(p);
        Assert.IsNotNull(corridor);
        var end = corridor!.Features[0].Geometry.Coordinates[^1];
        var noDec = SurfaceMapCorridorGeometry.OffsetLatLngMeters(
            new SurfaceMapCorridorGeometry.LatLonPoint(40, 22), 0f, 0f, 100f);
        var withDec = SurfaceMapCorridorGeometry.OffsetLatLngMeters(
            new SurfaceMapCorridorGeometry.LatLonPoint(40, 22), 10f, 0f, 100f);
        Assert.AreEqual(withDec.Lon, end[0], 1e-6);
        Assert.AreEqual(withDec.Lat, end[1], 1e-6);
        Assert.AreNotEqual(noDec.Lon, end[0], 1e-6);
    }
}