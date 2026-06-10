namespace CaveAiProForWindows.Services.Auth;

/// <summary>Persists the last verified entitlement for account UI (toolbar, welcome banner).</summary>
public static class AccountSessionState
{
    private static SubscriptionEntitlementResult? _lastEntitlement;
    private static bool _welcomeBannerDismissed;

    public static SubscriptionEntitlementResult? LastEntitlement => _lastEntitlement;

    public static void Apply(SubscriptionEntitlementResult result)
    {
        if (!result.IsEntitled)
            return;
        _lastEntitlement = result;
    }

    public static void Clear()
    {
        _lastEntitlement = null;
        _welcomeBannerDismissed = false;
    }

    public static void DismissWelcomeBanner() => _welcomeBannerDismissed = true;

    public static bool ShouldShowWelcomeBanner =>
        !_welcomeBannerDismissed && _lastEntitlement?.IsEntitled == true;

    public static string? WelcomeBannerMessage =>
        _lastEntitlement == null ? null : AccountStatusFormatter.FormatWelcomeBanner(_lastEntitlement);

    public static string AccountToolbarLabel
    {
        get
        {
            var email = FirebaseAuthSession.CurrentAccountEmail;
            if (!string.IsNullOrWhiteSpace(email))
                return email;

            return "Account";
        }
    }

    public static string? AccountAccessSummary =>
        _lastEntitlement == null ? null : AccountStatusFormatter.FormatAccessSummary(_lastEntitlement);

    public static async Task<SubscriptionEntitlementResult?> RefreshEntitlementAsync(
        CancellationToken cancellationToken = default)
    {
        var token = CloudPublish.CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
        if (token == null)
            return null;

        var service = new SubscriptionEntitlementService();
        var result = await service.ValidateMonthlySubscriptionAsync(token, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsEntitled)
            Apply(result);
        return result;
    }
}
