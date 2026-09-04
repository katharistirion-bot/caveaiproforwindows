using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class PublicLibraryCatalogTests
{
    [TestMethod]
    public void WithEmbed_inserts_query_before_fragment()
    {
        var url = PublicLibraryCatalog.WithEmbed(
            "https://www.caveaipro.com/map?view=fieldtrip#trip=abc");
        StringAssert.Contains(url, "embed=windows");
        StringAssert.Contains(url, "#trip=abc");
        Assert.IsTrue(url.IndexOf("embed=windows", StringComparison.Ordinal) < url.IndexOf("#trip=", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryRestampEmbed_stamps_own_site_without_desktopAuth()
    {
        var next = PublicLibraryCatalog.TryRestampEmbed(PublicLibraryCatalog.WebOrigin + "/map?view=browse");
        Assert.IsNotNull(next);
        StringAssert.Contains(next, "embed=windows");
        Assert.IsFalse(next!.Contains("desktopAuth=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TryRestampEmbed_returns_null_when_already_embedded()
    {
        var url = PublicLibraryCatalog.WebMapUrlEmbedded;
        Assert.IsNull(PublicLibraryCatalog.TryRestampEmbed(url));
    }

    [TestMethod]
    public void TryRestampEmbed_skips_firebase_auth_handler()
    {
        Assert.IsNull(PublicLibraryCatalog.TryRestampEmbed(
            "https://www.caveaipro.com/__/auth/handler?state=abc"));
        Assert.IsNull(PublicLibraryCatalog.TryRestampEmbed(
            PublicLibraryCatalog.FirebaseHostingOrigin + "/__/auth/handler"));
    }

    [TestMethod]
    public void TryRestampEmbed_skips_google_oauth()
    {
        Assert.IsNull(PublicLibraryCatalog.TryRestampEmbed("https://accounts.google.com/o/oauth2/v2/auth"));
        Assert.IsFalse(PublicLibraryCatalog.IsOwnSiteUrl("https://accounts.google.com/o/oauth2/v2/auth"));
    }

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
    public void ForExternalBrowser_strips_webview_embed_params()
    {
        var url = PublicLibraryCatalog.ForExternalBrowser(
            "https://www.caveaipro.com/map?view=explore&embed=windows&desktopAuth=v1&lat=38.2");
        StringAssert.Contains(url, "view=explore");
        StringAssert.Contains(url, "lat=38.2");
        Assert.IsFalse(url.Contains("embed=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(url.Contains("desktopAuth=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ResolveInAppStartUrl_adds_embed_to_cave_ai_path()
    {
        var url = PublicLibraryCatalog.ResolveInAppStartUrl("https://www.caveaipro.com/ai?refId=abc");
        StringAssert.Contains(url, "/ai");
        StringAssert.Contains(url, "refId=abc");
        StringAssert.Contains(url, "embed=windows");
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
