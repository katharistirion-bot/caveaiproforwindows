using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.UserProfile;

namespace CaveAiProForWindows.Services.FollowPublisher;

/// <summary>Follow / unfollow published cave contributors (users/{uid}/followedPublishers).</summary>
public sealed class FollowPublisherRepository
{
    private readonly FirebaseRestClient _rest = new();

    public sealed class FollowedPublisherRow
    {
        public required string PublisherUid { get; init; }
        public long FollowedAtMs { get; init; }
        public string Name { get; init; } = "";
        public string PublicSlug { get; init; } = "";
    }

    public async Task<IReadOnlyList<FollowedPublisherRow>> ListFollowedAsync(CancellationToken cancellationToken = default)
    {
        var token = await RequireTokenAsync(cancellationToken).ConfigureAwait(false);
        var uid = token.Subject?.Trim() ?? throw new InvalidOperationException("Missing uid.");
        var ids = await _rest.ListSubcollectionDocumentIdsAsync(token, $"users/{uid}/followedPublishers", cancellationToken)
            .ConfigureAwait(false);
        var profileService = new UserProfileService();
        var rows = new List<FollowedPublisherRow>();
        foreach (var publisherUid in ids)
        {
            UserProfileDocument? profile = null;
            try
            {
                profile = await profileService.LoadProfileByUidAsync(token, publisherUid, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // enrichment optional
            }

            rows.Add(new FollowedPublisherRow
            {
                PublisherUid = publisherUid,
                FollowedAtMs = 0,
                Name = profile?.DisplayName ?? publisherUid,
                PublicSlug = profile?.PublicSlug ?? "",
            });
        }

        return rows.OrderByDescending(r => r.Name).ToList();
    }

    public async Task UnfollowAsync(string publisherUid, CancellationToken cancellationToken = default)
    {
        var token = await RequireTokenAsync(cancellationToken).ConfigureAwait(false);
        var uid = token.Subject?.Trim() ?? throw new InvalidOperationException("Missing uid.");
        await _rest.DeleteDocumentAsync(token, $"users/{uid}/followedPublishers/{publisherUid.Trim()}", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<FirebaseIdToken> RequireTokenAsync(CancellationToken cancellationToken)
    {
        var entitlement = await AccountSessionState.RefreshEntitlementAsync(cancellationToken).ConfigureAwait(false);
        if (entitlement?.IsEntitled != true)
            throw new InvalidOperationException("Active CaveAI Pro subscription required.");
        var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
        if (token == null)
            throw new InvalidOperationException("Sign in to manage followed contributors.");
        return token;
    }
}