using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.Win32;

namespace CaveAiProForWindows.ViewModels;

/// <summary>Offline AI Analytics — local geometry fused with live Android Desktop Sync JSON.</summary>
public partial class AiAnalyticsViewModel : ObservableObject, IDisposable
{
    private readonly AndroidSurveySyncWatcher _syncWatcher = new();
    private readonly Dispatcher _dispatcher;
    private CaveProjectDocument? _project;
    private string _selectedToolTag = "Qc";
    private AndroidSyncSettings _syncSettings = new();
    private bool _isLoaded;

    [ObservableProperty] private bool _isRunning;

    [ObservableProperty] private bool _showProgress;

    [ObservableProperty] private bool _autoSyncEnabled = true;

    [ObservableProperty] private bool _autoRunAnalysisOnSync = true;

    [ObservableProperty] private bool _autoRunAnalysisOnProjectLoad = true;

    [ObservableProperty] private bool _autoReloadBackupZip = true;

    [ObservableProperty] private string _statusMessage =
        "Ready — select an engine and run. Desktop Sync watches caveai_database_v1.json + android_export.json.";

    [ObservableProperty] private string _resultsSummary = "";

    [ObservableProperty] private string _syncFolderDisplay = "No sync folder configured";

    [ObservableProperty] private AndroidSurveyAnalyticsContext? _androidContext;

    public ObservableCollection<AiAnalyticsMetricRow> ResultRows { get; } = new();

    public string ImportStatusDisplay
    {
        get
        {
            if (AndroidContext is not { HasObservations: true } ctx)
                return "No Android notes loaded — choose a sync folder or open a matching backup.";
            return $"{ctx.Observations.Count} observation(s) · {ctx.SourceLabel}";
        }
    }

    public AiAnalyticsViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _syncWatcher.SyncChanged += OnSyncFolderChanged;
        _syncSettings = AppUiSettingsStore.LoadOrDefault().AndroidSync;
        AutoSyncEnabled = _syncSettings.AutoSyncEnabled;
        AutoRunAnalysisOnSync = _syncSettings.AutoRunAnalysisOnSync;
        AutoRunAnalysisOnProjectLoad = _syncSettings.AutoRunAnalysisOnProjectLoad;
        AutoReloadBackupZip = _syncSettings.AutoReloadBackupZip;
    }

    partial void OnAndroidContextChanged(AndroidSurveyAnalyticsContext? value)
    {
        OnPropertyChanged(nameof(ImportStatusDisplay));
        RunAnalysisCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => RunAnalysisCommand.NotifyCanExecuteChanged();

    partial void OnAutoSyncEnabledChanged(bool value)
    {
        _syncSettings.AutoSyncEnabled = value;
        PersistSyncSettings();
        if (_isLoaded)
            RestartSyncWatcher();
    }

    partial void OnAutoRunAnalysisOnSyncChanged(bool value)
    {
        _syncSettings.AutoRunAnalysisOnSync = value;
        PersistSyncSettings();
    }

    partial void OnAutoRunAnalysisOnProjectLoadChanged(bool value)
    {
        _syncSettings.AutoRunAnalysisOnProjectLoad = value;
        PersistSyncSettings();
    }

    partial void OnAutoReloadBackupZipChanged(bool value)
    {
        _syncSettings.AutoReloadBackupZip = value;
        PersistSyncSettings();
    }

    public void OnViewLoaded()
    {
        if (_isLoaded)
            return;
        _isLoaded = true;
        RestartSyncWatcher();
        TryRefreshFromSyncFolder(silent: true);
    }

    public void SetProject(CaveProjectDocument? project)
    {
        _project = project;
        RestartSyncWatcher();

        AndroidSurveyAnalyticsContext? projectCtx = project != null
            ? AndroidSurveyAnalyticsImporter.BuildFromProject(project, "loaded backup")
            : null;

        TryRefreshFromSyncFolder(silent: true);

        if (projectCtx is { HasObservations: true })
        {
            AndroidContext = AndroidContext is { HasObservations: true } existing
                ? AndroidSurveyAnalyticsImporter.MergeContexts([existing, projectCtx], "Desktop Sync + loaded backup")
                : projectCtx;
        }
        else if (AndroidContext == null && projectCtx != null)
            AndroidContext = projectCtx;

        ClearResults("Project changed — run analysis when ready.");
        RunAnalysisCommand.NotifyCanExecuteChanged();

        if (_syncSettings.AutoRunAnalysisOnProjectLoad && _project != null && !IsRunning)
            _ = RunAnalysisAsync();
    }

    public void SetSelectedToolTag(string tag) =>
        _selectedToolTag = string.IsNullOrWhiteSpace(tag) ? "Qc" : tag;

    [RelayCommand(CanExecute = nameof(CanExportResults))]
    private void ExportResultsCsv()
    {
        if (ResultRows.Count == 0)
            return;

        var dlg = new SaveFileDialog
        {
            Title = "Export diagnostic results (CSV)",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"caveai_analytics_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv",
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var bytes = AiAnalyticsDiagnosticExporter.BuildCsvUtf8Bom(ResultRows, ResultsSummary);
            File.WriteAllBytes(dlg.FileName, bytes);
            StatusMessage = $"Exported {ResultRows.Count} row(s) to {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV export failed: {ex.Message}";
        }
    }

    private bool CanExportResults() => ResultRows.Count > 0;

    public void SelectStationFromRow(AiAnalyticsMetricRow? row)
    {
        if (row == null || !row.CanSelectStation)
            return;
        SurveyWorkspaceNavigator.JumpToStation(row.Station.Trim(), "Analytics");
    }

    [RelayCommand]
    private void ImportAndroidProject()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select Android Desktop Sync folder (caveai_database_v1.json + android_export.json)",
        };

        if (Application.Current?.MainWindow is { } owner)
        {
            if (dlg.ShowDialog(owner) != true)
                return;
        }
        else if (dlg.ShowDialog() != true)
        {
            return;
        }

        ApplySyncFolder(dlg.FolderName, reload: true);
    }

    [RelayCommand]
    private void SyncNow() => TryRefreshFromSyncFolder(silent: false);

    [RelayCommand]
    private async Task ImportAndroidExportAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import Android JSON export",
            Filter =
                "Android sync JSON|caveai_database_v1.json;android_export.json;data.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            StatusMessage = "Reading Android export…";
            var fileName = Path.GetFileName(dlg.FileName);
            var imported = AndroidSurveySyncService.TryImportFromSyncFile(dlg.FileName, _project?.Name);

            if (!imported.HasObservations)
            {
                StatusMessage = "Import finished — no survey notes, annotations, or lead hints found.";
                return;
            }

            MergeOrReplaceContext(imported, $"manual import · {fileName}");
            StatusMessage = $"Imported {imported.Observations.Count} observation(s) from {fileName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunAnalysisAsync()
    {
        if (IsRunning || _project == null)
            return;

        IsRunning = true;
        ShowProgress = true;
        StatusMessage = "Running diagnostic pipeline (local geometry + Android context)…";

        var tag = _selectedToolTag;
        var project = _project;
        var ctx = AndroidContext;

        try
        {
            var rows = await Task.Run(() => AiLocalSurveyAnalytics.BuildResultRows(tag, project, ctx).ToList())
                .ConfigureAwait(true);

            ResultRows.Clear();
            foreach (var row in rows)
                ResultRows.Add(row);

            var name = string.IsNullOrWhiteSpace(project.Name) ? "(unnamed project)" : project.Name.Trim();
            var mode = tag switch
            {
                "Volume" => "Volumetric",
                "Lead" => "Lead prediction",
                _ => "QC anomaly",
            };
            var priorityCount = rows.Count(r => r.IsPriorityAlert);
            var importNote = ctx is { HasObservations: true }
                ? $" · {ctx.Observations.Count} Android notes"
                : "";
            var alertNote = priorityCount > 0 ? $" · {priorityCount} priority alert(s)" : "";
            ResultsSummary = $"{name} · {DateTime.Now:yyyy-MM-dd HH:mm} · {mode}{importNote}{alertNote}";
            StatusMessage = "Diagnostic pipeline complete.";
            ExportResultsCsvCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            ShowProgress = false;
            IsRunning = false;
        }
    }

    private bool CanRunAnalysis() => !IsRunning && _project != null;

    private void ApplySyncFolder(string folder, bool reload)
    {
        _syncSettings.SyncFolderPath = folder;
        SyncFolderDisplay = folder;
        PersistSyncSettings();
        RestartSyncWatcher();
        if (reload)
            TryRefreshFromSyncFolder(silent: false);
    }

    private void RestartSyncWatcher()
    {
        if (!_syncSettings.AutoSyncEnabled)
        {
            _syncWatcher.Watch(null);
            return;
        }

        var hint = _project?.LoadedFromFile is { Length: > 0 } lf
            ? Path.GetDirectoryName(lf)
            : null;
        var folder = AndroidSurveySyncService.ResolveSyncFolder(_syncSettings.SyncFolderPath, hint);
        SyncFolderDisplay = folder ?? _syncSettings.SyncFolderPath ?? "No sync folder found";
        _syncWatcher.Watch(folder, _project?.Name);
    }

    private void TryRefreshFromSyncFolder(bool silent)
    {
        var hint = _project?.LoadedFromFile is { Length: > 0 } lf
            ? Path.GetDirectoryName(lf)
            : null;
        var folder = AndroidSurveySyncService.ResolveSyncFolder(_syncSettings.SyncFolderPath, hint);
        if (folder == null)
        {
            if (!silent)
                StatusMessage = "Sync folder not found — use Import Android Project to choose a folder.";
            return;
        }

        var bundle = AndroidSurveySyncService.TryLoadBundle(folder, _project?.Name);
        if (bundle == null || !bundle.Context.HasObservations)
        {
            if (!silent)
                StatusMessage =
                    $"No Android observations in {folder} ({AndroidSurveySyncService.DatabaseFileName} / {AndroidSurveySyncService.ExportFileName}).";
            return;
        }

        _syncSettings.SyncFolderPath = folder;
        SyncFolderDisplay = folder;
        PersistSyncSettings();
        MergeOrReplaceContext(bundle.Context, bundle.Context.SourceLabel ?? folder);

        var files = new List<string>();
        if (bundle.DatabaseFilePath != null)
            files.Add(Path.GetFileName(bundle.DatabaseFilePath));
        if (bundle.ExportFilePath != null)
            files.Add(Path.GetFileName(bundle.ExportFilePath));

        StatusMessage = silent
            ? $"Synced {bundle.Context.Observations.Count} observation(s) from {string.Join(" + ", files)}."
            : $"Reloaded {bundle.Context.Observations.Count} observation(s) from {string.Join(" + ", files)}.";

        if (_syncSettings.AutoRunAnalysisOnSync && _project != null && !IsRunning)
            _ = RunAnalysisAsync();
    }

    private void MergeOrReplaceContext(AndroidSurveyAnalyticsContext incoming, string label)
    {
        if (AndroidContext is { HasObservations: true } existing &&
            !string.Equals(existing.SourceLabel, incoming.SourceLabel, StringComparison.OrdinalIgnoreCase))
        {
            AndroidContext = AndroidSurveyAnalyticsImporter.MergeContexts([existing, incoming], label);
        }
        else
        {
            AndroidContext = new AndroidSurveyAnalyticsContext
            {
                SourceLabel = label,
                ProjectName = incoming.ProjectName,
                Observations = incoming.Observations,
                ByStation = incoming.ByStation,
            };
        }

        OnPropertyChanged(nameof(ImportStatusDisplay));
    }

    private void OnSyncFolderChanged(object? sender, AndroidSurveySyncBundle bundle)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!_syncSettings.AutoSyncEnabled)
                return;

            MergeOrReplaceContext(bundle.Context, bundle.Context.SourceLabel ?? bundle.SyncFolder);
            StatusMessage =
                $"Auto-sync: {bundle.Context.Observations.Count} observation(s) updated from {bundle.SyncFolder}.";

            AndroidDesktopSyncHub.NotifySyncFilesChanged(bundle.SyncFolder, AndroidSurveySyncService.DatabaseFileName);

            if (_syncSettings.AutoRunAnalysisOnSync && _project != null && !IsRunning)
                _ = RunAnalysisAsync();
        });
    }

    private void PersistSyncSettings()
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        settings.AndroidSync = new AndroidSyncSettings
        {
            SyncFolderPath = _syncSettings.SyncFolderPath,
            AutoSyncEnabled = AutoSyncEnabled,
            AutoRunAnalysisOnSync = AutoRunAnalysisOnSync,
            AutoRunAnalysisOnProjectLoad = AutoRunAnalysisOnProjectLoad,
            AutoReloadBackupZip = AutoReloadBackupZip,
        };
        AppUiSettingsStore.Save(settings);
        AndroidDesktopSyncHub.NotifySyncSettingsChanged();
    }

    private void ClearResults(string statusMessage)
    {
        ResultRows.Clear();
        ResultsSummary = "";
        StatusMessage = statusMessage;
        ExportResultsCsvCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _syncWatcher.SyncChanged -= OnSyncFolderChanged;
        _syncWatcher.Dispose();
    }
}
