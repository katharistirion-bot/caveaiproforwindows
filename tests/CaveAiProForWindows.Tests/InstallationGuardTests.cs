using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class InstallationGuardTests
{
    [TestMethod]
    public void BlockedUserMessage_mentions_velopack_setup()
    {
        StringAssert.Contains(InstallationGuard.BlockedUserMessage, "Setup.exe");
        StringAssert.Contains(InstallationGuard.BlockedUserMessage, "GitHub Releases");
    }

#if STORE_DISTRIBUTION
    [TestMethod]
    public void BlockedUserMessage_mentions_store_when_store_build()
    {
        StringAssert.Contains(InstallationGuard.BlockedUserMessage, "Microsoft Store");
    }
#endif

    [TestMethod]
    public void IsMicrosoftStoreInstallPath_detects_WindowsApps()
    {
        Assert.IsTrue(InstallationGuard.IsMicrosoftStoreInstallPath(
            @"C:\Program Files\WindowsApps\Publisher.CaveAiProForWindows_1.3.0.0_x64__abc123"));
    }

    [TestMethod]
    public void IsMicrosoftStoreInstallPath_detects_local_WindowsApps_stub()
    {
        Assert.IsTrue(InstallationGuard.IsMicrosoftStoreInstallPath(
            @"C:\Users\test\AppData\Local\Microsoft\WindowsApps\CaveAiProForWindows.exe"));
    }

    [TestMethod]
    public void IsMicrosoftStoreInstallPath_detects_ProgramData_Packages()
    {
        Assert.IsTrue(InstallationGuard.IsMicrosoftStoreInstallPath(
            @"C:\ProgramData\Packages\Publisher.CaveAiProForWindows_abc123\LocalCache"));
    }

    [TestMethod]
    public void IsMicrosoftStoreInstallPath_rejects_downloads_folder()
    {
        Assert.IsFalse(InstallationGuard.IsMicrosoftStoreInstallPath(@"C:\Users\test\Downloads\CaveAiProForWindows"));
        Assert.IsFalse(InstallationGuard.IsMicrosoftStoreInstallPath(@"D:\dist\CaveAiProForWindows"));
    }

    [TestMethod]
    public void IsMicrosoftStoreInstallPath_rejects_null_or_empty()
    {
        Assert.IsFalse(InstallationGuard.IsMicrosoftStoreInstallPath(null));
        Assert.IsFalse(InstallationGuard.IsMicrosoftStoreInstallPath(""));
        Assert.IsFalse(InstallationGuard.IsMicrosoftStoreInstallPath("   "));
    }

#if DEBUG
    [TestMethod]
    public void IsLaunchedFromRegisteredInstall_true_in_debug()
    {
        Assert.IsTrue(InstallationGuard.IsLaunchedFromRegisteredInstall());
    }
#endif

    [TestMethod]
    public void StartupUriRouter_parses_explore_protocol()
    {
        var intent = StartupUriRouter.Parse(["caveaipro://explore?lat=40.1&lon=22.5&z=8"]);
        StringAssert.Contains(intent.ExploreMapUrl, "lat=40.1");
        StringAssert.Contains(intent.ExploreMapUrl, "lon=22.5");
    }

    [TestMethod]
    public void MapToolbarPreset_field_qc_enables_loop_highlights()
    {
        MapToolbarPresetApplier.Apply(MapToolbarPresetApplier.Preset.FieldQc);
        var settings = AppUiSettingsStore.LoadOrDefault();
        Assert.IsTrue(settings.Plan.LoopClosureHighlights);
        Assert.AreEqual("Full", settings.SurveyDetailDensity);
    }
}
