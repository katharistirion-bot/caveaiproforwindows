using CaveAiProForWindows.Services.Auth;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AccountStatusFormatterTests
{
    [TestMethod]
    public void FormatAccessSummary_play_subscription_includes_until_date()
    {
        var until = DateTimeOffset.UtcNow.AddDays(20);
        var result = new SubscriptionEntitlementResult(
            true,
            null,
            new UserEntitlementDocument("ACTIVE", until, "PLAY_SUBSCRIPTION"),
            SubscriptionAccessKind.PaidPlaySubscription);

        var text = AccountStatusFormatter.FormatAccessSummary(result);
        StringAssert.Contains(text, "Google Play subscription");
        StringAssert.Contains(text, until.ToLocalTime().ToString("d"));
    }

    [TestMethod]
    public void FormatWelcomeBanner_uses_access_summary()
    {
        var result = new SubscriptionEntitlementResult(
            true,
            null,
            new UserEntitlementDocument("ACTIVE", DateTimeOffset.UtcNow.AddDays(5), "INSTALL_GRACE", "a".PadLeft(64, 'a')),
            SubscriptionAccessKind.InstallGraceTrial);

        var banner = AccountStatusFormatter.FormatWelcomeBanner(result);
        StringAssert.StartsWith(banner, "Signed in");
        StringAssert.Contains(banner, "install trial");
    }

    [TestMethod]
    public void FormatAccessSummary_owner_does_not_mention_play()
    {
        var until = DateTimeOffset.UtcNow.AddYears(10);
        var result = new SubscriptionEntitlementResult(
            true,
            null,
            new UserEntitlementDocument("ACTIVE", until, "OWNER"),
            SubscriptionAccessKind.Owner);

        var text = AccountStatusFormatter.FormatAccessSummary(result);
        StringAssert.Contains(text, "Owner access");
        Assert.IsFalse(text.Contains("Play", StringComparison.OrdinalIgnoreCase));
        var about = AccountStatusFormatter.FormatAboutSubscription(result);
        Assert.IsFalse(about.Contains("Manage subscription", StringComparison.OrdinalIgnoreCase));
    }
}
