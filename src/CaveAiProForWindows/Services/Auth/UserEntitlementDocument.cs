namespace CaveAiProForWindows.Services.Auth;

/// <summary>
/// Parsed <c>user_entitlements/{uid}</c> document from Firestore (Android Play Billing backend).
/// </summary>
public sealed record UserEntitlementDocument(
    string Status,
    DateTimeOffset? PremiumCloudUntil,
    string? EntitlementSource,
    string? GraceDocId = null);

/// <summary>
/// Result of validating whether the signed-in user may use the Desktop Companion.
/// </summary>
public enum SubscriptionAccessKind
{
    None,
    PaidPlaySubscription,
    InstallGraceTrial,
    LegacyEntitlement,
}

public sealed record SubscriptionEntitlementResult(
    bool IsActiveMonthlySubscription,
    string? DenialReason,
    UserEntitlementDocument? Document = null,
    SubscriptionAccessKind AccessKind = SubscriptionAccessKind.None)
{
    /// <summary>True when the user may use the Desktop Companion (paid, trialing, or install-grace trial).</summary>
    public bool IsEntitled => IsActiveMonthlySubscription;

    public const string RequiredUserMessage =
        "An active CaveAI Pro subscription or trial is required to use the Desktop Companion";

    /// <summary>Shown when Firebase auth succeeded but entitlement validation failed.</summary>
    public const string AccessDeniedNoSubscriptionMessage =
        "No active CaveAI Pro subscription or trial was found for this Google account. " +
        "Sign in with the same Google account you use in CaveAI Pro on Android (Google Play). " +
        "If you are in your 30-day trial, open the Android app once to sync your account, " +
        "or subscribe to CaveAI Pro on Google Play, then sign in here again.";
}
