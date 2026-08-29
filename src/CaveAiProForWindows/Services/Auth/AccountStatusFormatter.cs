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
            SubscriptionAccessKind.Owner when untilText != null =>
                $"Owner access · until {untilText}",
            SubscriptionAccessKind.Owner =>
                "Owner access · active",
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
        return $"Signed in — {summary}";
    }

    public static string FormatAccountBanner(SubscriptionEntitlementResult result)
    {
        var summary = FormatAccessSummary(result);
        if (result.AccessKind == SubscriptionAccessKind.InstallGraceTrial &&
            result.Document?.PremiumCloudUntil is { } until)
        {
            var days = DaysRemaining(until);
            return $"{summary} · {days} day(s) left on trial · Manage subscription on Google Play (Android).";
        }

        if (result.AccessKind == SubscriptionAccessKind.PaidPlaySubscription)
            return $"{summary} · Manage subscription on Google Play.";

        if (result.AccessKind == SubscriptionAccessKind.Owner)
            return summary;

        return $"{summary} · Manage subscription on Google Play (Android app).";
    }

    public static string FormatAboutSubscription(SubscriptionEntitlementResult? result)
    {
        if (result == null || !result.IsEntitled)
            return "Subscription status: sign in to view entitlement and trial days remaining.";

        var summary = FormatAccessSummary(result);
        if (result.AccessKind == SubscriptionAccessKind.InstallGraceTrial &&
            result.Document?.PremiumCloudUntil is { } until)
        {
            var days = DaysRemaining(until);
            return $"{summary}\n\nTrial: {days} day(s) remaining. Subscribe on Google Play (Android) with the same Google account.";
        }

        if (result.AccessKind == SubscriptionAccessKind.Owner)
            return summary;

        return $"{summary}\n\nManage subscription on Google Play (Android app required for billing).";
    }

    private static int DaysRemaining(DateTimeOffset untilUtc)
    {
        var remaining = untilUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return 0;
        return Math.Max(1, (int)Math.Ceiling(remaining.TotalDays));
    }
}
