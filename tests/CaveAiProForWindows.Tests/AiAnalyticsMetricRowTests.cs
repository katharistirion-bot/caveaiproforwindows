using CaveAiProForWindows.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AiAnalyticsMetricRowTests
{
    [TestMethod]
    public void CanSelectStation_requires_valid_station_and_non_separator_metric()
    {
        var row = new AiAnalyticsMetricRow
        {
            Station = "A1",
            AndroidContext = "Draft blows east",
            Metric = "QC geometry warning",
        };
        Assert.IsTrue(row.CanSelectStation);

        row.Station = "";
        Assert.IsFalse(row.CanSelectStation);

        row.Station = "A1";
        row.Metric = "— QC summary —";
        Assert.IsFalse(row.CanSelectStation);

        row.Metric = "Finding";
        row.Station = "—";
        Assert.IsFalse(row.CanSelectStation);
    }
}
