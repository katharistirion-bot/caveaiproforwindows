using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SketchAssist;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Artifacts assembled on disk / in memory before cloud upload.</summary>
public sealed class CloudPublishArtifactBundle
{
    public required CaveProjectDocument Project { get; init; }

    /// <summary>Existing Firestore <c>published_caves</c> document id (same as web map <c>?cave=</c>).</summary>
    public required string PublishedCaveDocId { get; init; }

    public byte[]? AiMapPng { get; init; }

    public byte[]? StructureMaskPng { get; init; }

    /// <summary>UTF-8 JSON bytes — typically single-project <c>data.json</c> array.</summary>
    public byte[]? SurveyJsonUtf8 { get; init; }
}

/// <summary>
/// Orchestrates zero-touch publish: waits for sniffed token, uploads Storage objects, PATCHes Firestore.
/// </summary>
public sealed class CloudPublishService
{
    private readonly FirebaseAuthTokenCache _tokenCache;
    private readonly FirebaseRestClient _rest;

    public CloudPublishService(FirebaseAuthTokenCache tokenCache, FirebaseRestClient? rest = null)
    {
        _tokenCache = tokenCache;
        _rest = rest ?? new FirebaseRestClient();
    }

    /// <summary>
    /// Waits until a usable ID token appears in the cache (user must be signed in inside Public Library WebView).
    /// </summary>
    public async Task<FirebaseIdToken> WaitForTokenAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var existing = _tokenCache.TryGetUsableToken();
        if (existing != null)
            return existing;

        var tcs = new TaskCompletionSource<FirebaseIdToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        using var timer = new CancellationTokenSource(timeout);
        using var timerReg = timer.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                "No Firebase ID token was captured. Open Help → Public Cave Library, sign in with Google, browse the map briefly, then retry Push to Cloud.")));

        void OnToken(object? _, FirebaseIdToken t)
        {
            if (t.IsUsable())
                tcs.TrySetResult(t);
        }

        _tokenCache.TokenUpdated += OnToken;
        try
        {
            existing = _tokenCache.TryGetUsableToken();
            if (existing != null)
                return existing;
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _tokenCache.TokenUpdated -= OnToken;
        }
    }

    /// <summary>Upload AI map + optional survey JSON / structure mask; patch published cave metadata.</summary>
    public async Task<CloudPublishMetadata> PublishAsync(
        CloudPublishArtifactBundle bundle,
        FirebaseIdToken token,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrWhiteSpace(bundle.PublishedCaveDocId))
            throw new ArgumentException("Published cave document id is required.", nameof(bundle));

        var uid = token.Subject
            ?? throw new InvalidOperationException("Firebase token has no subject (uid).");
        var sessionPrefix = $"users/{uid}/desktop_publishes/{Guid.NewGuid():N}";
        var projectSlug = SanitizePathSegment(bundle.Project.Name);

        string? aiUrl = null;
        string? maskUrl = null;
        string? surveyPath = null;
        string? surveyUrl = null;

        if (bundle.AiMapPng is { Length: > 0 })
        {
            progress?.Report("Uploading AI map image…");
            var path = $"{sessionPrefix}/{projectSlug}/ai_map.png";
            var up = await _rest.UploadBytesAsync(token, path, bundle.AiMapPng, "image/png", cancellationToken)
                .ConfigureAwait(false);
            aiUrl = up.MediaUrl;
        }

        if (bundle.StructureMaskPng is { Length: > 0 })
        {
            progress?.Report("Uploading structure mask…");
            var path = $"{sessionPrefix}/{projectSlug}/structure_mask.png";
            var up = await _rest.UploadBytesAsync(token, path, bundle.StructureMaskPng, "image/png", cancellationToken)
                .ConfigureAwait(false);
            maskUrl = up.MediaUrl;
        }

        if (bundle.SurveyJsonUtf8 is { Length: > 0 })
        {
            progress?.Report("Uploading enriched survey JSON…");
            surveyPath = $"{sessionPrefix}/{projectSlug}/data.json";
            var up = await _rest.UploadBytesAsync(
                    token,
                    surveyPath,
                    bundle.SurveyJsonUtf8,
                    "application/json; charset=utf-8",
                    cancellationToken)
                .ConfigureAwait(false);
            surveyUrl = up.MediaUrl;
        }

        progress?.Report("Updating Firestore published cave…");
        var metadata = new CloudPublishMetadata
        {
            PublishedCaveDocId = bundle.PublishedCaveDocId.Trim(),
            CartographyImageUrl = aiUrl,
            StructureMaskUrl = maskUrl,
            SurveyJsonStoragePath = surveyPath,
            SurveyJsonMediaUrl = surveyUrl,
            SurveyArchiveSchemaVersion = bundle.Project.SurveyArchiveSchemaVersion,
            UpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        await _rest.PatchPublishedCaveAsync(token, metadata, cancellationToken).ConfigureAwait(false);
        progress?.Report("Publish complete.");
        return metadata;
    }

    /// <summary>Build survey JSON bytes using the same exporter as portable ZIP.</summary>
    public static byte[] SerializeProjectJsonUtf8(CaveProjectDocument project) =>
        System.Text.Encoding.UTF8.GetBytes(SurveyPortableZipExporter.SerializeSingleProjectArray(project));

    /// <summary>Capture structure mask PNG from current sketch editor state.</summary>
    public static byte[]? TryCaptureStructureMask(
        CaveProjectDocument project,
        System.Windows.Controls.Canvas? designLayer,
        double canvasWidth,
        double canvasHeight) =>
        SketchAssistStructureMaskExporter.TryExportStructureMaskPngFromEditor(
            project,
            designLayer,
            canvasWidth,
            canvasHeight);

    private static string SanitizePathSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "cave";
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var s = new string(chars).Trim('_');
        if (s.Length == 0)
            return "cave";
        return s.Length > 48 ? s[..48] : s;
    }
}
