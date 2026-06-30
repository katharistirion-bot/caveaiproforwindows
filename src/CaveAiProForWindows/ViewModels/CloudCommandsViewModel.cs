using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.ViewModels;

public sealed partial class CloudCommandsViewModel : ObservableObject
{
    private readonly MainViewModel _host;

    public CloudCommandsViewModel(MainViewModel host)
    {
        _host = host;
        RefreshRetryCount();
    }

    [ObservableProperty] private int _retryCount;

    public void RefreshRetryCount()
    {
        RetryCount = CloudPublishRetryStore.LoadAll().Count;
        RetryCommand.NotifyCanExecuteChanged();
    }

    public void RecordFailure(string? projectName, string? backupPath, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return;
        CloudPublishRetryStore.Enqueue(projectName, backupPath, errorMessage);
        RefreshRetryCount();
    }

    public void ClearRetry(string projectName)
    {
        CloudPublishRetryStore.Remove(projectName);
        RefreshRetryCount();
    }

    public IReadOnlyList<CloudPublishHistoryEntry> HistoryForProject(string projectName) =>
        CloudPublishHistoryStore.LoadAll()
            .Where(e => string.Equals(e.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.PublishedAtUtc)
            .ToList();

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync()
    {
        var pending = CloudPublishRetryStore.LoadAll();
        if (pending.Count == 0)
            return;

        var entry = pending[0];
        var project = _host.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, entry.ProjectName, StringComparison.OrdinalIgnoreCase));
        if (project == null)
        {
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current.MainWindow,
                $"Project \"{entry.ProjectName}\" is not open. Open the backup first, then retry.",
                "Cloud publish retry",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        _host.SelectedProject = project;
        await _host.PublishToCloudAsyncInternal().ConfigureAwait(true);
        RefreshRetryCount();
    }

    internal void NotifyPublishStateChanged() => RetryCommand.NotifyCanExecuteChanged();

    private bool CanRetry() =>
        _host.LegalTermsAccepted &&
        !_host.IsCloudPublishing &&
        RetryCount > 0 &&
        _host.SelectedProject != null;
}
