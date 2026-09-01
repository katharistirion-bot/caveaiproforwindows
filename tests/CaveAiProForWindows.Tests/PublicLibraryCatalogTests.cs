using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class PublicLibraryCatalogTests
{
    [TestMethod]
    public void WithEmbed_appends_query_param()
    {
        var url = PublicLibraryCatalog.WithEmbed("https://example.com/map");
        StringAssert.Contains(url, "embed=windows");
        Assert.IsFalse(PublicLibraryCatalog.WithEmbed(url).Contains("embed=windows&embed="));
    }

    [TestMethod]
    public void WithEmbed_preserves_existing_query()
    {
        var url = PublicLibraryCatalog.WithEmbed("https://example.com/map?cave=abc");
        StringAssert.Contains(url, "cave=abc");
        StringAssert.Contains(url, "embed=windows");
    }

    [TestMethod]
    public void BuildMapCaveUrl_encodes_id()
    {
        var env = Environment.GetEnvironmentVariable("CAVEAIPRO_WEB_ORIGIN");
        try
        {
            Environment.SetEnvironmentVariable("CAVEAIPRO_WEB_ORIGIN", "https://test.example");
            var url = PublicLibraryCatalog.BuildMapCaveUrl("uid_cave-1");
            StringAssert.StartsWith(url, "https://test.example/map?cave=");
            StringAssert.Contains(url, "uid_cave-1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CAVEAIPRO_WEB_ORIGIN", env);
        }
    }

    [TestMethod]
    public void ResolveInAppStartUrl_adds_embed_and_keeps_watch_view()
    {
        var url = PublicLibraryCatalog.ResolveInAppStartUrl(
            "https://www.caveaipro.com/map?view=watch&lat=38.22&lon=20.62");
        StringAssert.Contains(url, "view=watch");
        StringAssert.Contains(url, "embed=windows");
        StringAssert.Contains(url, "lat=38.22");
        StringAssert.Contains(url, "zoom=13");
    }

    [TestMethod]
    public void EnsureWatchDefaultZoom_skips_when_zoom_present()
    {
        var url = PublicLibraryCatalog.EnsureWatchDefaultZoom(
            "https://www.caveaipro.com/map?view=watch&lat=38.22&lon=20.62&zoom=11");
        StringAssert.Contains(url, "zoom=11");
        Assert.IsFalse(url.Contains("zoom=13"));
    }

    [TestMethod]
    public void WebExploreMapUrlEmbedded_includes_embed_and_path()
    {
        var env = Environment.GetEnvironmentVariable("CAVEAIPRO_WEB_ORIGIN");
        try
        {
            Environment.SetEnvironmentVariable("CAVEAIPRO_WEB_ORIGIN", "https://test.example");
            var url = PublicLibraryCatalog.WebExploreMapUrlEmbedded;
            StringAssert.Contains(url, "view=explore");
            StringAssert.Contains(url, "embed=windows");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CAVEAIPRO_WEB_ORIGIN", env);
        }
    }

    [TestMethod]
    public void RememberExploreMapViewportUrl_persists_url()
    {
        var path = AppUiSettingsStore.SettingsPath;
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            PublicLibraryCatalog.RememberExploreMapViewportUrl("https://www.caveaipro.com/map?view=explore&lat=40&lon=10");
            var loaded = AppUiSettingsStore.LoadOrDefault();
            Assert.AreEqual("https://www.caveaipro.com/map?view=explore&lat=40&lon=10", loaded.ExploreMap.LastViewportUrl);
            StringAssert.Contains(PublicLibraryCatalog.ResolveExploreMapOpenUrl(), "lat=40");
        }
        finally
        {
            if (backup != null)
                File.WriteAllText(path, backup);
            else if (File.Exists(path))
                File.Delete(path);
        }
    }
}
