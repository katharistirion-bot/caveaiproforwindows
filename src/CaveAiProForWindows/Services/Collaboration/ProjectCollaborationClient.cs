using System.Text.Json.Serialization;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services.Collaboration;

/// <summary>Firebase Callable client for shared project comments and push fan-out.</summary>
public sealed class ProjectCollaborationClient
{
    private readonly FirebaseCallableClient _shareClient = new("shareProjectCollaboration");
    private readonly FirebaseCallableClient _commentClient = new("postProjectComment");
    private readonly FirebaseCallableClient _listClient = new("listProjectComments");
    private readonly FirebaseCallableClient _registerTokenClient = new("registerDeviceToken");
    private readonly Func<FirebaseIdToken?> _tokenProvider;

    public ProjectCollaborationClient(Func<FirebaseIdToken?>? tokenProvider = null)
    {
        _tokenProvider = tokenProvider ?? (() => FirebaseAuthTokenStore.TryLoad());
    }

    public async Task<string> ShareProjectAsync(
        string projectName,
        string? surveyJsonUrl,
        IReadOnlyList<string>? memberEmails,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        var result = await _shareClient.InvokeAsync<ShareProjectResult>(
            token,
            new
            {
                projectName,
                surveyJsonUrl,
                memberEmails,
            },
            cancellationToken).ConfigureAwait(false);
        return result.ProjectId ?? throw new InvalidOperationException("Share project returned no id.");
    }

    public async Task PostCommentAsync(string projectId, string text, CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        await _commentClient.InvokeAsync<object>(
            token,
            new { projectId, text },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectComment>> ListCommentsAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        var result = await _listClient.InvokeAsync<ListCommentsResult>(
            token,
            new { projectId },
            cancellationToken).ConfigureAwait(false);
        return result.Comments ?? (IReadOnlyList<ProjectComment>)Array.Empty<ProjectComment>();
    }

    public async Task RegisterDeviceTokenAsync(string fcmToken, string platform, CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        await _registerTokenClient.InvokeAsync<object>(
            token,
            new { fcmToken, platform },
            cancellationToken).ConfigureAwait(false);
    }

    private FirebaseIdToken RequireToken()
    {
        var token = _tokenProvider()
            ?? throw new InvalidOperationException("Sign in with Google to use collaboration features.");
        if (!token.IsUsable())
            throw new InvalidOperationException("Your sign-in session expired. Sign in again.");
        return token;
    }

    private sealed class ShareProjectResult
    {
        [JsonPropertyName("projectId")]
        public string? ProjectId { get; set; }
    }

    private sealed class ListCommentsResult
    {
        [JsonPropertyName("comments")]
        public List<ProjectComment>? Comments { get; set; }
    }
}
