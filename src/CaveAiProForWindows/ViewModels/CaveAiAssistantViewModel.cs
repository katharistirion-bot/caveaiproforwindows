using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.ViewModels;

public partial class CaveAiAssistantViewModel : ObservableObject
{
    private Func<CaveProjectDocument?>? _projectProvider;
    private Func<IReadOnlyList<KnownCaveRecord>>? _libraryProvider;
    private Func<bool>? _cloudAvailable;

    public ObservableCollection<CaveAiChatMessage> Messages { get; } = new();

    [ObservableProperty] private bool _panelVisible;
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusHint =
        "On-device answers from the selected project. Sign in for cloud AI on the web (Help -> Cave AI on web).";

    public void Attach(Func<CaveProjectDocument?> projectProvider, Func<IReadOnlyList<KnownCaveRecord>> libraryProvider, Func<bool>? cloudAvailable = null)
    {
        _projectProvider = projectProvider;
        _libraryProvider = libraryProvider;
        _cloudAvailable = cloudAvailable;
    }

    partial void OnPanelVisibleChanged(bool value) { if (value && Messages.Count == 0) SeedWelcome(); }
    partial void OnIsBusyChanged(bool value) => SendMessageCommand.NotifyCanExecuteChanged();

    [RelayCommand] private void TogglePanel() => PanelVisible = !PanelVisible;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void SendMessage()
    {
        var query = InputText.Trim();
        if (string.IsNullOrWhiteSpace(query) || IsBusy) return;
        Messages.Add(new CaveAiChatMessage { Text = query, IsUser = true });
        InputText = "";
        IsBusy = true;
        try
        {
            var project = _projectProvider?.Invoke();
            if (project == null)
            {
                Messages.Add(new CaveAiChatMessage { Text = "Select a cave project first.", IsUser = false, ReplySource = CaveAiAssistantReplySource.Device });
                return;
            }
            var library = _libraryProvider?.Invoke() ?? Array.Empty<KnownCaveRecord>();
            Messages.Add(new CaveAiChatMessage
            {
                Text = CaveAiOfflineBrain.Answer(project, query, library),
                IsUser = false,
                ReplySource = CaveAiAssistantReplySource.Device,
            });
            if (CloudPublishWebViewHost.TokenCache.TryGetUsableToken() != null)
                StatusHint = "On-device reply shown. For cloud answers use Help -> Cave AI on web.";
        }
        finally { IsBusy = false; }
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);
    public void NotifyProjectChanged() { if (PanelVisible && Messages.Count <= 1) SeedWelcome(); }

    private void SeedWelcome()
    {
        Messages.Clear();
        var project = _projectProvider?.Invoke();
        var library = _libraryProvider?.Invoke() ?? Array.Empty<KnownCaveRecord>();
        var text = project == null
            ? "Cave AI assistant - select a project, then ask about depth, traverse, loops, or trip report."
            : CaveAiOfflineBrain.Answer(project, null, library);
        Messages.Add(new CaveAiChatMessage { Text = text, IsUser = false, ReplySource = CaveAiAssistantReplySource.Device });
    }
}