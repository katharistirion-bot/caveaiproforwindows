using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class CaveAiOfflineBrainTests
{
    [TestMethod]
    public void Answer_depth_usesProjectGeometry()
    {
        var project = new CaveProjectDocument
        {
            Name = "Test",
            SurveySiteType = "CAVE",
            Alt = 100,
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A1",
                    ToStation = "A2",
                    Distance = 10,
                    Azimuth = 0,
                    Clino = -45,
                },
            ],
        };

        var reply = CaveAiOfflineBrain.Answer(project, "what is the max depth?");
        StringAssert.Contains(reply, "depth");
    }

    [TestMethod]
    public void Answer_siteType_reportsMine()
    {
        var project = new CaveProjectDocument
        {
            Name = "Quarry",
            SurveySiteType = "MINE",
        };

        var reply = CaveAiOfflineBrain.Answer(project, "what site type is this?");
        StringAssert.Contains(reply, "Mine");
    }

    [TestMethod]
    public void Answer_lastLeg_reportsLatestMain()
    {
        var project = new CaveProjectDocument
        {
            Name = "Demo",
            Shots =
            [
                new ShotRecord { FromStation = "A1", ToStation = "A2", Distance = 18, Azimuth = 0, Clino = -12 },
            ],
        };
        var reply = CaveAiOfflineBrain.Answer(project, "what was the last leg");
        StringAssert.Contains(reply, "Last main leg");
        StringAssert.Contains(reply, "A1");
    }

    [TestMethod]
    public void Answer_volume_usesLrudPrism()
    {
        var project = new CaveProjectDocument
        {
            Name = "Demo",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A1",
                    ToStation = "A2",
                    Distance = 10,
                    Azimuth = 0,
                    Clino = 0,
                    L = 1,
                    R = 1,
                    U = 1,
                    D = 1,
                },
            ],
        };
        var reply = CaveAiOfflineBrain.Answer(project, "what is the passage volume");
        StringAssert.Contains(reply, "volume");
        StringAssert.Contains(reply, "m³");
    }

    [TestMethod]
    public void Answer_clino_reportsLatestMain()
    {
        var project = new CaveProjectDocument
        {
            Name = "Demo",
            Shots =
            [
                new ShotRecord { FromStation = "A1", ToStation = "A2", Distance = 10, Azimuth = 0, Clino = -25 },
            ],
        };
        var reply = CaveAiOfflineBrain.Answer(project, "what is the latest clino");
        StringAssert.Contains(reply, "clino");
        StringAssert.Contains(reply, "-25");
    }

    [TestMethod]
    public void ProjectDocument_surveySiteType_roundTripsToken()
    {
        var project = new CaveProjectDocument { Name = "Quarry", SurveySiteType = "MINE" };
        Assert.AreEqual("MINE", project.SurveySiteType);
        Assert.AreEqual("Mine", SurveySiteType.GetMapLabel(SurveySiteType.Parse(project.SurveySiteType)));
    }

    [TestMethod]
    public void Answer_loopMisclosure_reportsDetectedLoop()
    {
        var project = new CaveProjectDocument
        {
            Name = "Loop Demo",
            Shots =
            [
                new ShotRecord { FromStation = "A1", ToStation = "A2", Distance = 10, Azimuth = 0, Clino = 0 },
                new ShotRecord { FromStation = "A2", ToStation = "A3", Distance = 10, Azimuth = 90, Clino = 0 },
                new ShotRecord { FromStation = "A3", ToStation = "A1", Distance = 14.2f, Azimuth = 225, Clino = 0 },
            ],
        };
        var reply = CaveAiOfflineBrain.Answer(project, "what is the loop misclosure");
        StringAssert.Contains(reply, "loop");
        StringAssert.Contains(reply, "m");
    }

    [TestMethod]
    public void Fixture_cases_produceExpectedAnswerFragments()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "android-reference", "cave-ai-offline-intents-fixture.json");
        path = Path.GetFullPath(path);
        Assert.IsTrue(File.Exists(path), $"Missing fixture: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var cases = doc.RootElement.GetProperty("cases");
        var i = 0;
        foreach (var c in cases.EnumerateArray())
        {
            var intentId = c.GetProperty("intentId").GetString() ?? "";
            var query = c.GetProperty("query").GetString() ?? "";
            var project = BuildProjectFromFixture(c.GetProperty("project"));
            var reply = CaveAiOfflineBrain.Answer(project, query);
            foreach (var expected in c.GetProperty("expectedContains").EnumerateArray())
            {
                var fragment = expected.GetString() ?? "";
                StringAssert.Contains(reply, fragment, $"case {i} ({intentId}): {reply}");
            }
            i++;
        }
    }

    private static CaveProjectDocument BuildProjectFromFixture(JsonElement projectEl)
    {
        var project = new CaveProjectDocument
        {
            Name = projectEl.GetProperty("name").GetString() ?? "",
            SurveySiteType = projectEl.TryGetProperty("surveySiteType", out var st) ? st.GetString() : null,
            Alt = projectEl.TryGetProperty("alt", out var alt) ? alt.GetDouble() : 0,
        };

        if (projectEl.TryGetProperty("lat", out var lat) && lat.ValueKind == JsonValueKind.Number)
            project.Lat = lat.GetDouble();
        if (projectEl.TryGetProperty("lon", out var lon) && lon.ValueKind == JsonValueKind.Number)
            project.Lon = lon.GetDouble();

        if (projectEl.TryGetProperty("shots", out var shotsEl) && shotsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var shotEl in shotsEl.EnumerateArray())
            {
                project.Shots.Add(new ShotRecord
                {
                    FromStation = shotEl.GetProperty("fromStation").GetString() ?? "",
                    ToStation = shotEl.GetProperty("toStation").GetString() ?? "",
                    Distance = shotEl.GetProperty("distance").GetSingle(),
                    Azimuth = shotEl.GetProperty("azimuth").GetSingle(),
                    Clino = shotEl.GetProperty("clino").GetSingle(),
                    L = shotEl.TryGetProperty("l", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetSingle() : 0,
                    R = shotEl.TryGetProperty("r", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetSingle() : 0,
                    U = shotEl.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetSingle() : 0,
                    D = shotEl.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetSingle() : 0,
                });
            }
        }

        if (projectEl.TryGetProperty("rocks", out var rocksEl) && rocksEl.ValueKind == JsonValueKind.Array)
            project.Rocks = rocksEl.Clone();

        return project;
    }
}
