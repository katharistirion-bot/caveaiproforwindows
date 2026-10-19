using System.Text.RegularExpressions;

namespace CaveAiProForWindows.Services.Auth;

/// <summary>
/// Mirrors Firebase <c>hasActivePremiumCloudEntitlement</c> and Android
/// <c>UserEntitlementFirestore.rulesDenyReason</c> for the Desktop Companion gate.
/// </summary>
internal static class SubscriptionEntitlementPolicy
{
    /// <summary>Must match Android <c>InstallGracePeriod.INSTALL_GRACE_PERIOD_DAYS</c> and Cloud Functions.</summary>
    public const int InstallGracePeriodDays = 30;

    public static readonly TimeSpan InstallGracePeriod = TimeSpan.FromDays(InstallGracePeriodDays);

    private static readonly Regex GraceDocIdPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Same as Android <c>UserEntitlementFirestore</c>, website <c>premiumEntitlement.js</c>,
    /// and <c>firestore.rules</c> <c>hasActivePremiumCloudEntitlement</c>: paid/owner/install-grace
    /// are stored as <c>ACTIVE</c>. Play Billing <c>TRIALING</c> is not a Firestore status.
    /// </summary>
    public static bool IsAllowedStatus(string? status) =>
        string.Equals(status, "ACTIVE", StringComparison.Ordinal);

    public static bool IsValidGraceDocId(string? graceDocId) =>
        !string.IsNullOrWhiteSpace(graceDocId) && GraceDocIdPattern.IsMatch(graceDocId.Trim());

    public static bool IsInstallGraceWindowOpen(long firstSeenMsUtc, DateTimeOffset nowUtc)
    {
        if (firstSeenMsUtc <= 0)
            return false;

        var graceEndsAtMs = firstSeenMsUtc + (long)InstallGracePeriod.TotalMilliseconds;
        return nowUtc.ToUnixTimeMilliseconds() < graceEndsAtMs;
    }
}
