namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Parsed Firebase Auth ID token (JWT) captured from WebView2 outbound requests.</summary>
public sealed class FirebaseIdToken
{
    public required string Raw { get; init; }

    public string? Subject { get; init; }

    public string? Email { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public DateTimeOffset IssuedAtUtc { get; init; }

    public bool IsExpired(DateTimeOffset? now = null) =>
        (now ?? DateTimeOffset.UtcNow) >= ExpiresAtUtc.AddSeconds(-30);

    public bool IsUsable(DateTimeOffset? now = null) => !IsExpired(now);
}
