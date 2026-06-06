using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class FieldCatalogMapPinTests
{
    [TestMethod]
    public void Collect_ReadsFieldCatalogEntriesWithPlanCoordinates()
    {
        var project = new CaveProjectDocument
        {
            Name = "Test Cave",
            FieldCatalogEntries = JsonDocument.Parse("""
                [
                  {
                    "category": "Fungi",
                    "commonName": "Cave mold",
                    "scientificName": "Myco sp.",
                    "planMapX": 12.5,
                    "planMapY": -3.2
                  }
                ]
                """).RootElement,
        };

        var pins = FieldCatalogMapPinCollector.Collect(project);

        Assert.AreEqual(1, pins.Count);
        Assert.AreEqual(12.5f, pins[0].X, 0.001f);
        Assert.AreEqual(-3.2f, pins[0].Y, 0.001f);
        Assert.IsFalse(string.IsNullOrWhiteSpace(pins[0].Title));
    }

    [TestMethod]
    public void Collect_SkipsEntriesWithoutPlanCoordinates()
    {
        var project = new CaveProjectDocument
        {
            Name = "Test Cave",
            FieldCatalogEntries = JsonDocument.Parse("""
                [{"category":"Plant","commonName":"Moss"}]
                """).RootElement,
        };

        var pins = FieldCatalogMapPinCollector.Collect(project);
        Assert.AreEqual(0, pins.Count);
    }
}
