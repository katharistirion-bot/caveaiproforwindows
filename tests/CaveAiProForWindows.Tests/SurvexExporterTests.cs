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
        StringAssert.Contains(text, "0 0 0");
        StringAssert.Contains(text, "A2\t");
        StringAssert.Contains(text, "5.25\t90\t-5");
        Assert.IsFalse(text.Contains("A2\t-\t", StringComparison.Ordinal));
        Assert.IsTrue(Regex.IsMatch(text.TrimEnd(), @"\*end demo_cave\s*\z"), "expected *end demo_cave trailer");
    }
}
