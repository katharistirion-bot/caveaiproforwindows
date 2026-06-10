using CaveAiProForWindows.Services.Auth;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class StoreReviewBuildTests
{
    [TestMethod]
    public void CreateSyntheticEntitlement_grants_active_play_subscription()
    {
        var result = StoreReviewBuild.CreateSyntheticEntitlement();
        Assert.IsTrue(result.IsEntitled);
        Assert.AreEqual(SubscriptionAccessKind.PaidPlaySubscription, result.AccessKind);
        Assert.IsNull(result.DenialReason);
        Assert.IsNotNull(result.Document);
        Assert.AreEqual("PLAY_SUBSCRIPTION", result.Document!.EntitlementSource);
    }

    [TestMethod]
    public void IsActive_is_false_in_test_build()
    {
        // STORE_REVIEW_UNLOCKED is only set by MicrosoftStore-Review-Win64 publish profile.
        Assert.IsFalse(StoreReviewBuild.IsActive);
    }
}
