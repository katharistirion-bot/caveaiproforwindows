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

        TokenUpdated?.Invoke(this, token);
    }

    public void Clear()
    {
        lock (_gate)
            _current = null;
    }
}
