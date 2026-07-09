namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Thread-safe holder for the most recent App Check JWT from the website WebView bridge.</summary>
public sealed class FirebaseAppCheckTokenCache
{
    private readonly object _gate = new();
    private FirebaseAppCheckToken? _current;

    public event EventHandler<FirebaseAppCheckToken>? TokenUpdated;

    public FirebaseAppCheckToken? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public FirebaseAppCheckToken? TryGetUsableToken()
    {
        lock (_gate)
            return _current is { } t && t.IsUsable() ? t : null;
    }

    public void Update(FirebaseAppCheckToken token)
    {
        lock (_gate)
        {
            if (_current is { } prev
                && string.Equals(prev.Raw, token.Raw, StringComparison.Ordinal)
                && prev.ExpiresAtUtc == token.ExpiresAtUtc)
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