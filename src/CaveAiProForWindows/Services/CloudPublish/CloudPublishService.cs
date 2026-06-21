using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Services.SketchAssist;
using CaveAiProForWindows.Views;

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
/// Orchestrates Push to Cloud: secure WebView2 auth, Storage uploads, Firestore PATCH.
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
    /// Returns a cached token or opens the secure sign-in WebView2 dialog.
    /// </summary>
    public async Task<FirebaseIdToken> AcquireTokenAsync(
        Window? owner,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (MicrosoftTestMode.IsActive)
            throw MicrosoftTestMode.CreateNetworkBlockedException("Firebase sign-in");

        var existing = _tokenCache.TryGetUsableToken();
        if (existing != null)
            return existing;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            await DesktopAuthWindow.AcquireTokenAsync(owner, _tokenCache).ConfigureAwait(true));

        return await WaitForTokenAsync(timeout, linked.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Waits until a usable ID token appears in the cache (e.g. after sign-in in any attached WebView).
    /// </summary>
    public async Task<FirebaseIdToken> WaitForTokenAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (MicrosoftTestMode.IsActive)
            throw MicrosoftTestMode.CreateNetworkBlockedException("Firebase sign-in");

        var existing = _tokenCache.TryGetUsableToken();
        if (existing != null)
            return existing;

        var tcs = new TaskCompletionSource<FirebaseIdToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        using var timer = new CancellationTokenSource(timeout);
        using var timerReg = timer.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                "Firebase sign-in timed out. Use Publish to Cloud to open the secure sign-in window, sign in with Google, then retry.")));

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
        ReferenceSurveyLinkService.TryGetLink(bundle.Project, out var refLink);

        string? publishedCaveName = null;
        try
        {
            var existing = await _rest.GetPublishedCaveDocumentAsync(
                    bundle.PublishedCaveDocId.Trim(),
                    token,
                    cancellationToken)
                .ConfigureAwait(false);
            publishedCaveName = existing.CaveName;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read published cave name for search key: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(publishedCaveName))
            publishedCaveName = CaveProjectDisplayNames.GetDisplayName(bundle.Project);

        var metadata = new CloudPublishMetadata
        {
            PublishedCaveDocId = bundle.PublishedCaveDocId.Trim(),
            CartographyImageUrls = string.IsNullOrWhiteSpace(aiUrl) ? null : new[] { aiUrl },
            StructureMaskUrl = maskUrl,
            SurveyJsonStoragePath = surveyPath,
            SurveyJsonMediaUrl = surveyUrl,
            SurveyOverlaySummary = SurveyAnnotationReportFormatter.BuildOverlaySummary(bundle.Project),
            SurveyArchiveSchemaVersion = bundle.Project.SurveyArchiveSchemaVersion,
            ReferenceCatalogId = refLink?.Id,
            ReferenceCatalogCountry = refLink?.Country,
            CaveNameSearchKey = PublicLibrarySearchKey.FromCaveName(publishedCaveName),
            UpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        await _rest.PatchPublishedCaveAsync(token, metadata, cancellationToken).ConfigureAwait(false);
        progress?.Report("Publish complete.");
        return metadata;
    }

    /// <summary>Build survey JSON bytes using the same exporter as portable ZIP (normalized for publish).</summary>
    public static byte[] SerializeProjectJsonUtf8(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        CaveProjectJsonWriteNormalizer.Prepare(project);
        if (string.IsNullOrWhiteSpace(project.SurveyArchiveSchemaVersion))
            project.SurveyArchiveSchemaVersion = "2";
        return System.Text.Encoding.UTF8.GetBytes(SurveyPortableZipExporter.SerializeSingleProjectArray(project));
    }

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
