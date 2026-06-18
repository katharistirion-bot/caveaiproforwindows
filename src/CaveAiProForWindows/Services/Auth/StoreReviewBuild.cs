namespace CaveAiProForWindows.Services.Auth;

/// <summary>
/// Legacy alias for <see cref="MicrosoftTestMode"/>. Prefer <c>MICROSOFT_TEST_MODE</c> via
/// <c>MicrosoftStore-Review-Win64</c> publish profile — never in production Store builds.
/// </summary>
public static class StoreReviewBuild
{
    public static bool IsActive => MicrosoftTestMode.IsActive;

    public const string BannerMessage = MicrosoftTestMode.BannerMessage;

    public static SubscriptionEntitlementResult CreateSyntheticEntitlement() =>
        MicrosoftTestMode.CreateSyntheticEntitlement();

    public static void ActivateSession() => MicrosoftTestMode.ActivateSession();
}
