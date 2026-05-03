using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class ExplorationDataLoaderTests
{
    private const string MinimalProject =
        """{"name":"Test","date":"2024-01-01","shots":[{"fromStation":"1","toStation":"2","distance":1,"azimuth":0,"clino":0}]}""";

    [TestMethod]
    public void Normalize_top_level_array_is_passthrough_trimmed()
    {
        var raw = " \r\n[" + MinimalProject + "]";
        var n = ExplorationDataLoader.NormalizeToProjectArrayJson(raw, "x.json");
        Assert.AreEqual('[', n.TrimStart()[0]);
        StringAssert.Contains(n, "Test");
    }

    [TestMethod]
    public void Normalize_wrapped_projects_extracts_inner_array()
    {
        var inner = "[" + MinimalProject + "]";
        var wrapped = "{\"projects\":" + inner + "}";
        var n = ExplorationDataLoader.NormalizeToProjectArrayJson(wrapped, "wrap.json");
        StringAssert.StartsWith(n.TrimStart(), "[");
        Assert.IsFalse(n.Contains("projects", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Normalize_wrapped_caves_property_name()
    {
        var inner = "[" + MinimalProject + "]";
        var wrapped = "{\"caves\":" + inner + "}";
        var n = ExplorationDataLoader.NormalizeToProjectArrayJson(wrapped, "caves.json");
        StringAssert.Contains(n, "Test");
    }

    [TestMethod]
    public void DeserializeProjectsFromText_accepts_wrapped_object()
    {
        var inner = "[" + MinimalProject + "]";
        var wrapped = "{\"surveyProjects\":" + inner + "}";
        var list = ExplorationDataLoader.DeserializeProjectsFromText(wrapped);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("Test", list[0].Name);
    }

    [TestMethod]
    public void Normalize_empty_throws()
    {
        Assert.ThrowsException<InvalidDataException>(() =>
            ExplorationDataLoader.NormalizeToProjectArrayJson("   ", "empty.json"));
    }
}
