using System.Linq;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class ShotsCsvExporterTests
{
    [TestMethod]
    public void ExportShotsToCsv_writes_utf8_bom_and_header_in_expected_order()
    {
        var project = new CaveProjectDocument { Name = "T", Shots = [] };
        var bytes = ExplorationAnalytics.ExportShotsToCsvUtf8Bom(project);
        Assert.AreEqual(0xEF, bytes[0]);
        Assert.AreEqual(0xBB, bytes[1]);
        Assert.AreEqual(0xBF, bytes[2]);
        var text = new UTF8Encoding(false).GetString(bytes.AsSpan(3..));
        var firstLine = text.Split('\n')[0].Trim('\r');
        StringAssert.StartsWith(firstLine, "fromStation,toStation,distance_m,clino_deg,azimuth_deg,depth_m,l_m,r_m,u_m,d_m");
    }

    [TestMethod]
    public void ExportShotsToCsv_quotes_commas_and_quotes_in_text_columns()
    {
        var project = new CaveProjectDocument
        {
            Name = "T",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A,1",
                    ToStation = "B \"two\"",
                    Distance = 5f,
                    Azimuth = 100f,
                    Clino = -2f,
                    Notes = "needs ,review and \"checks\"",
                },
            ],
        };

        var text = new UTF8Encoding(false).GetString(
            ExplorationAnalytics.ExportShotsToCsvUtf8Bom(project).AsSpan(3..));
        var rowLine = text.Split('\n').Skip(1).First().Trim('\r');

        // Fields with comma OR embedded quotes must be wrapped in quotes; embedded quotes doubled.
        StringAssert.Contains(rowLine, "\"A,1\"");
        StringAssert.Contains(rowLine, "\"B \"\"two\"\"\"");
        StringAssert.Contains(rowLine, "\"needs ,review and \"\"checks\"\"\"");
    }

    [TestMethod]
    public void ExportShotsToCsv_emits_invariant_decimal_separator()
    {
        var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("el-GR");

            var project = new CaveProjectDocument
            {
                Name = "T",
                Shots =
                [
                    new ShotRecord
                    {
                        FromStation = "A",
                        ToStation = "B",
                        Distance = 12.5f,
                        Azimuth = 45.25f,
                        Clino = -7.5f,
                        L = 1.5f, R = 2.5f, U = 3.5f, D = 4.5f,
                    },
                ],
            };
            var text = new UTF8Encoding(false).GetString(
                ExplorationAnalytics.ExportShotsToCsvUtf8Bom(project).AsSpan(3..));
            Assert.IsFalse(text.Contains("12,5", System.StringComparison.Ordinal),
                "CSV must always use '.' as decimal separator regardless of thread culture.");
            StringAssert.Contains(text, "12.5");
            StringAssert.Contains(text, "45.25");
            StringAssert.Contains(text, "-7.5");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [TestMethod]
    public void ExportShotsToCsv_writes_traverse_and_splay_rows_in_input_order()
    {
        var project = new CaveProjectDocument
        {
            Name = "T",
            Shots =
            [
                new ShotRecord { FromStation = "A", ToStation = "B", Distance = 1f, Azimuth = 10f, Clino = 0f },
                new ShotRecord { FromStation = "B", ToStation = "-", Distance = 0.5f, Azimuth = 270f, Clino = -45f },
                new ShotRecord { FromStation = "B", ToStation = "C", Distance = 2f, Azimuth = 20f, Clino = 0f },
            ],
        };
        var text = new UTF8Encoding(false).GetString(
            ExplorationAnalytics.ExportShotsToCsvUtf8Bom(project).AsSpan(3..));
        var lines = text.Split('\n').Select(l => l.Trim('\r')).Where(l => l.Length > 0).ToList();
        Assert.AreEqual(4, lines.Count, "Header + three data rows.");
        StringAssert.StartsWith(lines[1], "A,B,");
        StringAssert.StartsWith(lines[2], "B,-,");
        StringAssert.StartsWith(lines[3], "B,C,");
    }
}
