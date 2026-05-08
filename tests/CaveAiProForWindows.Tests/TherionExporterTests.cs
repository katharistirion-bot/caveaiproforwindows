using System.Linq;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class TherionExporterTests
{
    [TestMethod]
    public void BuildCenterlineTh_writes_metric_units_and_normal_data_block()
    {
        var project = new CaveProjectDocument
        {
            Name = "Demo Cave",
            Date = "2024-06-01",
            Alt = 87.5,
            Shots =
            [
                new ShotRecord { FromStation = "A1", ToStation = "A2", Distance = 5.25f, Azimuth = 90f, Clino = -5f },
                new ShotRecord { FromStation = "A2", ToStation = "A3", Distance = 6.0f, Azimuth = 180f, Clino = 0f },
            ],
        };

        var bytes = TherionExporter.BuildCenterlineThUtf8Bom(project);
        Assert.AreEqual(0xEF, bytes[0]);
        Assert.AreEqual(0xBB, bytes[1]);
        Assert.AreEqual(0xBF, bytes[2]);
        var text = new UTF8Encoding(false).GetString(bytes.AsSpan(3..));

        StringAssert.StartsWith(text, "encoding utf-8");
        StringAssert.Contains(text, "survey demo_cave -title \"Demo Cave\"");
        StringAssert.Contains(text, "  centerline");
        StringAssert.Contains(text, "    units metric");
        StringAssert.Contains(text, "    data normal from to tape compass clino");
        StringAssert.Contains(text, "    A1 A2 5.25 90 -5");
        StringAssert.Contains(text, "    A2 A3 6 180 0");
        StringAssert.Contains(text, "  endcenterline");
        StringAssert.Contains(text, "endsurvey");
    }

    [TestMethod]
    public void BuildCenterlineTh_emits_anonymous_dot_for_splay_destination()
    {
        var project = new CaveProjectDocument
        {
            Name = "Splay test",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 4f, Azimuth = 0f, Clino = 0f },
                new ShotRecord { FromStation = "B", ToStation = "-", Distance = 1.2f, Azimuth = 270f, Clino = -40f },
            ],
        };

        var text = new UTF8Encoding(false).GetString(
            TherionExporter.BuildCenterlineThUtf8Bom(project).AsSpan(3..));

        // Two centerline blocks: traverse + splays.
        Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(text, @"^\s*centerline\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline).Count);
        // Splay row uses Therion anonymous-station dot ('B . tape compass clino').
        StringAssert.Contains(text, "    B . 1.2 270 -40");
        // Traverse leg never has dot.
        Assert.IsFalse(text.Contains("    A . ", System.StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildCenterlineTh_writes_geographic_fix_for_lat_lon_anchored_cave()
    {
        var project = new CaveProjectDocument
        {
            Name = "Geo anchored",
            Lat = 40.123,
            Lon = 22.456,
            Alt = 1234.5,
            Shots =
            [
                new ShotRecord { FromStation = "S0", ToStation = "S1", Distance = 3f, Azimuth = 0f, Clino = 0f },
            ],
        };

        var text = new UTF8Encoding(false).GetString(
            TherionExporter.BuildCenterlineThUtf8Bom(project).AsSpan(3..));

        StringAssert.Contains(text, "    fix S0 40.123 22.456 1234.5");
    }

    [TestMethod]
    public void BuildCenterlineTh_uses_invariant_culture_for_decimals()
    {
        var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("el-GR");
            var project = new CaveProjectDocument
            {
                Name = "Greek decimals",
                Shots =
                [
                    new ShotRecord { FromStation = "A", ToStation = "B", Distance = 7.5f, Azimuth = 12.34f, Clino = -1.5f },
                ],
            };
            var text = new UTF8Encoding(false).GetString(
                TherionExporter.BuildCenterlineThUtf8Bom(project).AsSpan(3..));
            Assert.IsFalse(text.Contains("7,5", System.StringComparison.Ordinal));
            StringAssert.Contains(text, "    A B 7.5 12.34 -1.5");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prev;
        }
    }
}
