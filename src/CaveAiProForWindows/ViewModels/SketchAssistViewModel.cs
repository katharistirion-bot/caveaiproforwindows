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

    public Action? OpenApiSettings { get; init; }

    /// <summary>After a successful render, show mask vs result comparison (optional).</summary>
    public Action<byte[], byte[]>? ShowRenderCompare { get; init; }
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
        _selectedPromptPresetId = AppUiSettingsStore.LoadOrDefault().GenerativeMap.SelectedPromptPresetId;
        ApplyPromptPreset(_selectedPromptPresetId, overwritePrompt: false);
        RefreshApiTokenStatus();
    }

    [ObservableProperty] private bool _isRendering;

    [ObservableProperty] private bool _showProgress;

    [ObservableProperty] private bool _isIndeterminate = true;

    [ObservableProperty] private double _progressValue;

    [ObservableProperty]
    private string _statusMessage =
        "Draw on the design layer, then AI Render uses your structure mask + prompt via CaveAI Cloud AI.";

    public bool ShowDevApiSettings => GenerativeAiAccessGate.UseDirectByok;

    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private string _errorMessage = "";

    [ObservableProperty] private string _prompt;

    [ObservableProperty] private double _guidanceScale = 9;

    [ObservableProperty] private bool _showAiRenderOnCanvas;

    [ObservableProperty] private bool _hasGenerativeRender;

    [ObservableProperty] private string _apiTokenStatus = GenerativeAiAccessGate.StatusText();

    [ObservableProperty] private bool _hasApiToken;

    public IReadOnlyList<GenerativeMapPromptPreset> PromptPresets { get; } = GenerativeMapPromptPresets.All;

    [ObservableProperty] private string _selectedPromptPresetId = "photoreal";

    [ObservableProperty] private string _negativePrompt =
        GenerativeMapPromptPresets.TryGet("photoreal")?.NegativePrompt ?? "";

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
        HasApiToken = GenerativeAiAccessGate.IsConfigured();
        ApiTokenStatus = GenerativeAiAccessGate.StatusText();
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

        if (!GenerativeAiAccessGate.EnsureConfigured(_host.GetOwnerWindow(), out var gateError))
        {
            SetError(gateError ?? "Cloud AI access required.");
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
                NegativePrompt = NegativePrompt.Trim(),
                GuidanceScale = GuidanceScale,
            };

            var progress = new Progress<string>(OnRenderProgress);
            var result = await _renderer.RenderAsync(request, progress, ct).ConfigureAwait(true);

            GenerativeMapSessionCache.Set(project, result.PngBytes, mask);
            HasGenerativeRender = true;
            ProgressValue = 100;
            StatusMessage = $"{result.ProviderName} complete ({result.PixelWidth}×{result.PixelHeight}).";
            _host.OnGenerativeRenderCompleted(result.PngBytes, mask);
            _host.ShowRenderCompare?.Invoke(mask, result.PngBytes);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "AI render cancelled.";
        }
        catch (Exception ex)
        {
            if (GenerativeAiAccessGate.IsAuthError(ex))
            {
                if (GenerativeAiAccessGate.UseDirectByok)
                {
                    ReplicateApiTokenStore.TryClear();
                    RefreshApiTokenStatus();
                    ReplicateTokenGate.PromptInvalidToken(_host.GetOwnerWindow());
                    SetError("Replicate rejected the API token. Enter a valid token in Developer API Settings.");
                }
                else
                    SetError(ex.Message);
            }
            else
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

    partial void OnSelectedPromptPresetIdChanged(string value) => ApplyPromptPreset(value, overwritePrompt: true);

    private void ApplyPromptPreset(string? presetId, bool overwritePrompt)
    {
        var preset = GenerativeMapPromptPresets.TryGet(presetId);
        if (preset == null)
            return;
        if (overwritePrompt)
            Prompt = preset.Prompt;
        NegativePrompt = preset.NegativePrompt;
        if (overwritePrompt)
            GuidanceScale = preset.GuidanceScale;
        PersistGenerativePreferences();
    }

    [RelayCommand]
    private void OpenApiSettings()
    {
        _host.OpenApiSettings?.Invoke();
        RefreshApiTokenStatus();
    }

    private bool CanAiRender() =>
        !IsRendering &&
        _host.GetProject() != null &&
        _host.GetLegalTermsAccepted() &&
        GenerativeAiAccessGate.IsConfigured();

    private void OnRenderProgress(string message)
    {
        StatusMessage = message;
        ProgressValue = message switch
        {
            var m when m.StartsWith("Preparing structure", StringComparison.OrdinalIgnoreCase) => 25,
            var m when m.StartsWith("Submitting cloud AI", StringComparison.OrdinalIgnoreCase) => 40,
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
        all.GenerativeMap.SelectedPromptPresetId = SelectedPromptPresetId;
        AppUiSettingsStore.Save(all);
    }
}
