using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.Collaboration;

using CaveAiProForWindows.Services.Auth;

namespace CaveAiProForWindows.Services;

/// <summary>Polls shared-project comments and tracks unread count for status-bar badge.</summary>
public sealed class CollaborationNotificationService : IDisposable
{
    private readonly ProjectCollaborationClient _client = new();
    private System.Threading.Timer? _timer;
    private long _lastSeenCommentMs;

    public event EventHandler<int>? UnreadCountChanged;

    public int UnreadCount { get; private set; }

    public string? TrackedProjectId { get; private set; }

    public void TrackProject(string? sharedProjectId)
    {
        TrackedProjectId = string.IsNullOrWhiteSpace(sharedProjectId) ? null : sharedProjectId.Trim();
        _lastSeenCommentMs = 0;
        UnreadCount = 0;
        UnreadCountChanged?.Invoke(this, 0);
        RestartTimer();
    }

    public void MarkAllRead()
    {
        _lastSeenCommentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SetUnread(0);
    }

    private void RestartTimer()
    {
        _timer?.Dispose();
        if (string.IsNullOrWhiteSpace(TrackedProjectId))
            return;

        _timer = new System.Threading.Timer(_ => _ = PollAsync(), null, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(45));
    }

    private async Task PollAsync()
    {
        if (MicrosoftTestMode.IsActive)
            return;

        if (string.IsNullOrWhiteSpace(TrackedProjectId))
            return;

        var token = FirebaseAuthTokenStore.TryLoad();
        if (token == null || !token.IsUsable())
            return;

        try
        {
            var comments = await _client.ListCommentsAsync(TrackedProjectId!).ConfigureAwait(false);
            var unread = comments.Count(c => c.CreatedAtMs > _lastSeenCommentMs);
            SetUnread(unread);
        }
        catch
        {
            /* offline / no auth */
        }
    }

    private void SetUnread(int count)
    {
        if (count == UnreadCount)
            return;
        UnreadCount = count;
        UnreadCountChanged?.Invoke(this, count);
    }

    public void Dispose() => _timer?.Dispose();
}
