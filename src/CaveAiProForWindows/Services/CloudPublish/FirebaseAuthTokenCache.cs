namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Thread-safe holder for the most recent Firebase ID token from the desktop auth bridge.</summary>
public sealed class FirebaseAuthTokenCache
{
    private readonly object _gate = new();
    private FirebaseIdToken? _current;

    public event EventHandler<FirebaseIdToken>? TokenUpdated;

    public FirebaseIdToken? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    /// <summary>Returns a non-expired token if available.</summary>
    public FirebaseIdToken? TryGetUsableToken()
    {
        lock (_gate)
            return _current is { } t && t.IsUsable() ? t : null;
    }

    /// <summary>
    /// Attempts a silent token refresh using the persisted Firebase refresh token.
    /// Returns the new token on success, null if no refresh token is stored or the refresh fails.
    /// On success the token is stored in this cache and persisted to disk.
    /// </summary>
    public async Task<FirebaseIdToken?> TrySilentRefreshAsync(CancellationToken cancellationToken = default)
    {
        var existing = TryGetUsableToken();
        if (existing != null)
            return existing;

        var refreshToken = FirebaseAuthTokenStore.TryLoadRefreshToken();
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        using var client = new FirebaseTokenRefreshClient();
        var token = await client.TryRefreshAsync(refreshToken!, cancellationToken).ConfigureAwait(false);
        if (token == null)
            return null;

        Update(token);
        return token;
    }

    public void Update(FirebaseIdToken token)
    {
        lock (_gate)
        {
            if (_current is { } prev &&
                string.Equals(prev.Raw, token.Raw, StringComparison.Ordinal) &&
                prev.ExpiresAtUtc == token.ExpiresAtUtc)
                return;
            _current = token;
        }

        FirebaseAuthTokenStore.TrySave(token);
        TokenUpdated?.Invoke(this, token);
    }

    public void Clear()
    {
        lock (_gate)
            _current = null;

        FirebaseAuthTokenStore.TryClear();
    }
}
