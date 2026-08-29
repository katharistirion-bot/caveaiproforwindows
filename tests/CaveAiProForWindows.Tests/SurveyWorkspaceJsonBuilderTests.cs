using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyWorkspaceJsonBuilderTests
{
    [TestMethod]
    public void BuildBytes_keeps_named_cartography_and_drops_photo_heavy_keys()
    {
        var full = """
            {
              "name": "Xtenia",
              "shots": [{"fromStation":"A","toStation":"B","distance":1,"photoUri":"content://x"}],
              "namedCartographyMaps": [{"id":"a","name":"Plan"}],
              "activeCartographyMapId": "a",
              "depthSpanAnnotations": [{"id":"d1"}],
              "rocks": [{"id":"photo-heavy"}],
              "fieldPhotos": [{"uri":"content://y"}]
            }
            """;

        var bytes = SurveyWorkspaceJsonBuilder.BuildBytes(Encoding.UTF8.GetBytes(full));
        using var doc = JsonDocument.Parse(bytes);
        var slim = doc.RootElement;

        Assert.AreEqual("Xtenia", slim.GetProperty("name").GetString());
        Assert.AreEqual("a", slim.GetProperty("activeCartographyMapId").GetString());
        Assert.AreEqual(1, slim.GetProperty("namedCartographyMaps").GetArrayLength());
        Assert.AreEqual(1, slim.GetProperty("depthSpanAnnotations").GetArrayLength());
        Assert.IsFalse(slim.TryGetProperty("rocks", out _));
        Assert.IsFalse(slim.TryGetProperty("fieldPhotos", out _));

        var shot = slim.GetProperty("shots")[0];
        Assert.AreEqual("A", shot.GetProperty("fromStation").GetString());
        Assert.AreEqual(1.0, shot.GetProperty("distance").GetDouble(), 1e-9);
        Assert.IsFalse(shot.TryGetProperty("photoUri", out _));
    }

    [TestMethod]
    public void BuildBytes_filters_empty_publicLibraryCartographyUris()
    {
        var full = """
            {
              "name": "Uris",
              "publicLibraryCartographyUris": ["https://example.com/a.png", "", "https://example.com/b.png"]
            }
            """;

        var bytes = SurveyWorkspaceJsonBuilder.BuildBytes(Encoding.UTF8.GetBytes(full));
        using var doc = JsonDocument.Parse(bytes);
        var uris = doc.RootElement.GetProperty("publicLibraryCartographyUris");
        Assert.AreEqual(2, uris.GetArrayLength());
        Assert.AreEqual("https://example.com/a.png", uris[0].GetString());
        Assert.AreEqual("https://example.com/b.png", uris[1].GetString());
    }

    [TestMethod]
    public void BuildBytes_invalid_json_returns_fallback_marker()
    {
        var bytes = SurveyWorkspaceJsonBuilder.BuildBytes(Encoding.UTF8.GetBytes("{not-json"));
        using var doc = JsonDocument.Parse(bytes);
        Assert.IsTrue(doc.RootElement.GetProperty("__caveAiWorkspaceInvalid").GetBoolean());
        Assert.AreEqual(0, doc.RootElement.GetProperty("shots").GetArrayLength());
    }

    [TestMethod]
    public void BuildBytes_non_object_root_returns_fallback_marker()
    {
        var bytes = SurveyWorkspaceJsonBuilder.BuildBytes(Encoding.UTF8.GetBytes("[1,2,3]"));
        using var doc = JsonDocument.Parse(bytes);
        Assert.IsTrue(doc.RootElement.GetProperty("__caveAiWorkspaceInvalid").GetBoolean());
    }

    [TestMethod]
    public void MaxBytes_is_twelve_mib()
    {
        Assert.AreEqual(12 * 1024 * 1024, SurveyWorkspaceJsonBuilder.MaxBytes);
    }
}
