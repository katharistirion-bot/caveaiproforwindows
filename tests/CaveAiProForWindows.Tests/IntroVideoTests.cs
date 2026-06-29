using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class IntroVideoTests
{
    [TestMethod]
    public void Gate_shows_intro_when_not_seen_and_video_bundled()
    {
        var settings = new AppUiSettingsModel { HasSeenIntroVideo = false };
        var expected = IntroVideoAssetPaths.IsVideoBundled;
        Assert.AreEqual(expected, IntroVideoGate.ShouldShowFirstRun(settings));
    }

    [TestMethod]
    public void Gate_skips_intro_when_already_seen()
    {
        var settings = new AppUiSettingsModel { HasSeenIntroVideo = true };
        Assert.IsFalse(IntroVideoGate.ShouldShowFirstRun(settings));
    }

    [TestMethod]
    public void HasSeenIntroVideo_serializes_in_ui_settings()
    {
        var model = new AppUiSettingsModel { HasSeenIntroVideo = true };
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        StringAssert.Contains(json, "hasSeenIntroVideo");
        Assert.IsTrue(json.Contains("true", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Asset_paths_resolve_null_when_video_missing()
    {
        var path = IntroVideoAssetPaths.TryResolveExistingPath();
        Assert.IsNull(path);
        Assert.IsFalse(IntroVideoAssetPaths.IsVideoBundled);
    }

    [TestMethod]
    public void Relative_video_path_matches_assets_folder()
    {
        Assert.AreEqual(Path.Combine("Assets", "Intro", "intro.mp4"), IntroVideoAssetPaths.RelativeVideoPath);
    }
}
