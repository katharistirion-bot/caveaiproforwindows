namespace CaveAiProForWindows.Services.Auth;

public static class AccountStatusFormatter
{
    public static string FormatAccessSummary(SubscriptionEntitlementResult result)
    {
        var until = result.Document?.PremiumCloudUntil;
        var untilText = until.HasValue ? until.Value.ToLocalTime().ToString("d") : null;

        return result.AccessKind switch
        {
            SubscriptionAccessKind.PaidPlaySubscription when untilText != null =>
                $"Google Play subscription · active until {untilText}",
            SubscriptionAccessKind.PaidPlaySubscription =>
                "Google Play subscription · active",
            SubscriptionAccessKind.InstallGraceTrial when until.HasValue && untilText != null =>
                $"30-day install trial · {DaysRemaining(until.Value)} day(s) left (until {untilText})",
            SubscriptionAccessKind.InstallGraceTrial =>
                "30-day install trial · active",
            SubscriptionAccessKind.LegacyEntitlement when untilText != null =>
                $"CaveAI Pro access · until {untilText}",
            SubscriptionAccessKind.LegacyEntitlement =>
                "CaveAI Pro access · active",
            _ => "CaveAI Pro access",
        };
    }

    public static string FormatWelcomeBanner(SubscriptionEntitlementResult result)
    {
        var summary = FormatAccessSummary(result);
        return $"Signed in successfully — {summary}.";
    }

    private static int DaysRemaining(DateTimeOffset untilUtc)
    {
        var remaining = untilUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return 0;
        return Math.Max(1, (int)Math.Ceiling(remaining.TotalDays));
    }
}
