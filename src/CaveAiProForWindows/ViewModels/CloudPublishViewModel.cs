using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.PublishReadiness;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.ViewModels;

/// <summary>Captured on the UI thread immediately before upload.</summary>
public sealed class CloudPublishArtifactCapture
{
    public byte[]? AiMapPng { get; init; }

    public byte[]? StructureMaskPng { get; init; }

    public required byte[] SurveyJsonUtf8 { get; init; }

    /// <summary>Photos to upload during publish (from backup ZIP).</summary>
    public IReadOnlyList<CloudPublishPhotoCollector.GalleryPhoto>? PendingGalleryPhotos { get; init; }

    /// <summary>Uploaded gallery photo HTTPS URLs (Android Public Library parity).</summary>
    public IReadOnlyList<string>? GalleryPhotoUrls { get; init; }
}

/// <summary>Host callbacks supplied by <see cref="SketchEditorView"/>.</summary>
public sealed class CloudPublishEditorHost
{
    public required Func<CaveProjectDocument?> GetProject { get; init; }

    public required Func<bool> GetLegalTermsAccepted { get; init; }

    public required Func<CloudPublishArtifactCapture?> CaptureArtifacts { get; init; }

    public required Func<Window?> GetOwnerWindow { get; init; }

    public Action<string>? PersistLinkedLibraryCaveId { get; init; }
}

/// <summary>Push to Cloud UI state for the Sketch Editor sidebar.</summary>
public partial class CloudPublishViewModel : ObservableObject
{
    private readonly CloudPublishEditorHost _host;
    private readonly CloudPublishService _publishService;
    private CancellationTokenSource? _publishCts;

    public CloudPublishViewModel(CloudPublishEditorHost host, CloudPublishService? publishService = null)
    {
        _host = host;
        _publishService = publishService
            ?? new CloudPublishService(CloudPublishWebViewHost.TokenCache);
    }

    [ObservableProperty] private bool _isPublishing;

    [ObservableProperty] private bool _showProgress;

    [ObservableProperty] private bool _isIndeterminate = true;

    [ObservableProperty] private double _progressValue;

    [ObservableProperty] private string _statusMessage =
        "Publish survey JSON and AI map renders to your linked Public Cave Library entry.";

    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private string _errorMessage = "";

    [ObservableProperty] private bool _showOpenLibraryHint;

    [ObservableProperty] private string _linkedCaveIdDisplay = "Library link: not set";

    [ObservableProperty] private string _readinessSummary = "";

    public void RefreshLinkedCaveDisplay(CaveProjectDocument? project)
    {
        var id = project != null ? LinkedLibraryCaveIdResolver.TryGet(project) : null;
        LinkedCaveIdDisplay = string.IsNullOrEmpty(id)
            ? "Library link: not set (required before publish)"
            : $"Library link: {id}";
        RefreshReadinessSummary(project);
    }

    public void RefreshReadinessSummary(CaveProjectDocument? project)
    {
        if (project == null)
        {
            ReadinessSummary = "";
            return;
        }

        var result = PublishReadinessEvaluator.Evaluate(new PublishReadinessContext
        {
            Mode = PublishReadinessMode.WindowsCloud,
            Project = project,
            LegalTermsAccepted = _host.GetLegalTermsAccepted(),
            LinkedLibraryCaveId = LinkedLibraryCaveIdResolver.TryGet(project),
            TopologyQcCriticalCount = PublishReadinessEvaluator.CountCriticalTopologyIssues(project),
        });
        ReadinessSummary = PublishReadinessEvaluator.FormatSummaryLine(result);
    }

    partial void OnIsPublishingChanged(bool value) => PushToCloudCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanPushToCloud))]
    private async Task PushToCloudAsync()
    {
        if (_publishCts != null)
            return;

        HasError = false;
        ErrorMessage = "";
        ShowOpenLibraryHint = false;

        _publishCts = new CancellationTokenSource();
        var ct = _publishCts.Token;

        try
        {
            IsPublishing = true;
            ShowProgress = true;
            IsIndeterminate = true;
            ProgressValue = 0;

            var progress = new Progress<CloudPublishProgressUpdate>(ApplyProgress);
            var metadata = await CloudPublishWorkflow.RunAsync(new CloudPublishWorkflow.Request
            {
                GetProject = _host.GetProject,
                GetLegalTermsAccepted = _host.GetLegalTermsAccepted,
                CaptureArtifacts = _host.CaptureArtifacts,
                GetOwnerWindow = _host.GetOwnerWindow,
                PersistLinkedLibraryCaveId = _host.PersistLinkedLibraryCaveId,
                PublishService = _publishService,
                Progress = progress,
                CancellationToken = ct,
            }).ConfigureAwait(true);

            if (metadata != null)
                ProgressValue = 100;
        }
        finally
        {
            _publishCts?.Dispose();
            _publishCts = null;
            IsPublishing = false;
            IsIndeterminate = false;
            PushToCloudCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void CancelPublish()
    {
        _publishCts?.Cancel();
        StatusMessage = "Cancelling…";
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        try
        {
            var owner = _host.GetOwnerWindow();
            await DesktopAuthWindow.AcquireTokenAsync(owner, CloudPublishWebViewHost.TokenCache).ConfigureAwait(true);
            if (CloudPublishWebViewHost.TokenCache.TryGetUsableToken() != null)
            {
                HasError = false;
                ErrorMessage = "";
                StatusMessage = "Signed in — ready to publish.";
            }
            else
                StatusMessage = "Sign-in window closed without a token.";
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task SetLinkedCaveIdAsync()
    {
        var project = _host.GetProject();
        if (project == null)
        {
            SetError("Select a project first.");
            return;
        }

        var existing = LinkedLibraryCaveIdResolver.TryGet(project);
        var owner = _host.GetOwnerWindow();
        if (LinkedLibraryCaveIdPromptWindow.TryPrompt(owner, existing, out var entered) && !string.IsNullOrWhiteSpace(entered))
        {
            LinkedLibraryCaveIdResolver.Set(project, entered);
            _host.PersistLinkedLibraryCaveId?.Invoke(entered);
            RefreshLinkedCaveDisplay(project);
            HasError = false;
            ErrorMessage = "";
            StatusMessage = $"Linked to Cave Library document {entered.Trim()}.";
        }

        await Task.CompletedTask;
    }

    private bool CanPushToCloud() =>
        !IsPublishing && _host.GetProject() != null && _host.GetLegalTermsAccepted();

    public void NotifyProjectChanged(CaveProjectDocument? project)
    {
        RefreshLinkedCaveDisplay(project);
        PushToCloudCommand.NotifyCanExecuteChanged();
    }

    public void NotifyLegalTermsChanged() => PushToCloudCommand.NotifyCanExecuteChanged();

    private void ApplyProgress(CloudPublishProgressUpdate update)
    {
        StatusMessage = update.Message;
        ShowOpenLibraryHint = update.ShowAuthHint;
        IsIndeterminate = update.IsIndeterminate;
        if (update.ProgressPercent is double p && !double.IsNaN(p))
            ProgressValue = p;

        if (update.HasError)
        {
            HasError = true;
            ErrorMessage = update.ErrorMessage ?? UserFacingErrors.CloudPublishFailed();
        }
    }

    private void SetError(string message)
    {
        HasError = true;
        ErrorMessage = UserFacingErrors.CloudPublishFailed(message);
        StatusMessage = "Publish failed.";
    }
}
