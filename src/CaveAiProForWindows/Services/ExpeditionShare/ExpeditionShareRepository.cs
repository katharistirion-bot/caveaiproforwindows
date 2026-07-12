using System.Text.Json;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.ExpeditionShare;
using CaveAiProForWindows.Services.UserProfile;

namespace CaveAiProForWindows.Services.ExpeditionShare;

/// <summary>Opt-in expedition sharing for subscriber map layer.</summary>
public sealed class ExpeditionShareRepository
{
    private const string Collection = "expedition_shares";
    private const int DisclaimerVersion = 1;
    private readonly FirebaseRestClient _rest = new();

    public sealed class ExpeditionShareState
    {
        public bool Active { get; init; }
        public string CaveKey { get; init; } = "";
        public string CaveName { get; init; } = "";
        public string Country { get; init; } = "";
        public double Lat { get; init; }
        public double Lon { get; init; }
        public long StartedAtMs { get; init; }
        public long? ExpectedExitAtMs { get; init; }
    }

    public async Task<ExpeditionShareState> LoadOwnShareAsync(CancellationToken cancellationToken = default)
    {
        var token = await RequireTokenAsync(cancellationToken).ConfigureAwait(false);
        var uid = token.Subject?.Trim();
        if (string.IsNullOrWhiteSpace(uid))
            return new ExpeditionShareState { Active = false };

        var json = await _rest.GetDocumentJsonAsync(token, $"{Collection}/{uid}", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return new ExpeditionShareState { Active = false };

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("fields", out var fields))
            return new ExpeditionShareState { Active = false };
        var status = ReadString(fields, "status");
        if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            return new ExpeditionShareState { Active = false };

        return new ExpeditionShareState
        {
            Active = true,
            CaveKey = ReadString(fields, "caveKey"),
            CaveName = ReadString(fields, "caveName"),
            Country = ReadString(fields, "country"),
            Lat = ReadDouble(fields, "lat"),
            Lon = ReadDouble(fields, "lon"),
            StartedAtMs = ReadLong(fields, "startedAtMs"),
            ExpectedExitAtMs = ReadOptionalLong(fields, "expectedExitAtMs"),
        };
    }

    public async Task<ExpeditionShareState> StartSharingAsync(
        CaveProjectDocument project,
        string? publishedDocId,
        long? expectedExitAtMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var token = await RequireTokenAsync(cancellationToken).ConfigureAwait(false);
        var uid = token.Subject?.Trim() ?? throw new InvalidOperationException("Firebase token missing uid.");
        var lat = ExpeditionShareDisplay.ParseCoordinate(project.Lat?.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ?? project.Lat;
        var lon = ExpeditionShareDisplay.ParseCoordinate(project.Lon?.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ?? project.Lon;
        if (lat is null or < -90 or > 90 || lon is null or < -180 or > 180)
            throw new InvalidOperationException("Set entrance latitude and longitude before sharing.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        UserProfileDocument? profile = null;
        try
        {
            profile = await new UserProfileService().LoadProfileAsync(token, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // optional snapshot
        }

        var pairs = new List<KeyValuePair<string, object?>>
        {
            new("leaderUid", uid),
            new("caveKey", ExpeditionShareDisplay.CaveKeyForProject(project, publishedDocId)),
            new("caveName", project.Name?.Trim() ?? "Cave"),
            new("lat", lat.Value),
            new("lon", lon.Value),
            new("startedAtMs", now),
            new("status", "active"),
            new("sharingConsentAtMs", now),
            new("disclaimerVersion", DisclaimerVersion),
            new("teamLabel", "Expedition team"),
        };
        string? country = profile?.Country?.Trim();
        if (!string.IsNullOrEmpty(country)) pairs.Add(new("country", country));
        if (expectedExitAtMs is long exitAt && exitAt > now) pairs.Add(new("expectedExitAtMs", exitAt));
        var displayName = profile?.DisplayName;
        if (!string.IsNullOrWhiteSpace(displayName)) pairs.Add(new("leaderDisplayName", displayName));
        if (!string.IsNullOrWhiteSpace(profile?.AvatarUrl)) pairs.Add(new("leaderAvatarUrl", profile!.AvatarUrl!));

        await UpsertFieldsAsync(token, $"{Collection}/{uid}", pairs, cancellationToken).ConfigureAwait(false);
        return new ExpeditionShareState
        {
            Active = true,
            CaveKey = (string)pairs[1].Value!,
            CaveName = (string)pairs[2].Value!,
            Country = country ?? "",
            Lat = lat.Value,
            Lon = lon.Value,
            StartedAtMs = now,
            ExpectedExitAtMs = expectedExitAtMs,
        };
    }

    public async Task<ExpeditionShareState> EndSharingAsync(CancellationToken cancellationToken = default)
    {
        var token = await RequireTokenAsync(cancellationToken).ConfigureAwait(false);
        var uid = token.Subject?.Trim() ?? throw new InvalidOperationException("Firebase token missing uid.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await UpsertFieldsAsync(token, $"{Collection}/{uid}", new[]
        {
            new KeyValuePair<string, object?>("status", "ended"),
            new KeyValuePair<string, object?>("endedAtMs", now),
        }, cancellationToken).ConfigureAwait(false);
        return new ExpeditionShareState { Active = false };
    }

    public async Task<IReadOnlyList<ExpeditionShareDisplay.ExpeditionShareRow>> LoadVisibleActiveSharesAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await RequireTokenAsync(cancellationToken).ConfigureAwait(false);
        return await _rest.QueryActiveExpeditionSharesAsync(token, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertFieldsAsync(
        FirebaseIdToken token,
        string documentPath,
        IEnumerable<KeyValuePair<string, object?>> pairs,
        CancellationToken cancellationToken)
    {
        var fields = FirestoreFieldBuilder.BuildFields(pairs);
        if (await _rest.DocumentExistsAsync(token, documentPath, cancellationToken).ConfigureAwait(false))
            await _rest.PatchDocumentAsync(token, documentPath, fields, cancellationToken).ConfigureAwait(false);
        else
        {
            var slash = documentPath.IndexOf('/');
            if (slash <= 0)
                throw new ArgumentException("Document path must be collection/documentId.", nameof(documentPath));
            await _rest.CreateDocumentWithIdAsync(
                token,
                documentPath[..slash],
                documentPath[(slash + 1)..],
                fields,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<FirebaseIdToken> RequireTokenAsync(CancellationToken cancellationToken)
    {
        var entitlement = await AccountSessionState.RefreshEntitlementAsync(cancellationToken).ConfigureAwait(false);
        if (entitlement?.IsEntitled != true)
            throw new InvalidOperationException("Active CaveAI Pro subscription required for expedition share.");
        var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
        if (token == null)
            throw new InvalidOperationException("Sign in to share expedition status.");
        return token;
    }

    private static string ReadString(JsonElement fields, string key)
    {
        if (!fields.TryGetProperty(key, out var el) || !el.TryGetProperty("stringValue", out var sv))
            return "";
        return sv.GetString() ?? "";
    }

    private static double ReadDouble(JsonElement fields, string key)
    {
        if (!fields.TryGetProperty(key, out var el)) return 0;
        if (el.TryGetProperty("doubleValue", out var dv) && dv.TryGetDouble(out var d)) return d;
        if (el.TryGetProperty("integerValue", out var iv) && long.TryParse(iv.GetString(), out var l)) return l;
        return 0;
    }

    private static long ReadLong(JsonElement fields, string key)
    {
        if (!fields.TryGetProperty(key, out var el)) return 0;
        if (el.TryGetProperty("integerValue", out var iv) && long.TryParse(iv.GetString(), out var l)) return l;
        if (el.TryGetProperty("doubleValue", out var dv) && dv.TryGetDouble(out var d)) return (long)d;
        return 0;
    }

    private static long? ReadOptionalLong(JsonElement fields, string key)
    {
        if (!fields.TryGetProperty(key, out var el)) return null;
        if (el.TryGetProperty("integerValue", out var iv) && long.TryParse(iv.GetString(), out var l)) return l;
        return null;
    }
}