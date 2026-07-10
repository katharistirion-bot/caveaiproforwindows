using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services.UserProfile;

/// <summary>Reads users/{uid} via Firestore REST (subscriber entitlement required).</summary>
public sealed class UserProfileService
{
    private readonly FirebaseRestClient _rest = new();

    public async Task<UserProfileDocument?> LoadProfileAsync(
        FirebaseIdToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var entitlement = await AccountSessionState.RefreshEntitlementAsync(cancellationToken)
            .ConfigureAwait(false);
        if (entitlement?.IsEntitled != true)
            throw new InvalidOperationException("Active subscription required to load publisher profile.");

        var uid = token.Subject?.Trim();
        if (string.IsNullOrWhiteSpace(uid))
            return null;

        return await _rest.GetUserProfileDocumentAsync(token, uid, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Firestore users/{uid} publisher profile - contract: docs/user-profile-contract.json</summary>
public sealed class UserProfileDocument
{
    public string Uid { get; init; } = "";

    public string FirstName { get; init; } = "";

    public string LastName { get; init; } = "";

    public string Country { get; init; } = "";

    public string? Continent { get; init; }

    public string? Bio { get; init; }

    public string? AvatarUrl { get; init; }

    public string? PublicSlug { get; init; }

    public string DisplayName =>
        string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

    /// <summary>True when first name, last name, and country are non-blank (publish gate).</summary>
    public bool IsCompleteForPublish() =>
        !string.IsNullOrWhiteSpace(FirstName)
        && !string.IsNullOrWhiteSpace(LastName)
        && !string.IsNullOrWhiteSpace(Country);
}