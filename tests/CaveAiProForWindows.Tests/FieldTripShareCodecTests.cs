using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.FieldTrip;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class FieldTripShareCodecTests
{
    [TestMethod]
    public void RoundTrip_encodes_and_decodes_v1_payload()
    {
        var stops = new List<FieldTripStop>
        {
            new()
            {
                ReferenceId = "ref-osm-1",
                Name = "Alpha Cave",
                Lat = 39.07421,
                Lon = 21.82431,
                Country = "Greece",
            },
            new()
            {
                CommunityDocId = "uid_abc123",
                Name = "Community Site",
                Lat = 39.1,
                Lon = 21.9,
                Country = "Greece",
            },
        };

        var url = FieldTripShareCodec.BuildShareUrl(stops);
        StringAssert.Contains(url, "https://www.caveaipro.com/map?view=fieldtrip#trip=");
        StringAssert.Contains(url, "#trip=");

        var payload = FieldTripShareCodec.TryParseFromUrl(url);
        Assert.IsNotNull(payload);
        Assert.AreEqual(1, payload!.V);
        Assert.AreEqual(2, payload.Stops.Count);
        Assert.AreEqual("r", payload.Stops[0].K);
        Assert.AreEqual("ref-osm-1", payload.Stops[0].I);
        Assert.AreEqual("p", payload.Stops[1].K);
        Assert.AreEqual("uid_abc123", payload.Stops[1].I);

        var roundTrip = FieldTripShareCodec.ToFieldTripStops(payload);
        Assert.AreEqual(2, roundTrip.Count);
        Assert.AreEqual("ref-osm-1", roundTrip[0].ReferenceId);
        Assert.AreEqual("uid_abc123", roundTrip[1].CommunityDocId);
    }

    [TestMethod]
    public void StopImporter_ParsesPublishedCaveUrl()
    {
        var stop = FieldTripStopImporter.TryParseInput("https://www.caveaipro.com/map?cave=doc_xyz");
        Assert.IsNotNull(stop);
        Assert.AreEqual("doc_xyz", stop!.CommunityDocId);
    }

    [TestMethod]
    public void StopImporter_ParsesRawDocId()
    {
        var stop = FieldTripStopImporter.TryParseInput("uid_community_42");
        Assert.IsNotNull(stop);
        Assert.AreEqual("uid_community_42", stop!.CommunityDocId);
    }
}
