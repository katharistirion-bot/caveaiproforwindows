using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AppUpdateServiceTests
{
    [TestMethod]
    public void IsRemoteNewer_detects_newer_semver()
    {
        Assert.IsTrue(AppUpdateService.IsRemoteNewer("1.2.0", "1.1.9"));
        Assert.IsFalse(AppUpdateService.IsRemoteNewer("1.0.0", "1.0.0"));
        Assert.IsFalse(AppUpdateService.IsRemoteNewer("0.9.0", "1.0.0"));
    }

    [TestMethod]
    public void IsRemoteNewer_handles_v_prefix_and_two_part_versions()
    {
        Assert.IsTrue(AppUpdateService.IsRemoteNewer("v1.3.0", "1.2.1"));
        Assert.IsTrue(AppUpdateService.IsRemoteNewer("1.3", "1.2.1"));
        Assert.IsFalse(AppUpdateService.IsRemoteNewer("V1.2.1", "1.2.1"));
    }

    [TestMethod]
    public void CurrentVersion_matches_informational_version()
    {
        Assert.AreEqual(AppMetadata.InformationalVersion, AppUpdateService.CurrentVersion);
    }

    [TestMethod]
    public void GitHubRepo_defaults_to_release_repository()
    {
        Assert.AreEqual("katharistirion-bot/caveaiproforwindows", AppUpdateService.DefaultGitHubRepo);
        Assert.AreEqual(
            "https://github.com/katharistirion-bot/caveaiproforwindows",
            AppUpdateService.GitHubRepoUrl);
    }

    [TestMethod]
    public void VelopackAppId_matches_pack_script()
    {
        Assert.AreEqual("CaveAiProForWindows", AppUpdateService.VelopackAppId);
    }

    [TestMethod]
    public void IsUpdateCheckDisabled_matches_store_distribution_flag()
    {
        Assert.AreEqual(DistributionChannel.UpdatesHandledByStore, AppUpdateService.IsUpdateCheckDisabled);
    }
}
