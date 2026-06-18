using CaveAiProForWindows.Services.Auth;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class MicrosoftTestModeTests
{
    [TestMethod]
    public void CreateSyntheticEntitlement_grants_active_play_subscription()
    {
        var result = MicrosoftTestMode.CreateSyntheticEntitlement();
        Assert.IsTrue(result.IsEntitled);
        Assert.AreEqual(SubscriptionAccessKind.PaidPlaySubscription, result.AccessKind);
        Assert.IsNull(result.DenialReason);
        Assert.IsNotNull(result.Document);
        Assert.AreEqual("PLAY_SUBSCRIPTION", result.Document!.EntitlementSource);
    }

    [TestMethod]
    public void IsActive_is_false_in_test_build()
    {
        // MICROSOFT_TEST_MODE is only set by MicrosoftStore-Review-Win64 publish profile.
        Assert.IsFalse(MicrosoftTestMode.IsActive);
        Assert.IsFalse(MicrosoftTestMode.IS_MICROSOFT_TEST_MODE);
    }

    [TestMethod]
    public void StoreReviewBuild_delegates_to_MicrosoftTestMode()
    {
        Assert.AreEqual(MicrosoftTestMode.IsActive, StoreReviewBuild.IsActive);
        Assert.AreEqual(MicrosoftTestMode.BannerMessage, StoreReviewBuild.BannerMessage);
    }

    [TestMethod]
    public void ThrowIfNetworkBlocked_is_no_op_when_test_mode_inactive()
    {
        MicrosoftTestMode.ThrowIfNetworkBlocked("unit test");
    }
}
