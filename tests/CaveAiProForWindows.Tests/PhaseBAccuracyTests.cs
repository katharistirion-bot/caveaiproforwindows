using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class PhaseBAccuracyTests
{
    [TestMethod]
    public void LoopClosureSeverityClassifier_good_moderate_large()
    {
        Assert.AreEqual(LoopClosureSeverity.Good, LoopClosureSeverityClassifier.Classify(0.04));
        Assert.AreEqual(LoopClosureSeverity.Moderate, LoopClosureSeverityClassifier.Classify(0.35));
        Assert.AreEqual(LoopClosureSeverity.Large, LoopClosureSeverityClassifier.Classify(1.2));
    }

    [TestMethod]
    public void LoopClosureSeverityClassifier_formats_mm_and_metres()
    {
        Assert.AreEqual("8 mm", LoopClosureSeverityClassifier.FormatMisclosureLabel(0.008));
        Assert.AreEqual("0.12 m", LoopClosureSeverityClassifier.FormatMisclosureLabel(0.12));
        Assert.AreEqual("1.25 m", LoopClosureSeverityClassifier.FormatMisclosureLabel(1.25));
    }

    [TestMethod]
    public void LoopClosureHighlighter_includes_severity_and_error_label()
    {
        var project = new CaveProjectDocument
        {
            Name = "Loop cave",
            Shots =
            [
                new ShotRecord { FromStation = "1", ToStation = "2", Distance = 10, Azimuth = 0, Clino = 0 },
                new ShotRecord { FromStation = "2", ToStation = "3", Distance = 10, Azimuth = 90, Clino = 0 },
                new ShotRecord { FromStation = "3", ToStation = "1", Distance = 12, Azimuth = 225, Clino = 0 },
            ],
        };

        var loops = SurveyLoopClosureHighlighter.Detect(project);
        Assert.IsTrue(loops.Count > 0);
        var loop = loops[0];
        Assert.IsTrue(loop.MisclosureMeters >= 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(loop.Label));
        Assert.IsTrue(loop.Label.Contains("loop", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(
            LoopClosureSeverityClassifier.Classify(loop.MisclosureMeters),
            loop.Severity);
        Assert.IsTrue(loop.Label.Contains(
            LoopClosureSeverityClassifier.SeverityCaption(loop.Severity),
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LrudRibbonQcScanner_flags_missing_lrud_on_long_leg()
    {
        var project = new CaveProjectDocument
        {
            Name = "QC cave",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 5, Azimuth = 0, Clino = 0 },
            ],
        };
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var issues = LrudRibbonQcScanner.Scan(project, coords, [], 0, 10, 0, 10);

        Assert.IsTrue(issues.Any(i => i.Kind == LrudRibbonQcKind.MissingLrud));
    }

    [TestMethod]
    public void LrudRibbonQcScanner_flags_degenerate_wall_polyline()
    {
        var project = new CaveProjectDocument { Name = "Wall QC" };
        var coords = new Dictionary<string, SurveyStationGeometry.StationPlanCoords>(StringComparer.OrdinalIgnoreCase);
        var walls = new[]
        {
            new SurveyStationGeometry.PlanVectorPolyline("sketch", [(1f, 2f)], Closed: false),
        };
        var issues = LrudRibbonQcScanner.Scan(project, coords, walls, 0, 5, 0, 5);

        Assert.IsTrue(issues.Any(i => i.Kind == LrudRibbonQcKind.DegenerateWall));
    }

    [DataTestMethod]
    [DataRow(2, 100, 1)]
    [DataRow(10, 500, 10)]
    [DataRow(0.5, 200, 50)]
    public void SurveyCoordinateGridRenderer_picks_nice_spacing(double pxPerMetre, double span, double expectedMin)
    {
        var spacing = SurveyCoordinateGridRenderer.ResolveGridSpacingMetres(pxPerMetre, span);
        Assert.IsTrue(spacing >= expectedMin);
        Assert.IsTrue(new[] { 1.0, 2.0, 5.0, 10.0, 20.0, 50.0, 100.0, 200.0, 500.0, 1000.0 }.Contains(spacing));
    }
}
