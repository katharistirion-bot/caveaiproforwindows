using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SurveyAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

/// <summary>
/// End-to-end style smoke: Network anchors Apply → network LS → Survex/Therion bytes.
/// Survex <c>cavern</c> is not required on the build machine.
/// </summary>
[TestClass]
public sealed class NetworkAnchorsSmokeTests
{
    [TestMethod]
    public void Apply_rejects_partial_xyz_and_accepts_hold_only()
    {
        var project = MinimalLoopProject();
        var bad = NetworkAnchorsApplier.TryApply(
            project,
            Array.Empty<NetworkAnchorsApplier.EquateDraft>(),
            [new NetworkAnchorsApplier.FixedDraft("A", true, "1", "", "")]);
        Assert.IsFalse(bad.Ok);
        Assert.IsNotNull(bad.ErrorMessage);

        var ok = NetworkAnchorsApplier.TryApply(
            project,
            [new NetworkAnchorsApplier.EquateDraft("A", "A_alias")],
            [
                new NetworkAnchorsApplier.FixedDraft("A", true, "", "", ""),
                new NetworkAnchorsApplier.FixedDraft("C", true, "10", "0", "0"),
            ]);
        Assert.IsTrue(ok.Ok, ok.ErrorMessage);
        Assert.AreEqual(1, project.StationEquates!.Count);
        Assert.AreEqual("A", project.StationEquates[0].StationA);
        Assert.AreEqual("A_alias", project.StationEquates[0].StationB);
        CollectionAssert.Contains(project.FixedStations!.ToList(), "A");
        CollectionAssert.Contains(project.FixedStations, "C");
        Assert.AreEqual(10f, project.FixedStationPositions!["C"].X, 0.001f);
        Assert.IsFalse(project.FixedStationPositions.ContainsKey("A"));
    }

    [TestMethod]
    public void Apply_then_network_solve_keeps_fixed_xyz_and_honors_dontadjust_weight()
    {
        var project = MinimalLoopProject();
        var apply = NetworkAnchorsApplier.TryApply(
            project,
            [new NetworkAnchorsApplier.EquateDraft("A", "A_alias")],
            [new NetworkAnchorsApplier.FixedDraft("A", true, "0", "0", "100")]);
        Assert.IsTrue(apply.Ok, apply.ErrorMessage);

        // Hold one leg hard during LS (Compass C / DONTADJUST).
        project.Shots[0].LegFlags = SurveyLegFlags.DontAdjust;

        var solved = SurveyNetworkAdjuster.Solve(project);
        Assert.IsTrue(solved.TryGetValue("A", out var a));
        Assert.AreEqual(0f, a.X, 0.05f);
        Assert.AreEqual(0f, a.Y, 0.05f);
        Assert.AreEqual(100f, a.Z, 0.05f);
        Assert.IsTrue(solved.ContainsKey("A_alias"));
        Assert.AreEqual(a.X, solved["A_alias"].X, 0.05f);
        Assert.AreEqual(a.Y, solved["A_alias"].Y, 0.05f);
        Assert.AreEqual(a.Z, solved["A_alias"].Z, 0.05f);
    }

    [TestMethod]
    public void Apply_then_survex_export_is_cavern_shaped_and_writes_smoke_file()
    {
        var project = MinimalLoopProject();
        project.Name = "Anchors Smoke";
        var apply = NetworkAnchorsApplier.TryApply(
            project,
            [new NetworkAnchorsApplier.EquateDraft("A", "A_alias")],
            [
                new NetworkAnchorsApplier.FixedDraft("A", true, "0", "0", "100"),
                new NetworkAnchorsApplier.FixedDraft("C", true, "10", "0", "100"),
            ]);
        Assert.IsTrue(apply.Ok, apply.ErrorMessage);
        project.Shots[1].LegFlags = "SURFACE";
        project.Shots.Add(new ShotRecord
        {
            FromStation = "B",
            ToStation = "SKIP",
            Distance = 1f,
            Azimuth = 0f,
            Clino = 0f,
            LegFlags = SurveyLegFlags.Exclude,
        });

        var bytes = SurvexExporter.BuildSvxUtf8Bom(project, new SurvexExportOptions
        {
            FixStation = "A",
            FixEasting = 0,
            FixNorthing = 0,
            FixElevation = 100,
            IncludeSplays = false,
            ProvisionalFix = false,
            IncludeLrudPassage = false,
            IncludeEntranceGpsCs = false,
        });
        var text = new UTF8Encoding(false).GetString(bytes.AsSpan(3..));

        StringAssert.Contains(text, "*encoding utf-8");
        StringAssert.Contains(text, "*begin anchors_smoke");
        StringAssert.Contains(text, "*equate A A_alias");
        StringAssert.Contains(text, "*fix A 0 0 100");
        StringAssert.Contains(text, "*fix C 10 0 100");
        StringAssert.Contains(text, "*flags surface");
        Assert.IsFalse(text.Contains("B\tSKIP\t", StringComparison.Ordinal));
        Assert.IsTrue(text.TrimEnd().EndsWith("*end anchors_smoke", StringComparison.Ordinal));

        var th = new UTF8Encoding(false).GetString(TherionExporter.BuildCenterlineThUtf8Bom(project).AsSpan(3..));
        StringAssert.Contains(th, "equate A A_alias");
        StringAssert.Contains(th, "fix C 10 0 100");
        StringAssert.Contains(th, "flags surface");

        var outDir = Path.Combine(Path.GetTempPath(), "caveaipro-smoke");
        Directory.CreateDirectory(outDir);
        var svxPath = Path.Combine(outDir, "anchors_smoke.svx");
        var thPath = Path.Combine(outDir, "anchors_smoke.th");
        File.WriteAllBytes(svxPath, bytes);
        File.WriteAllBytes(thPath, TherionExporter.BuildCenterlineThUtf8Bom(project));
        Assert.IsTrue(File.Exists(svxPath));
        Assert.IsTrue(new FileInfo(svxPath).Length > 80);
        Console.WriteLine($"Survex smoke file: {svxPath}");
        Console.WriteLine($"Therion smoke file: {thPath}");
    }

    private static CaveProjectDocument MinimalLoopProject() =>
        new()
        {
            Name = "Loop",
            Alt = 100,
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10f, Azimuth = 90f, Clino = 0f },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 10f, Azimuth = 180f, Clino = 0f },
                new ShotRecord { FromStation = "C", ToStation = "D", Distance = 10f, Azimuth = 270f, Clino = 0f },
                new ShotRecord { FromStation = "D", ToStation = "A", Distance = 10.4f, Azimuth = 0f, Clino = 0f },
            ],
        };
}
