using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SurveyAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class LegacyCenterlineImporterTests
{
    [TestMethod]
    public void Compass_dat_converts_feet_and_keeps_backsight_and_flags()
    {
        const string dat = """
            SECRET CAVE
            SURVEY NAME: B
            SURVEY DATE: 7 10 1979 COMMENT:Big Room
            SURVEY TEAM:
            D.SMITH
            DECLINATION: 0.00 FORMAT: DDDDLUDRADLNT CORRECTIONS: 0.00 0.00 0.00

            FROM TO LEN BEAR INC LEFT UP DOWN RIGHT AZM2 INC2 FLAGS COMMENTS
            B1 B2 10.00 90.00 0.00 1.00 2.00 3.00 4.00 270.00 0.00 Side
            B2 B3 10.00 180.00 0.00 0 0 0 0 0 0 #|L# surface
            B3 B4 5.00 0.00 0.00 0 0 0 0 180 -999 #|X# skip
            """;

        var result = LegacyCenterlineImporter.ParseCompass(dat, "fallback");
        Assert.AreEqual("SECRET CAVE", result.Project.Name);
        Assert.AreEqual("1979-07-10", result.Project.Date);
        Assert.AreEqual("ft", result.Project.LengthUnit);
        Assert.AreEqual(3, result.Project.Shots.Count);

        var first = result.Project.Shots[0];
        Assert.AreEqual(10f * 0.3048f, first.Distance, 0.0001f);
        Assert.AreEqual(90f, first.Azimuth, 0.01f);
        Assert.AreEqual(270f, first.BacksightAzimuth);
        Assert.AreEqual(1f * 0.3048f, first.L, 0.0001f);
        Assert.AreEqual(4f * 0.3048f, first.R, 0.0001f);
        Assert.AreEqual("Side", first.Notes);

        Assert.IsTrue(SurveyLegFlags.Has(result.Project.Shots[1].LegFlags, SurveyLegFlags.Surface));
        Assert.IsFalse(result.Project.Shots[1].CountsTowardCaveLength);
        Assert.IsTrue(SurveyLegFlags.IsExcluded(result.Project.Shots[2].LegFlags));
        Assert.IsFalse(result.Project.Shots[2].IsNetworkLeg);
    }

    [TestMethod]
    public void Walls_srv_reads_metres_equate_fix_and_surface()
    {
        const string srv = """
            #units meters order=dav
            #fix A1 0 0 10
            #flag surface
            A1 A2 5 90 0
            #flag
            A2 A3 5 180 -10 <1 2 3 4>
            #equate A3 B1
            """;

        var result = LegacyCenterlineImporter.ParseWalls(srv, "Walls cave");
        Assert.AreEqual("m", result.Project.LengthUnit);
        Assert.AreEqual(2, result.Project.Shots.Count);
        Assert.IsTrue(SurveyLegFlags.Has(result.Project.Shots[0].LegFlags, SurveyLegFlags.Surface));
        Assert.IsNull(result.Project.Shots[1].LegFlags);
        Assert.AreEqual(1f, result.Project.Shots[1].L, 0.001f);
        Assert.AreEqual(4f, result.Project.Shots[1].D, 0.001f);
        Assert.AreEqual("A3", result.Project.StationEquates![0].StationA);
        Assert.AreEqual("B1", result.Project.StationEquates[0].StationB);
        Assert.AreEqual(10f, result.Project.FixedStationPositions!["A1"].Z, 0.001f);
    }

    [TestMethod]
    public void VisualTopo_reads_to_from_order_and_exclude()
    {
        const string tro = """
            Entree Sample
            A1 * 0 0 5
            A2 A1 8 45 -5 1 1 2 0.5
            A3 A2 4 180 0 E
            """;

        var result = LegacyCenterlineImporter.ParseVisualTopo(tro, "fallback");
        Assert.AreEqual(2, result.Project.Shots.Count);
        Assert.AreEqual("A1", result.Project.Shots[0].FromStation);
        Assert.AreEqual("A2", result.Project.Shots[0].ToStation);
        Assert.AreEqual(8f, result.Project.Shots[0].Distance, 0.001f);
        Assert.AreEqual(1f, result.Project.Shots[0].L, 0.001f);
        Assert.IsTrue(SurveyLegFlags.IsExcluded(result.Project.Shots[1].LegFlags));
        Assert.AreEqual(5f, result.Project.FixedStationPositions!["A1"].Z, 0.001f);
    }
}

[TestClass]
public sealed class SurveyNetworkAdjustmentTests
{
    [TestMethod]
    public void Network_solve_closes_a_square_and_holds_the_entrance()
    {
        var project = new CaveProjectDocument
        {
            Name = "Square",
            Alt = 0,
            FixedStations = new List<string> { "A" },
            Shots =
            [
                Leg("A", "B", 10f, 90f),
                Leg("B", "C", 10f, 180f),
                Leg("C", "D", 10f, 270f),
                Leg("D", "A", 10.4f, 0f),
            ],
        };

        var result = SurveyLoopClosureAdjuster.Adjust(project, LoopAdjustmentMethod.NetworkLeastSquares);
        Assert.IsTrue(result.AdjustedCoordinates.TryGetValue("A", out var entrance));
        Assert.AreEqual(0f, entrance.X, 0.02f);
        Assert.AreEqual(0f, entrance.Y, 0.02f);
        Assert.IsTrue(result.AdjustedCoordinates["B"].X > 8f);

        var worst = 0.0;
        foreach (var shot in project.Shots)
        {
            var from = result.AdjustedCoordinates[shot.FromStation];
            var to = result.AdjustedCoordinates[shot.ToStation];
            var (dx, dy, dz) = SurveyLegGeometry.Delta(shot);
            var rx = (to.X - from.X) - dx;
            var ry = (to.Y - from.Y) - dy;
            var rz = (to.Z - from.Z) - dz;
            worst = Math.Max(worst, Math.Sqrt(rx * rx + ry * ry + rz * rz));
        }

        Assert.IsTrue(worst < 0.2, $"worst leg residual {worst}");
    }

    [TestMethod]
    public void Backsight_disagreement_is_averaged_before_the_vector()
    {
        var shot = new ShotRecord
        {
            FromStation = "A",
            ToStation = "B",
            Distance = 10f,
            Azimuth = 10f,
            Clino = 0f,
            BacksightAzimuth = 192f,
            BacksightClino = 2f,
        };
        Assert.AreEqual(11f, SurveyLegGeometry.ResolvedAzimuthDeg(shot), 0.05f);
        Assert.AreEqual(-1f, SurveyLegGeometry.ResolvedClinoDeg(shot), 0.001f);
    }

    private static ShotRecord Leg(string from, string to, float distance, float azimuth) => new()
    {
        FromStation = from,
        ToStation = to,
        Distance = distance,
        Azimuth = azimuth,
        Clino = 0f,
    };
}
