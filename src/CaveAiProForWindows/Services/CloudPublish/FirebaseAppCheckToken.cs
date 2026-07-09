namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Firebase App Check JWT from the website WebView bridge (no signature verification on desktop).</summary>
public sealed class FirebaseAppCheckToken
{
    public required string Raw { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    public bool IsExpired() => DateTimeOffset.UtcNow >= ExpiresAtUtc - Skew;

    public bool IsUsable() => !string.IsNullOrWhiteSpace(Raw) && !IsExpired();
}