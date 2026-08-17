using System.IO;
using System.Security.Cryptography;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Export;
using CaveAiProForWindows.Services.MeshImport;
using CaveAiProForWindows.Services.Security;
using CaveAiProForWindows.Services.SurveyAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AdvancedOfflineFeaturesTests
{
    private static CaveProjectDocument LoopProject() =>
        new()
        {
            Name = "LoopCave",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10, Azimuth = 0, Clino = 0, L = 1, R = 1, U = 1, D = 1 },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 10, Azimuth = 90, Clino = 0, L = 1, R = 1, U = 1, D = 1 },
                new ShotRecord { FromStation = "C", ToStation = "D", Distance = 10, Azimuth = 180, Clino = 0, L = 1, R = 1, U = 1, D = 1 },
                new ShotRecord { FromStation = "D", ToStation = "A", Distance = 10, Azimuth = 270, Clino = 0, L = 1, R = 1, U = 1, D = 1 },
            ],
        };

    [TestMethod]
    public void AnomalyScanner_flags_extreme_leg_length()
    {
        var project = LoopProject();
        project.Shots.Add(new ShotRecord
        {
            FromStation = "B",
            ToStation = "X",
            Distance = 120,
            Azimuth = 45,
            Clino = 0,
            L = 1,
            R = 1,
            U = 1,
            D = 1,
        });

        var findings = SurveyAnomalyScanner.Scan(project);
        Assert.IsTrue(findings.Any(f => f.Kind == SurveyAnomalyKind.LegDistance));
    }

    [TestMethod]
    public void LoopClosureAdjuster_detects_closed_loop()
    {
        var loops = SurveyLoopClosureAdjuster.DetectLoops(LoopProject());
        Assert.IsTrue(loops.Count >= 1);
        Assert.IsTrue(loops[0].MisclosureMeters >= 0);
    }

    [TestMethod]
    public void LoopClosureAdjuster_compass_rule_reduces_misclosure()
    {
        var result = SurveyLoopClosureAdjuster.Adjust(LoopProject(), LoopAdjustmentMethod.CompassRule);
        Assert.IsTrue(result.TotalMisclosureAfter <= result.TotalMisclosureBefore + 1e-6);
        Assert.IsTrue(result.AdjustedCoordinates.Count >= 4);
    }

    [TestMethod]
    public void ApplyToPlanOverrides_keepsManualOverrideOnStationsLoopDidNotMove()
    {
        var project = LoopProject();
        project.Shots.Add(new ShotRecord
        {
            FromStation = "B",
            ToStation = "E",
            Distance = 5,
            Azimuth = 45,
            Clino = 0,
            L = 1,
            R = 1,
            U = 1,
            D = 1,
        });
        project.PlanStationPositionOverrides["E"] = new PlanStationPositionOverride(99, 98, 97);
        project.Shots[3].Distance = 11;

        var result = SurveyLoopClosureAdjuster.Adjust(project, LoopAdjustmentMethod.CompassRule);
        SurveyLoopClosureAdjuster.ApplyToPlanOverrides(project, result);

        Assert.IsTrue(project.PlanStationPositionOverrides.TryGetValue("E", out var kept));
        Assert.AreEqual(99f, kept.X, 1e-4f);
        Assert.AreEqual(98f, kept.Y, 1e-4f);
        Assert.AreEqual(97f, kept.Z, 1e-4f);
    }

    [TestMethod]
    public void ObjMeshParser_reads_triangle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai_obj_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "tri.obj");
        try
        {
            File.WriteAllText(path,
                """
                v 0 0 0
                v 1 0 0
                v 0 1 0
                f 1 2 3
                """);
            var mesh = ObjMeshParser.ParseFile(path);
            Assert.AreEqual(3, mesh.Positions.Count);
            Assert.AreEqual(3, mesh.TriangleIndices.Count);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void KmlExporter_writes_document()
    {
        var path = Path.Combine(Path.GetTempPath(), "caveai_" + Guid.NewGuid().ToString("N") + ".kml");
        try
        {
            using (var w = new StreamWriter(path))
                SurveyKmlExporter.WritePlanKml(LoopProject(), w);

            var text = File.ReadAllText(path);
            Assert.IsTrue(text.Contains("<kml", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(text.Contains("LoopCave", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [TestMethod]
    public void SecureBundle_round_trip_encrypts_project()
    {
        var project = LoopProject();
        var path = Path.Combine(Path.GetTempPath(), "caveai_" + Guid.NewGuid().ToString("N") + CaveAiSecureBundleFormat.FileExtension);
        const string password = "test-password-123";
        try
        {
            CaveAiSecureBundleWriter.WriteBundle(project, path, password);
            Assert.IsTrue(File.Exists(path));

            var opened = CaveAiSecureBundleReader.OpenBundle(path, password);
            Assert.AreEqual(1, opened.Projects.Count);
            Assert.AreEqual("LoopCave", opened.Projects[0].Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [TestMethod]
    public void SmartLabelLayout_resolves_overlapping_boxes()
    {
        var initial = new List<SurveyMapSmartLabelLayout.LabelPlacement>
        {
            new("A", new System.Windows.Rect(10, 10, 60, 24), new System.Windows.Point(10, 10)),
            new("B", new System.Windows.Rect(12, 12, 60, 24), new System.Windows.Point(12, 12)),
            new("C", new System.Windows.Rect(14, 14, 60, 24), new System.Windows.Point(14, 14)),
        };

        var result = SurveyMapSmartLabelLayout.Resolve(initial);
        Assert.IsTrue(result.IterationsUsed >= 1);
        Assert.IsTrue(result.Placements.Any(p => Math.Abs(p.Bounds.X - 10) > 0.01 || Math.Abs(p.Bounds.Y - 10) > 0.01));
    }
}
