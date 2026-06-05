using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.ViewModels;

/// <summary>Captured on the UI thread immediately before upload.</summary>
public sealed class CloudPublishArtifactCapture
{
    public byte[]? AiMapPng { get; init; }

    public byte[]? StructureMaskPng { get; init; }

    public required byte[] SurveyJsonUtf8 { get; init; }
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
    private static readonly TimeSpan TokenWaitTimeout = TimeSpan.FromSeconds(90);

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
        "Sign in via Help → Public Cave Library, then publish your sketch and survey JSON.";

    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private string _errorMessage = "";

    [ObservableProperty] private bool _showOpenLibraryHint;

    [ObservableProperty] private string _linkedCaveIdDisplay = "Library link: not set";

    public void RefreshLinkedCaveDisplay(CaveProjectDocument? project)
    {
        var id = project != null ? LinkedLibraryCaveIdResolver.TryGet(project) : null;
        LinkedCaveIdDisplay = string.IsNullOrEmpty(id)
            ? "Library link: not set (required before publish)"
            : $"Library link: {id}";
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

        var project = _host.GetProject();
        if (project == null)
        {
            SetError("Select a survey project from the list before publishing.");
            return;
        }

        if (!_host.GetLegalTermsAccepted())
        {
            SetError("Accept the disclaimer on LEGAL & SETTINGS before using Push to Cloud.");
            return;
        }

        var docId = await ResolvePublishedCaveDocIdAsync(project).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(docId))
        {
            SetError("Publish cancelled — a Cave Library document id is required.");
            return;
        }

        _publishCts = new CancellationTokenSource();
        var ct = _publishCts.Token;

        try
        {
            IsPublishing = true;
            ShowProgress = true;
            IsIndeterminate = true;
            ProgressValue = 0;
            StatusMessage = "Waiting for Firebase sign-in token…";

            if (CloudPublishWebViewHost.TokenCache.TryGetUsableToken() == null)
            {
                ShowOpenLibraryHint = true;
                StatusMessage =
                    "No Firebase token yet. Open Help → Public Cave Library, sign in with Google, browse the map briefly, then retry — or wait here while the token is captured automatically.";
            }

            var token = await _publishService.WaitForTokenAsync(TokenWaitTimeout, ct).ConfigureAwait(true);
            ShowOpenLibraryHint = false;
            IsIndeterminate = false;
            ProgressValue = 10;
            StatusMessage = "Token captured — preparing artifacts…";

            CloudPublishArtifactCapture? capture = null;
            await Application.Current.Dispatcher.InvokeAsync(() => capture = _host.CaptureArtifacts());
            if (capture == null)
            {
                SetError("Could not capture publish artifacts from the sketch editor (no drawable plan data).");
                return;
            }

            var bundle = new CloudPublishArtifactBundle
            {
                Project = project,
                PublishedCaveDocId = docId,
                AiMapPng = capture.AiMapPng,
                StructureMaskPng = capture.StructureMaskPng,
                SurveyJsonUtf8 = capture.SurveyJsonUtf8,
            };

            var progress = new Progress<string>(OnPublishProgress);
            await _publishService.PublishAsync(bundle, token, progress, ct).ConfigureAwait(true);

            ProgressValue = 100;
            StatusMessage = $"Publish complete — updated published_caves/{docId}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Publish cancelled.";
        }
        catch (TimeoutException ex)
        {
            ShowOpenLibraryHint = true;
            SetError(ex.Message);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
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
    private void OpenPublicLibrary()
    {
        try
        {
            var owner = _host.GetOwnerWindow();
            PublicLibraryCatalog.ShowMapInAppWindow(owner);
            ShowOpenLibraryHint = true;
            if (!IsPublishing)
                StatusMessage =
                    "Public Cave Library opened — sign in with Google and browse the map so a Firebase token can be captured.";
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

    private void OnPublishProgress(string message)
    {
        StatusMessage = message;
        ProgressValue = message switch
        {
            var m when m.StartsWith("Uploading AI map", StringComparison.OrdinalIgnoreCase) => 30,
            var m when m.StartsWith("Uploading structure mask", StringComparison.OrdinalIgnoreCase) => 50,
            var m when m.StartsWith("Uploading enriched survey", StringComparison.OrdinalIgnoreCase) => 70,
            var m when m.StartsWith("Updating Firestore", StringComparison.OrdinalIgnoreCase) => 90,
            var m when m.StartsWith("Publish complete", StringComparison.OrdinalIgnoreCase) => 100,
            _ => ProgressValue,
        };
    }

    private void SetError(string message)
    {
        HasError = true;
        ErrorMessage = message;
        StatusMessage = "Publish failed.";
    }

    private async Task<string?> ResolvePublishedCaveDocIdAsync(CaveProjectDocument project)
    {
        var existing = LinkedLibraryCaveIdResolver.TryGet(project);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        string? entered = null;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var owner = _host.GetOwnerWindow();
            if (LinkedLibraryCaveIdPromptWindow.TryPrompt(owner, null, out var id))
                entered = id;
        });

        if (string.IsNullOrWhiteSpace(entered))
            return null;

        LinkedLibraryCaveIdResolver.Set(project, entered);
        _host.PersistLinkedLibraryCaveId?.Invoke(entered);
        RefreshLinkedCaveDisplay(project);
        return entered.Trim();
    }
}
