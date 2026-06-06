using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.GenerativeMap;
using CaveAiProForWindows.Services.Secrets;
using CaveAiProForWindows.Services.SketchAssist;

namespace CaveAiProForWindows.ViewModels;

/// <summary>Host callbacks from <see cref="Views.SketchEditorView"/>.</summary>
public sealed class SketchAssistEditorHost
{
    public required Func<CaveProjectDocument?> GetProject { get; init; }

    public required Func<bool> GetLegalTermsAccepted { get; init; }

    public required Func<SketchAssistSession?> BuildSession { get; init; }

    public required Func<byte[]?> CaptureStructureMask { get; init; }

    public required Action<byte[], byte[]> OnGenerativeRenderCompleted { get; init; }

    public Action? RefreshGenerativeOverlay { get; init; }

    public required Func<Window?> GetOwnerWindow { get; init; }

    public Action? OpenLegalSettingsTab { get; init; }
}

/// <summary>Sketch Assist Phase 4 — structure mask → ControlNet generative render.</summary>
public partial class SketchAssistViewModel : ObservableObject
{
    private readonly SketchAssistEditorHost _host;
    private readonly IGenerativeMapRenderer _renderer;
    private CancellationTokenSource? _renderCts;

    public SketchAssistViewModel(SketchAssistEditorHost host, IGenerativeMapRenderer? renderer = null)
    {
        _host = host;
        _renderer = renderer ?? new ReplicateControlNetProvider();
        Prompt = AppUiSettingsStore.LoadOrDefault().GenerativeMap.DefaultPrompt;
        ShowAiRenderOnCanvas = AppUiSettingsStore.LoadOrDefault().GenerativeMap.ShowAiRenderOnCanvas;
        GuidanceScale = AppUiSettingsStore.LoadOrDefault().GenerativeMap.GuidanceScale;
        RefreshApiTokenStatus();
    }

    [ObservableProperty] private bool _isRendering;

    [ObservableProperty] private bool _showProgress;

    [ObservableProperty] private bool _isIndeterminate = true;

    [ObservableProperty] private double _progressValue;

    [ObservableProperty]
    private string _statusMessage =
        "Draw on the design layer, then AI Render uses your structure mask + prompt (Replicate ControlNet).";

    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private string _errorMessage = "";

    [ObservableProperty] private string _prompt;

    [ObservableProperty] private double _guidanceScale = 9;

    [ObservableProperty] private bool _showAiRenderOnCanvas;

    [ObservableProperty] private bool _hasGenerativeRender;

    [ObservableProperty] private string _apiTokenStatus = "Replicate API token: not configured";

    [ObservableProperty] private bool _hasApiToken;

    partial void OnIsRenderingChanged(bool value) => AiRenderCommand.NotifyCanExecuteChanged();

    partial void OnShowAiRenderOnCanvasChanged(bool value)
    {
        PersistGenerativePreferences();
        _host.RefreshGenerativeOverlay?.Invoke();
    }

    partial void OnPromptChanged(string value) => PersistGenerativePreferences();

    partial void OnGuidanceScaleChanged(double value) => PersistGenerativePreferences();

    public void RefreshApiTokenStatus()
    {
        HasApiToken = ReplicateApiTokenStore.IsConfigured();
        ApiTokenStatus = HasApiToken
            ? "Replicate API token: configured (Windows Credential Manager)"
            : "Replicate API token: not configured — add under LEGAL & SETTINGS";
        AiRenderCommand.NotifyCanExecuteChanged();
    }

    public void NotifyProjectChanged(CaveProjectDocument? project)
    {
        HasGenerativeRender = project != null && GenerativeMapSessionCache.TryGet(project) != null;
        AiRenderCommand.NotifyCanExecuteChanged();
        ClearAiRenderCommand.NotifyCanExecuteChanged();
    }

    public void NotifyLegalTermsChanged() => AiRenderCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanAiRender))]
    private async Task AiRenderAsync()
    {
        if (_renderCts != null)
            return;

        HasError = false;
        ErrorMessage = "";

        var project = _host.GetProject();
        if (project == null)
        {
            SetError("Select a survey project from the list.");
            return;
        }

        if (!_host.GetLegalTermsAccepted())
        {
            SetError("Accept the disclaimer on LEGAL & SETTINGS before using AI Render.");
            return;
        }

        if (!ReplicateApiTokenStore.IsConfigured())
        {
            SetError("Configure a Replicate API token under LEGAL & SETTINGS → Generative map (Replicate).");
            return;
        }

        if (string.IsNullOrWhiteSpace(Prompt))
        {
            SetError("Enter a prompt describing the desired map style.");
            return;
        }

        _renderCts = new CancellationTokenSource();
        var ct = _renderCts.Token;

        try
        {
            IsRendering = true;
            ShowProgress = true;
            IsIndeterminate = true;
            ProgressValue = 0;
            StatusMessage = "Exporting structure mask…";

            byte[]? mask = null;
            await Application.Current.Dispatcher.InvokeAsync(() => mask = _host.CaptureStructureMask());
            if (mask is not { Length: > 0 })
            {
                SetError("Could not export a structure mask — add traverse data or sketch strokes first.");
                return;
            }

            IsIndeterminate = false;
            ProgressValue = 15;

            var request = new GenerativeMapRenderRequest
            {
                StructureMaskPng = mask,
                Prompt = Prompt.Trim(),
                GuidanceScale = GuidanceScale,
            };

            var progress = new Progress<string>(OnRenderProgress);
            var result = await _renderer.RenderAsync(request, progress, ct).ConfigureAwait(true);

            GenerativeMapSessionCache.Set(project, result.PngBytes, mask);
            HasGenerativeRender = true;
            ProgressValue = 100;
            StatusMessage = $"{result.ProviderName} complete ({result.PixelWidth}×{result.PixelHeight}).";
            _host.OnGenerativeRenderCompleted(result.PngBytes, mask);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "AI render cancelled.";
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            _renderCts?.Dispose();
            _renderCts = null;
            IsRendering = false;
            IsIndeterminate = false;
            AiRenderCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnHasGenerativeRenderChanged(bool value) => ClearAiRenderCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanClearAiRender))]
    private void ClearAiRender()
    {
        var project = _host.GetProject();
        if (project != null)
            GenerativeMapSessionCache.Clear(project);
        HasGenerativeRender = false;
        StatusMessage = "Cleared AI render from this session.";
        _host.RefreshGenerativeOverlay?.Invoke();
    }

    private bool CanClearAiRender() => HasGenerativeRender;

    [RelayCommand]
    private void CancelAiRender()
    {
        _renderCts?.Cancel();
        StatusMessage = "Cancelling AI render…";
    }

    [RelayCommand]
    private void OpenApiSettings()
    {
        _host.OpenLegalSettingsTab?.Invoke();
        StatusMessage = "Configure your Replicate API token on LEGAL & SETTINGS.";
    }

    private bool CanAiRender() =>
        !IsRendering &&
        _host.GetProject() != null &&
        _host.GetLegalTermsAccepted() &&
        ReplicateApiTokenStore.IsConfigured();

    private void OnRenderProgress(string message)
    {
        StatusMessage = message;
        ProgressValue = message switch
        {
            var m when m.StartsWith("Preparing structure", StringComparison.OrdinalIgnoreCase) => 25,
            var m when m.StartsWith("Submitting Replicate", StringComparison.OrdinalIgnoreCase) => 40,
            var m when m.StartsWith("Waiting for Replicate", StringComparison.OrdinalIgnoreCase) => 55,
            var m when m.StartsWith("Replicate:", StringComparison.OrdinalIgnoreCase) => 70,
            var m when m.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase) => 90,
            var m when m.StartsWith("Generative render complete", StringComparison.OrdinalIgnoreCase) => 100,
            _ => ProgressValue,
        };
    }

    private void SetError(string message)
    {
        HasError = true;
        ErrorMessage = message;
        StatusMessage = "AI render failed.";
    }

    private void PersistGenerativePreferences()
    {
        var all = AppUiSettingsStore.LoadOrDefault();
        all.GenerativeMap.DefaultPrompt = Prompt;
        all.GenerativeMap.ShowAiRenderOnCanvas = ShowAiRenderOnCanvas;
        all.GenerativeMap.GuidanceScale = GuidanceScale;
        AppUiSettingsStore.Save(all);
    }
}
