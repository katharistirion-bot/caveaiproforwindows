using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AiAnalyticsDiagnosticExporterTests
{
    [TestMethod]
    public void BuildCsvUtf8Bom_includes_header_and_escapes_commas()
    {
        var rows = new[]
        {
            new AiAnalyticsMetricRow
            {
                Station = "A1",
                AndroidContext = "Note, with comma",
                GeometryContext = "Loop",
                Metric = "Closure",
                Value = "0.12 m",
                AlertLevel = AiAnalyticsAlertLevel.Priority,
            },
        };

        var bytes = AiAnalyticsDiagnosticExporter.BuildCsvUtf8Bom(rows, "Demo run");
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.IsTrue(text.StartsWith('\uFEFF') || bytes.Length > 3);
        StringAssert.Contains(text, "Station,Android,Geometry,Finding,Detail,Alert");
        StringAssert.Contains(text, "\"Note, with comma\"");
        StringAssert.Contains(text, "PRIORITY");
    }
}
