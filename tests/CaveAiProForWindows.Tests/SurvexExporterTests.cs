using System.Text;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurvexExporterTests
{
    [TestMethod]
    public void BuildSvxUtf8Bom_output_is_stable_for_fixed_project()
    {
        var project = new CaveProjectDocument
        {
            Name = "Demo Cave",
            Date = "2024-06-01",
            Alt = 100.5,
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A1",
                    ToStation = "A2",
                    Distance = 5.25f,
                    Azimuth = 90,
                    Clino = -5,
                },
                new ShotRecord
                {
                    FromStation = "A2",
                    ToStation = "-",
                    Distance = 0,
                    Azimuth = 0,
                    Clino = 0,
                },
            ],
        };

        var bytes = SurvexExporter.BuildSvxUtf8Bom(project);
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes.AsSpan(3..));

        StringAssert.StartsWith(text, "*encoding utf-8");
        StringAssert.Contains(text, "*begin demo_cave");
        Assert.IsTrue(text.Contains("*fix ", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(text, "A1");
        StringAssert.Contains(text, "0 0 100.5");
        StringAssert.Contains(text, "A2\t");
        StringAssert.Contains(text, "5.25\t90\t-5");
        Assert.IsFalse(text.Contains("A2\t-\t", StringComparison.Ordinal));
        Assert.IsTrue(Regex.IsMatch(text.TrimEnd(), @"\*end demo_cave\s*\z"), "expected *end demo_cave trailer");
    }

    [TestMethod]
    public void BuildSvxUtf8Bom_emits_explicit_units_for_third_party_compatibility()
    {
        var project = new CaveProjectDocument
        {
            Name = "Units Test",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10f, Azimuth = 45f, Clino = 0f },
            ],
        };

        var bytes = SurvexExporter.BuildSvxUtf8Bom(project);
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes.AsSpan(3..));

        StringAssert.Contains(text, "*units tape metres");
        StringAssert.Contains(text, "*units compass degrees");
        StringAssert.Contains(text, "*units clino degrees");

        var unitsIndex = text.IndexOf("*units tape", StringComparison.Ordinal);
        var dataIndex = text.IndexOf("*data normal from to tape compass clino", StringComparison.Ordinal);
        Assert.IsTrue(unitsIndex >= 0 && dataIndex > unitsIndex,
            "*units directives must appear before *data normal in the centerline.");
    }

    [TestMethod]
    public void BuildSvxUtf8Bom_uses_invariant_culture_for_decimals_regardless_of_thread_culture()
    {
        var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("el-GR"); // comma decimal
            var project = new CaveProjectDocument
            {
                Name = "Greek decimals",
                Shots =
                [
                    new ShotRecord { FromStation = "A", ToStation = "B", Distance = 7.5f, Azimuth = 12.34f, Clino = -1.5f },
                ],
            };
            var bytes = SurvexExporter.BuildSvxUtf8Bom(project);
            var text = new UTF8Encoding(false).GetString(bytes.AsSpan(3..));

            Assert.IsFalse(text.Contains("7,5", StringComparison.Ordinal),
                "Decimals must use '.', never the el-GR ',' separator.");
            StringAssert.Contains(text, "7.5\t");
            StringAssert.Contains(text, "12.34\t-1.5");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [TestMethod]
    public void BuildSvxUtf8Bom_includes_splays_and_declination_when_requested()
    {
        var project = new CaveProjectDocument
        {
            Name = "Full Export",
            Alt = 420,
            SurveyCalibrationProfile = new SurveyCalibrationProfileSnapshot
            {
                MagneticDeclinationAppliedDeg = 4.5f,
            },
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10f, Azimuth = 45f, Clino = 0f, L = 1, R = 1, U = 0.5f, D = 0.5f },
                new ShotRecord { FromStation = "B", ToStation = "-", Distance = 3f, Azimuth = 90f, Clino = -10f },
            ],
        };

        var options = new SurvexExportOptions
        {
            FixStation = "A",
            FixEasting = 100,
            FixNorthing = 200,
            FixElevation = 420,
            DeclinationDeg = 4.5,
            IncludeSplays = true,
            ProvisionalFix = false,
        };

        var bytes = SurvexExporter.BuildSvxUtf8Bom(project, options);
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes.AsSpan(3..));

        StringAssert.Contains(text, "*declination 4.5");
        StringAssert.Contains(text, "*fix A 100 200 420");
        StringAssert.Contains(text, "B\t-\t3\t90\t-10");
        StringAssert.Contains(text, "*data passage station left right up down");
        StringAssert.Contains(text, "A\t1\t1\t0.5\t0.5");
    }

    [TestMethod]
    public void BuildSvxUtf8Bom_emits_entrance_gps_cs_when_lat_lon_present()
    {
        var project = new CaveProjectDocument
        {
            Name = "GPS Cave",
            Lat = 38.123456,
            Lon = 23.654321,
            Alt = 120,
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 10f, Azimuth = 0f, Clino = 0f },
            ],
        };

        var bytes = SurvexExporter.BuildSvxUtf8Bom(project);
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes.AsSpan(3..));

        StringAssert.Contains(text, "*cs long-lat");
        StringAssert.Contains(text, "*fix\tentrance-gps");
        StringAssert.Contains(text, "23.654321");
        StringAssert.Contains(text, "38.123456");
    }
}
