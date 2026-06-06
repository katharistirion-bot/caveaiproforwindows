using CaveAiProForWindows.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AiAnalyticsMetricRowTests
{
    [TestMethod]
    public void CanSelectStation_requires_android_note_and_valid_station()
    {
        var row = new AiAnalyticsMetricRow
        {
            Station = "A1",
            AndroidContext = "Draft blows east",
        };
        Assert.IsTrue(row.CanSelectStation);

        row.AndroidContext = "";
        Assert.IsFalse(row.CanSelectStation);

        row.AndroidContext = "Note";
        row.Station = "—";
        Assert.IsFalse(row.CanSelectStation);
    }
}
