using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveySiteTypeTests
{
    [TestMethod]
    public void GetMapLabel_returns_english_labels()
    {
        Assert.AreEqual("Cave", SurveySiteType.GetMapLabel(SurveySiteTypeKind.Cave));
        Assert.AreEqual("Mine", SurveySiteType.GetMapLabel(SurveySiteTypeKind.Mine));
        Assert.AreEqual("Pothole", SurveySiteType.GetMapLabel(SurveySiteTypeKind.Pothole));
        Assert.AreEqual("Spring", SurveySiteType.GetMapLabel(SurveySiteTypeKind.Spring));
        Assert.AreEqual("", SurveySiteType.GetMapLabel(SurveySiteTypeKind.Unknown));
    }

    [TestMethod]
    public void Parse_normalizes_android_tokens()
    {
        Assert.AreEqual(SurveySiteTypeKind.Cave, SurveySiteType.Parse("CAVE"));
        Assert.AreEqual(SurveySiteTypeKind.Mine, SurveySiteType.Parse("mine"));
        Assert.AreEqual(SurveySiteTypeKind.Unknown, SurveySiteType.Parse(null));
    }

    [TestMethod]
    public void InferFromCatalogLabel_mapsOsmLabels()
    {
        Assert.AreEqual("MINE", SurveySiteType.InferFromCatalogLabel("historic mine"));
        Assert.AreEqual("SPRING", SurveySiteType.InferFromCatalogLabel("Karst spring"));
        Assert.AreEqual("POTHOLE", SurveySiteType.InferFromCatalogLabel("sink hole"));
        Assert.AreEqual("CAVE", SurveySiteType.InferFromCatalogLabel("show cave"));
        Assert.AreEqual("CAVE", SurveySiteType.InferFromCatalogLabel(null));
    }
}
