using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class FieldReturnGuideTests
{
    [TestMethod]
    public void Distance_due_north_is_about_one_degree()
    {
        var meters = FieldReturnGuide.DistanceMeters(38.0, 23.0, 39.0, 23.0);
        Assert.IsTrue(meters > 110_000 && meters < 112_000, meters.ToString());
    }

    [TestMethod]
    public void Bearing_due_north_is_zero()
    {
        var bearing = FieldReturnGuide.BearingDegrees(38.0, 23.0, 39.0, 23.0);
        Assert.AreEqual(0, bearing, 1.0);
    }

    [TestMethod]
    public void Fix_older_than_25_seconds_is_lost()
    {
        var now = DateTimeOffset.Parse("2026-11-07T10:00:00Z");
        Assert.IsFalse(FieldReturnGuide.GpsLost(now.AddSeconds(-10), now));
        Assert.IsTrue(FieldReturnGuide.GpsLost(now.AddSeconds(-26), now));
    }
}
