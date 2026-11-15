using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyExpeditionQcPacketTests
{
    [TestMethod]
    public void Packet_contains_raw_legs_adjusted_stations_and_loops()
    {
        var project = new CaveProjectDocument
        {
            Name = "Packet Cave",
            Date = "2026-10-24",
            Alt = 100,
            Shots =
            [
                new ShotRecord { FromStation = "1", ToStation = "2", Distance = 10f, Azimuth = 0f, Clino = 0f },
                new ShotRecord { FromStation = "2", ToStation = "3", Distance = 5f, Azimuth = 90f, Clino = -10f },
            ],
            SurveyCalibrationProfile = new SurveyCalibrationProfileSnapshot
            {
                ProfileName = "DistoX",
                MeanGravityError = 0.06f,
                MeanMagneticError = 0.01f,
                FitQuality = "weak",
            },
        };

        var text = Encoding.UTF8.GetString(SurveyExpeditionQcPacket.BuildUtf8Bom(project));
        StringAssert.Contains(text, "## Raw legs");
        StringAssert.Contains(text, "1,2,10,0,0,");
        StringAssert.Contains(text, "## Adjusted stations");
        StringAssert.Contains(text, "## Loop closure");
        StringAssert.Contains(text, "No closed loops detected.");
        StringAssert.Contains(text, "Weak fit");
        StringAssert.Contains(text, "2,");
    }

    [TestMethod]
    public void Add_leg_continues_from_the_last_station()
    {
        var shots = new List<ShotRecord>
        {
            new() { FromStation = "A1", ToStation = "A2", Distance = 1f },
        };
        var next = SurveyBookLegDraft.CreateNext(shots);
        Assert.AreEqual("A2", next.FromStation);
        Assert.AreEqual("A3", next.ToStation);
        Assert.AreEqual(0f, next.Distance);
    }

    [TestMethod]
    public void Calibration_line_is_visible_in_the_summary()
    {
        var project = new CaveProjectDocument
        {
            Name = "Cal",
            Shots =
            [
                new ShotRecord { FromStation = "1", ToStation = "2", Distance = 4f, Azimuth = 0f, Clino = 0f },
            ],
            SurveyCalibrationProfile = new SurveyCalibrationProfileSnapshot
            {
                MeanGravityError = 0.012f,
                MeanMagneticError = 0.008f,
                FitQuality = "good",
            },
        };
        var summary = ExplorationAnalytics.BuildSummaryText(project);
        StringAssert.Contains(summary, "gravity error 0.012");
        StringAssert.Contains(summary, "Good fit");
        var qc = TraverseQcStats.BuildSurveyQcIssueRows(project);
        Assert.IsTrue(qc.Any(r => r.Message.Contains("Good fit", StringComparison.Ordinal)));
    }
}
