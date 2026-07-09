using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SurveyLoopQc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class SurveyLoopQcContractTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "survey-loop-qc-fixture.json");

    [TestMethod]
    public void Fixture_cases_match_web_contract()
    {
        var json = File.ReadAllText(FixturePath);
        using var doc = JsonDocument.Parse(json);
        foreach (var caseEl in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = caseEl.GetProperty("id").GetString()!;
            if (caseEl.TryGetProperty("project", out var projectEl))
            {
                var project = ParseProject(projectEl);
                var loops = SurveyLoopQcAnalyzer.AnalyzeLoops(project).ToList();
                var expect = caseEl.GetProperty("expect");

                if (expect.TryGetProperty("loopCount", out var lc))
                    Assert.AreEqual(lc.GetInt32(), loops.Count, id);

                if (expect.TryGetProperty("loopCountMin", out var lcm))
                    Assert.IsTrue(loops.Count >= lcm.GetInt32(), id);

                if (loops.Count > 0)
                {
                    var worst = loops.OrderByDescending(l => l.MisclosureMeters).First();
                    if (expect.TryGetProperty("worstMisclosureMin", out var wmin))
                        Assert.IsTrue(worst.MisclosureMeters >= wmin.GetDouble(), id);
                    if (expect.TryGetProperty("worstMisclosureMax", out var wmax))
                        Assert.IsTrue(worst.MisclosureMeters <= wmax.GetDouble(), id);
                    if (expect.TryGetProperty("worstSeverity", out var sev))
                    {
                        Assert.AreEqual(
                            sev.GetString(),
                            SurveyLoopQcSummary.SeverityLabel(worst.MisclosureMeters, worst.PathLengthMeters),
                            id);
                    }
                }

                if (expect.TryGetProperty("summaryContains", out var sub))
                {
                    var summary = SurveyLoopQcSummary.BuildMisclosureSummary(loops);
                    StringAssert.Contains(summary, sub.GetString(), id);
                }
            }

            if (caseEl.TryGetProperty("misclosureM", out var m) &&
                caseEl.TryGetProperty("pathLengthM", out var pl))
            {
                var severity = SurveyLoopQcSummary.SeverityLabel(m.GetDouble(), pl.GetDouble());
                var expected = caseEl.GetProperty("expect").GetProperty("severity").GetString();
                Assert.AreEqual(expected, severity, id);
            }
        }
    }

    [TestMethod]
    public void Triangle_well_closed_detects_excellent_loop()
    {
        var project = new CaveProjectDocument
        {
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10, Azimuth = 0, Clino = 0 },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 10, Azimuth = 90, Clino = 0 },
                new ShotRecord { FromStation = "C", ToStation = "A", Distance = 14.14f, Azimuth = 225, Clino = 0 },
            ],
        };
        var loops = SurveyLoopQcAnalyzer.AnalyzeLoops(project);
        Assert.IsTrue(loops.Count >= 1);
        var worst = loops.MaxBy(l => l.MisclosureMeters)!;
        Assert.IsTrue(worst.MisclosureMeters < 0.05);
        Assert.AreEqual("excellent", SurveyLoopQcSummary.SeverityLabel(worst.MisclosureMeters, worst.PathLengthMeters));
    }

    [TestMethod]
    public void TraverseQcStats_includes_loop_summary_row()
    {
        var project = new CaveProjectDocument
        {
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10, Azimuth = 0, Clino = 0 },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 10, Azimuth = 90, Clino = 0 },
                new ShotRecord { FromStation = "C", ToStation = "A", Distance = 14.14f, Azimuth = 225, Clino = 0 },
            ],
        };
        var summary = TraverseQcStats.BuildSummaryText(project);
        StringAssert.Contains(summary, "Loops:");
        var rows = TraverseQcStats.BuildSurveyQcIssueRows(project);
        Assert.IsTrue(rows.Any(r => r.Category == "Loop QC" && r.Message.Contains("detected")));
    }

    private static CaveProjectDocument ParseProject(JsonElement projectEl)
    {
        var shots = new List<ShotRecord>();
        foreach (var shot in projectEl.GetProperty("shots").EnumerateArray())
        {
            shots.Add(new ShotRecord
            {
                FromStation = shot.GetProperty("fromStation").GetString() ?? "",
                ToStation = shot.GetProperty("toStation").GetString() ?? "",
                Azimuth = (float)shot.GetProperty("azimuth").GetDouble(),
                Clino = (float)shot.GetProperty("clino").GetDouble(),
                Distance = (float)shot.GetProperty("distance").GetDouble(),
            });
        }
        return new CaveProjectDocument { Shots = shots };
    }
}
