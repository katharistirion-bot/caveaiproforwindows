using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services.Auth;

/// <summary>
/// Reads <c>user_entitlements/{uid}</c> and validates paid Play subscriptions, Play free-trial phases,
/// and the server-anchored 30-day install-grace trial (<c>INSTALL_GRACE</c>).
/// </summary>
public sealed class SubscriptionEntitlementService
{
    private readonly HttpClient _http;
    private readonly FirebaseProjectConfig _config;

    public SubscriptionEntitlementService(FirebaseProjectConfig? config = null, HttpMessageHandler? handler = null)
    {
        _config = config ?? FirebaseProjectConfig.LoadFromEnvironment();
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<SubscriptionEntitlementResult> ValidateMonthlySubscriptionAsync(
        FirebaseIdToken token,
        CancellationToken cancellationToken = default)
    {
        if (MicrosoftTestMode.IsActive)
            return MicrosoftTestMode.CreateSyntheticEntitlement();

        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrWhiteSpace(token.Subject))
            return Deny("Firebase token has no user id (sub).");

        var doc = await TryFetchEntitlementAsync(token, cancellationToken).ConfigureAwait(false);
        if (doc == null)
            return Deny(
                "No subscription profile found for this account. " +
                "Use the same Google account as CaveAI Pro on Android, ensure Play subscription or trial is active, " +
                "and open the Android app once if you rely on the 30-day install grace.");

        InstallGraceRecord? installGrace = null;
        if (string.Equals(doc.EntitlementSource, "INSTALL_GRACE", StringComparison.Ordinal))
        {
            if (!SubscriptionEntitlementPolicy.IsValidGraceDocId(doc.GraceDocId))
                return Deny("Install-grace trial is missing a valid graceDocId.", doc);

            installGrace = await TryFetchInstallGraceAsync(token, doc.GraceDocId!.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (installGrace == null)
                return Deny("Install-grace trial record was not found on the server.", doc);
        }

        return ValidateDocument(doc, installGrace);
    }

    /// <summary>
    /// Validates a parsed entitlement document. Pass <paramref name="installGrace"/> when
    /// <see cref="UserEntitlementDocument.EntitlementSource"/> is <c>INSTALL_GRACE</c>.
    /// </summary>
    public static SubscriptionEntitlementResult ValidateDocument(
        UserEntitlementDocument doc,
        InstallGraceRecord? installGrace = null)
    {
        if (!SubscriptionEntitlementPolicy.IsAllowedStatus(doc.Status))
        {
            return Deny(
                $"Subscription status is '{doc.Status ?? "unknown"}' (expected ACTIVE or TRIALING).",
                doc);
        }

        if (doc.PremiumCloudUntil == null)
            return Deny("Subscription expiry (premiumCloudUntil) is missing.", doc);

        var now = DateTimeOffset.UtcNow;
        if (doc.PremiumCloudUntil.Value <= now)
            return Deny("Subscription or trial period has expired.", doc);

        if (string.Equals(doc.EntitlementSource, "PLAY_SUBSCRIPTION", StringComparison.Ordinal))
            return Grant(doc, SubscriptionAccessKind.PaidPlaySubscription);

        if (string.Equals(doc.EntitlementSource, "INSTALL_GRACE", StringComparison.Ordinal))
        {
            if (!SubscriptionEntitlementPolicy.IsValidGraceDocId(doc.GraceDocId))
                return Deny("Install-grace trial is missing a valid graceDocId.", doc);

            if (installGrace == null)
            {
                return Deny(
                    "Install-grace trial requires server verification (app_install_grace document).",
                    doc);
            }

            if (!SubscriptionEntitlementPolicy.IsInstallGraceWindowOpen(installGrace.FirstSeenMsUtc, now))
            {
                return Deny(
                    $"Install-grace trial ended (>{SubscriptionEntitlementPolicy.InstallGracePeriodDays}-day window).",
                    doc);
            }

            return Grant(doc, SubscriptionAccessKind.InstallGraceTrial);
        }

        if (string.IsNullOrWhiteSpace(doc.EntitlementSource))
            return Grant(doc, SubscriptionAccessKind.LegacyEntitlement);

        var source = doc.EntitlementSource.Trim();
        return Deny($"Unknown entitlementSource '{source}'.", doc);
    }

    private async Task<UserEntitlementDocument?> TryFetchEntitlementAsync(
        FirebaseIdToken token,
        CancellationToken cancellationToken)
    {
        var uid = Uri.EscapeDataString(token.Subject!.Trim());
        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_config.ProjectId)}/databases/{Uri.EscapeDataString(_config.FirestoreDatabaseId)}/documents/user_entitlements/{uid}";

        using var json = await FetchFirestoreDocumentJsonAsync(token, url, cancellationToken).ConfigureAwait(false);
        return json == null ? null : FirestoreRestDocumentParser.TryParseUserEntitlement(json);
    }

    private async Task<InstallGraceRecord?> TryFetchInstallGraceAsync(
        FirebaseIdToken token,
        string graceDocId,
        CancellationToken cancellationToken)
    {
        var id = Uri.EscapeDataString(graceDocId);
        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_config.ProjectId)}/databases/{Uri.EscapeDataString(_config.FirestoreDatabaseId)}/documents/app_install_grace/{id}";

        using var json = await FetchFirestoreDocumentJsonAsync(token, url, cancellationToken).ConfigureAwait(false);
        if (json == null)
            return null;

        var root = json.RootElement;
        if (!root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
            return null;

        var firstSeenMs = FirestoreRestDocumentParser.GetInt64(fields, "firstSeenMs");
        return firstSeenMs == null ? null : new InstallGraceRecord(firstSeenMs.Value);
    }

    private async Task<JsonDocument?> FetchFirestoreDocumentJsonAsync(
        FirebaseIdToken token,
        string url,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        FirebaseAppCheckHeader.TryApply(req, CloudPublishWebViewHost.AppCheckTokenCache);

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not read Firestore document ({(int)resp.StatusCode}): {Truncate(body)}");
        }

        return JsonDocument.Parse(body);
    }

    private static SubscriptionEntitlementResult Grant(UserEntitlementDocument doc, SubscriptionAccessKind kind) =>
        new(true, null, doc, kind);

    private static SubscriptionEntitlementResult Deny(string reason, UserEntitlementDocument? doc = null) =>
        new(false, reason, doc);

    private static string Truncate(string? s, int max = 400) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
