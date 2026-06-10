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
        StringAssert.Contains(banner, "Signed in successfully");
        StringAssert.Contains(banner, "install trial");
    }
}
