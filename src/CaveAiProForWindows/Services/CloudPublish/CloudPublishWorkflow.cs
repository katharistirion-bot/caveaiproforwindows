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
        catch (TimeoutException ex)
        {
            ReportError(request.Progress, ex.Message, showAuthHint: true);
            return null;
        }
        catch (Exception ex)
        {
            ReportError(request.Progress, ex.Message);
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
