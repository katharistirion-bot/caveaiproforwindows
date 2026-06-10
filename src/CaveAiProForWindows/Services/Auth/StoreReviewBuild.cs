namespace CaveAiProForWindows.Services.Auth;

/// <summary>
/// Compile-time Microsoft Store certification build. Set <c>STORE_REVIEW_UNLOCKED</c> only via
/// <c>MicrosoftStore-Review-Win64</c> publish profile — never in production Store or sideload builds.
/// </summary>
public static class StoreReviewBuild
{
#if STORE_REVIEW_UNLOCKED
    public const bool IsActive = true;
#else
    public const bool IsActive = false;
#endif

    public const string BannerMessage = "Store review build — subscription checks disabled";

    /// <summary>Synthetic entitlement applied when the app lock is bypassed for Store certification.</summary>
    public static SubscriptionEntitlementResult CreateSyntheticEntitlement() =>
        new(
            true,
            null,
            new UserEntitlementDocument(
                "ACTIVE",
                DateTimeOffset.UtcNow.AddYears(1),
                "PLAY_SUBSCRIPTION"),
            SubscriptionAccessKind.PaidPlaySubscription);

    /// <summary>Records review entitlement for account UI without Firestore.</summary>
    public static void ActivateSession() => AccountSessionState.Apply(CreateSyntheticEntitlement());
}
