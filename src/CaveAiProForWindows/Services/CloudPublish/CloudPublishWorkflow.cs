using System.IO;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.ViewModels;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Progress update for Push to Cloud (main window + sketch editor).</summary>
public sealed class CloudPublishProgressUpdate
{
    public required string Message { get; init; }

    public double? ProgressPercent { get; init; }

    public bool IsIndeterminate { get; init; }

    public bool ShowAuthHint { get; init; }

    public bool HasError { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>Shared Push to Cloud orchestration for toolbar and sketch editor.</summary>
public static class CloudPublishWorkflow
{
    private static readonly TimeSpan TokenWaitTimeout = TimeSpan.FromSeconds(90);

    public sealed class Request
    {
        public required Func<CaveProjectDocument?> GetProject { get; init; }

        public required Func<bool> GetLegalTermsAccepted { get; init; }

        public required Func<CloudPublishArtifactCapture?> CaptureArtifacts { get; init; }

        public required Func<Window?> GetOwnerWindow { get; init; }

        public Action<string>? PersistLinkedLibraryCaveId { get; init; }

        public required IProgress<CloudPublishProgressUpdate> Progress { get; init; }

        public CloudPublishService? PublishService { get; init; }

        public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
    }

    public static async Task<CloudPublishMetadata?> RunAsync(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = request.GetProject();
        if (project == null)
        {
            ReportError(request.Progress, "Select a survey project before publishing.");
            return null;
        }

        if (!request.GetLegalTermsAccepted())
        {
            ReportError(request.Progress, "Accept the disclaimer on LEGAL & SETTINGS before using Push to Cloud.");
            return null;
        }

        if (!CloudPublishChecklistDialog.Confirm(
                request.GetOwnerWindow(),
                project,
                request.GetLegalTermsAccepted()))
        {
            ReportError(request.Progress, "Publish cancelled at checklist.");
            return null;
        }

        var docId = await ResolvePublishedCaveDocIdAsync(project, request).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(docId))
        {
            ReportError(request.Progress, "Publish cancelled — a Cave Library document id is required.");
            return null;
        }

        var publishService = request.PublishService
            ?? new CloudPublishService(CloudPublishWebViewHost.TokenCache);

        try
        {
            request.Progress.Report(new CloudPublishProgressUpdate
            {
                Message = "Waiting for Firebase sign-in…",
                IsIndeterminate = true,
                ShowAuthHint = CloudPublishWebViewHost.TokenCache.TryGetUsableToken() == null,
            });

            var token = await publishService.AcquireTokenAsync(
                    request.GetOwnerWindow(),
                    TokenWaitTimeout,
                    request.CancellationToken)
                .ConfigureAwait(true);

            var rest = new FirebaseRestClient();
            var profileReady = await CloudPublishProfileGate.EnsureReadyForPublishAsync(
                    rest,
                    token,
                    docId,
                    request.GetOwnerWindow(),
                    request.CancellationToken)
                .ConfigureAwait(true);
            if (!profileReady)
            {
                ReportError(
                    request.Progress,
                    "Complete your publisher profile (first name, last name, country) at caveaipro.com/account/profile before publishing.");
                return null;
            }

            request.Progress.Report(new CloudPublishProgressUpdate
            {
                Message = "Signed in — preparing artifacts…",
                ProgressPercent = 10,
                IsIndeterminate = false,
            });

            CloudPublishArtifactCapture? capture = null;
            await Application.Current.Dispatcher.InvokeAsync(() => capture = request.CaptureArtifacts());
            if (capture == null)
            {
                ReportError(request.Progress, "Could not capture publish artifacts for the active project.");
                return null;
            }

            var bundle = new CloudPublishArtifactBundle
            {
                Project = project,
                PublishedCaveDocId = docId,
                AiMapPng = capture.AiMapPng,
                StructureMaskPng = capture.StructureMaskPng,
                SurveyJsonUtf8 = capture.SurveyJsonUtf8,
                GalleryPhotos = capture.PendingGalleryPhotos,
            };

            var stringProgress = new Progress<string>(message =>
            {
                request.Progress.Report(new CloudPublishProgressUpdate
                {
                    Message = message,
                    ProgressPercent = MapProgressPercent(message),
                    IsIndeterminate = false,
                });
            });

            var metadata = await publishService.PublishAsync(
                    bundle,
                    token,
                    stringProgress,
                    request.CancellationToken)
                .ConfigureAwait(true);

            CloudPublishHistoryStore.Record(
                project.Name,
                docId,
                CaveProjectDisplayNames.GetDisplayName(project));

            request.Progress.Report(new CloudPublishProgressUpdate
            {
                Message = $"Publish complete — updated published_caves/{docId}.",
                ProgressPercent = 100,
                IsIndeterminate = false,
            });

            return metadata;
        }
        catch (OperationCanceledException)
        {
            request.Progress.Report(new CloudPublishProgressUpdate
            {
                Message = "Publish cancelled.",
                IsIndeterminate = false,
            });
            return null;
        }
        catch (TimeoutException)
        {
            ReportError(request.Progress, UserFacingErrors.SignInTimedOut(), showAuthHint: true);
            return null;
        }
        catch (Exception ex)
        {
            ReportError(request.Progress, UserFacingErrors.CloudPublishFailed(ex.Message));
            return null;
        }
    }

    private static async Task<string?> ResolvePublishedCaveDocIdAsync(
        CaveProjectDocument project,
        Request request)
    {
        var existing = LinkedLibraryCaveIdResolver.TryGet(project);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        string? entered = null;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var owner = request.GetOwnerWindow();
            if (LinkedLibraryCaveIdPromptWindow.TryPrompt(owner, null, out var id))
                entered = id;
        });

        if (string.IsNullOrWhiteSpace(entered))
            return null;

        LinkedLibraryCaveIdResolver.Set(project, entered);
        request.PersistLinkedLibraryCaveId?.Invoke(entered);
        return entered.Trim();
    }

    private static double MapProgressPercent(string message) => message switch
    {
        var m when m.StartsWith("Uploading AI map", StringComparison.OrdinalIgnoreCase) => 30,
        var m when m.StartsWith("Uploading structure mask", StringComparison.OrdinalIgnoreCase) => 50,
        var m when m.StartsWith("Uploading enriched survey", StringComparison.OrdinalIgnoreCase) => 70,
        var m when m.StartsWith("Updating Firestore", StringComparison.OrdinalIgnoreCase) => 90,
        var m when m.StartsWith("Publish complete", StringComparison.OrdinalIgnoreCase) => 100,
        _ => double.NaN,
    };

    private static void ReportError(
        IProgress<CloudPublishProgressUpdate> progress,
        string message,
        bool showAuthHint = false)
    {
        progress.Report(new CloudPublishProgressUpdate
        {
            Message = "Publish failed.",
            HasError = true,
            ErrorMessage = message,
            ShowAuthHint = showAuthHint,
            IsIndeterminate = false,
        });
    }
}

public static class CloudPublishRetryStore
{
    private static string RetryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CaveAiProForWindows", "cloud-publish-retry-queue.json");

    public static IReadOnlyList<CloudPublishRetryEntry> LoadAll()
    {
        try
        {
            if (!File.Exists(RetryPath)) return [];
            return System.Text.Json.JsonSerializer.Deserialize<List<CloudPublishRetryEntry>>(File.ReadAllText(RetryPath)) ?? [];
        }
        catch { return []; }
    }

    public static void Enqueue(string projectName, string? backupPath, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(projectName)) return;
        var list = LoadAll().ToList();
        list.RemoveAll(e => string.Equals(e.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, new CloudPublishRetryEntry { ProjectName = projectName, BackupPath = backupPath, ErrorMessage = errorMessage, FailedAtUtc = DateTime.UtcNow });
        while (list.Count > 20) list.RemoveAt(list.Count - 1);
        var dir = Path.GetDirectoryName(RetryPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(RetryPath, System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Remove(string projectName)
    {
        var list = LoadAll().Where(e => !string.Equals(e.ProjectName, projectName, StringComparison.OrdinalIgnoreCase)).ToList();
        var dir = Path.GetDirectoryName(RetryPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(RetryPath, System.Text.Json.JsonSerializer.Serialize(list));
    }

    public static void Clear()
    {
        if (File.Exists(RetryPath)) File.Delete(RetryPath);
    }
}

public sealed class CloudPublishRetryEntry
{
    public string ProjectName { get; set; } = "";
    public string? BackupPath { get; set; }
    public string ErrorMessage { get; set; } = "";
    public DateTime FailedAtUtc { get; set; }
}
