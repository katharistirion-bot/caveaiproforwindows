using CaveAiProForWindows.Services.Auth;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SubscriptionEntitlementTests
{
    private static readonly string ValidGraceDocId =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static InstallGraceRecord OpenInstallGrace() =>
        new(DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeMilliseconds());

    [TestMethod]
    public void ValidateDocument_accepts_active_play_subscription()
    {
        var doc = new UserEntitlementDocument(
            "ACTIVE",
            DateTimeOffset.UtcNow.AddDays(20),
            "PLAY_SUBSCRIPTION");
        var result = SubscriptionEntitlementService.ValidateDocument(doc);
        Assert.IsTrue(result.IsEntitled);
        Assert.AreEqual(SubscriptionAccessKind.PaidPlaySubscription, result.AccessKind);
        Assert.IsNull(result.DenialReason);
    }

    [TestMethod]
    public void ValidateDocument_accepts_trialing_play_subscription()
    {
        var doc = new UserEntitlementDocument(
            "TRIALING",
            DateTimeOffset.UtcNow.AddDays(25),
            "PLAY_SUBSCRIPTION");
        var result = SubscriptionEntitlementService.ValidateDocument(doc);
        Assert.IsTrue(result.IsEntitled);
        Assert.AreEqual(SubscriptionAccessKind.PaidPlaySubscription, result.AccessKind);
    }

    [TestMethod]
    public void ValidateDocument_accepts_install_grace_within_window()
    {
        var doc = new UserEntitlementDocument(
            "ACTIVE",
            DateTimeOffset.UtcNow.AddDays(10),
            "INSTALL_GRACE",
            ValidGraceDocId);
        var result = SubscriptionEntitlementService.ValidateDocument(doc, OpenInstallGrace());
        Assert.IsTrue(result.IsEntitled);
        Assert.AreEqual(SubscriptionAccessKind.InstallGraceTrial, result.AccessKind);
        Assert.IsNull(result.DenialReason);
    }

    [TestMethod]
    public void ValidateDocument_rejects_install_grace_without_server_record()
    {
        var doc = new UserEntitlementDocument(
            "ACTIVE",
            DateTimeOffset.UtcNow.AddDays(10),
            "INSTALL_GRACE",
            ValidGraceDocId);
        var result = SubscriptionEntitlementService.ValidateDocument(doc);
        Assert.IsFalse(result.IsEntitled);
        Assert.IsNotNull(result.DenialReason);
        StringAssert.Contains(result.DenialReason!, "app_install_grace");
    }

    [TestMethod]
    public void ValidateDocument_rejects_expired_install_grace_window()
    {
        var doc = new UserEntitlementDocument(
            "ACTIVE",
            DateTimeOffset.UtcNow.AddDays(1),
            "INSTALL_GRACE",
            ValidGraceDocId);
        var expiredGrace = new InstallGraceRecord(
            DateTimeOffset.UtcNow.AddDays(-40).ToUnixTimeMilliseconds());
        var result = SubscriptionEntitlementService.ValidateDocument(doc, expiredGrace);
        Assert.IsFalse(result.IsEntitled);
        StringAssert.Contains(result.DenialReason!, "Install-grace trial ended");
    }

    [TestMethod]
    public void ValidateDocument_rejects_expired_subscription()
    {
        var doc = new UserEntitlementDocument(
            "ACTIVE",
            DateTimeOffset.UtcNow.AddDays(-1),
            "PLAY_SUBSCRIPTION");
        var result = SubscriptionEntitlementService.ValidateDocument(doc);
        Assert.IsFalse(result.IsEntitled);
        StringAssert.Contains(result.DenialReason!, "expired");
    }

    [TestMethod]
    public void ValidateDocument_rejects_inactive_status()
    {
        var doc = new UserEntitlementDocument(
            "EXPIRED",
            DateTimeOffset.UtcNow.AddDays(5),
            "PLAY_SUBSCRIPTION");
        var result = SubscriptionEntitlementService.ValidateDocument(doc);
        Assert.IsFalse(result.IsEntitled);
    }

    [TestMethod]
    public void ValidateDocument_accepts_legacy_entitlement_without_source()
    {
        var doc = new UserEntitlementDocument(
            "ACTIVE",
            DateTimeOffset.UtcNow.AddDays(5),
            null);
        var result = SubscriptionEntitlementService.ValidateDocument(doc);
        Assert.IsTrue(result.IsEntitled);
        Assert.AreEqual(SubscriptionAccessKind.LegacyEntitlement, result.AccessKind);
    }

    [TestMethod]
    public void ValidateDocument_accepts_owner_entitlement()
    {
        var doc = new UserEntitlementDocument(
            "ACTIVE",
            DateTimeOffset.UtcNow.AddYears(50),
            "OWNER");
        var result = SubscriptionEntitlementService.ValidateDocument(doc);
        Assert.IsTrue(result.IsEntitled);
        Assert.AreEqual(SubscriptionAccessKind.Owner, result.AccessKind);
        Assert.IsNull(result.DenialReason);
    }

    [TestMethod]
    public void ValidateDocument_rejects_unknown_entitlement_source()
    {
        var doc = new UserEntitlementDocument(
            "ACTIVE",
            DateTimeOffset.UtcNow.AddDays(5),
            "SOMETHING_ELSE");
        var result = SubscriptionEntitlementService.ValidateDocument(doc);
        Assert.IsFalse(result.IsEntitled);
        StringAssert.Contains(result.DenialReason!, "Unknown entitlementSource");
    }

    [TestMethod]
    public void AccountSessionState_Apply_then_Clear_drops_stale_grant()
    {
        AccountSessionState.Clear();
        var grant = SubscriptionEntitlementService.ValidateDocument(
            new UserEntitlementDocument(
                "ACTIVE",
                DateTimeOffset.UtcNow.AddDays(5),
                "OWNER"));
        Assert.IsTrue(grant.IsEntitled);
        AccountSessionState.Apply(grant);
        Assert.IsTrue(AccountSessionState.LastEntitlement?.IsEntitled == true);

        var deny = SubscriptionEntitlementService.ValidateDocument(
            new UserEntitlementDocument(
                "ACTIVE",
                DateTimeOffset.UtcNow.AddDays(5),
                "SOMETHING_ELSE"));
        Assert.IsFalse(deny.IsEntitled);
        // RefreshEntitlementAsync clears on deny; mirror that contract here.
        AccountSessionState.Clear();
        Assert.IsNull(AccountSessionState.LastEntitlement);
    }
}
