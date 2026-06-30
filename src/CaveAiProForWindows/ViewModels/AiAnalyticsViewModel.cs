using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Views;
using Microsoft.Win32;

namespace CaveAiProForWindows.ViewModels;

/// <summary>Offline survey intelligence dashboard — health, insights, backup diff, batch QC.</summary>
public partial class AiAnalyticsViewModel : ObservableObject, IDisposable
{
    private readonly AndroidSurveySyncWatcher _syncWatcher = new();
    private readonly Dispatcher _dispatcher;
    private CaveProjectDocument? _project;
    private AndroidSyncSettings _syncSettings = new();
    private bool _isLoaded;

    [ObservableProperty] private bool _isRunning;

    [ObservableProperty] private bool _showProgress;

    [ObservableProperty] private bool _autoSyncEnabled = true;

    [ObservableProperty] private bool _autoRunAnalysisOnSync = true;

    [ObservableProperty] private bool _autoRunAnalysisOnProjectLoad = true;

    [ObservableProperty] private bool _autoReloadBackupZip = true;

    [ObservableProperty] private string _statusMessage =
        "Ready — open a project or sync Android folder. Dashboard refreshes automatically when backup JSON or ZIP updates arrive.";

    [ObservableProperty] private string _resultsSummary = "";

    [ObservableProperty] private string _syncFolderDisplay = "No sync folder configured";

    [ObservableProperty] private string _latestBackupSummary = "No backup ZIP detected in sync folder.";

    [ObservableProperty] private string _syncHubInventoryLine = "";

    [ObservableProperty] private AndroidSurveyAnalyticsContext? _androidContext;

    [ObservableProperty] private SurveyHealthSnapshot _health = new();

    [ObservableProperty] private string _backupDiffSummary = "Configure Desktop Sync to compare with previous backup.";

    [ObservableProperty] private string _backupDiffDetail = "";

    [ObservableProperty] private bool _sketchEditorReady;

    public ObservableCollection<AiAnalyticsMetricRow> ResultRows { get; } = new();

    public ObservableCollection<SurveyIntelligenceInsight> InsightRows { get; } = new();

    public ObservableCollection<SurveyBatchQcRow> BatchQcRows { get; } = new();

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
        AndroidDesktopSyncHub.SyncSettingsChanged += OnExternalSyncSettingsChanged;
        _syncSettings = AppUiSettingsStore.LoadOrDefault().AndroidSync;
        AutoSyncEnabled = _syncSettings.AutoSyncEnabled;
        AutoRunAnalysisOnSync = _syncSettings.AutoRunAnalysisOnSync;
        AutoRunAnalysisOnProjectLoad = _syncSettings.AutoRunAnalysisOnProjectLoad;
        AutoReloadBackupZip = _syncSettings.AutoReloadBackupZip;
    }

    partial void OnAndroidContextChanged(AndroidSurveyAnalyticsContext? value)
    {
        OnPropertyChanged(nameof(ImportStatusDisplay));
        RefreshDashboardCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => RefreshDashboardCommand.NotifyCanExecuteChanged();

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
        UpdateLatestBackupSummary();
        TryRefreshFromSyncFolder(silent: true);
    }

    public void SetProject(CaveProjectDocument? project)
    {
        _project = project;
        RestartSyncWatcher();
        UpdateLatestBackupSummary();

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

        ClearResults("Project changed — refreshing dashboard…");
        UpdateSyncHubInventoryLine();
        RefreshDashboardCommand.NotifyCanExecuteChanged();

        if (_syncSettings.AutoRunAnalysisOnProjectLoad && _project != null && !IsRunning)
            _ = RefreshDashboardAsync();
    }

    [RelayCommand(CanExecute = nameof(CanOpenLoopClosureAssistant))]
    private void OpenLoopClosureAssistant()
    {
        if (_project == null)
            return;
        if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
            vm.ShowLoopClosureAssistantCommand.Execute(null);
    }

    private bool CanOpenLoopClosureAssistant() => _project != null && Health.LoopCount > 0;

    [RelayCommand(CanExecute = nameof(CanExportResults))]
    private void ExportResultsCsv()
    {
        if (ResultRows.Count == 0)
            return;

        var dlg = new SaveFileDialog
        {
            Title = "Export findings (CSV)",
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

    [RelayCommand(CanExecute = nameof(CanExportResults))]
    private void ExportExpeditionReport()
    {
        if (_project == null)
            return;

        var dlg = new SaveFileDialog
        {
            Title = "Export expedition intelligence report (Markdown)",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            FileName = $"caveai_expedition_{SanitizeFileName(_project.Name)}_{DateTime.Now:yyyyMMdd}.md",
            AddExtension = true,
            DefaultExt = ".md",
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var diff = SurveyIntelligenceEngine.CompareWithPreviousBackup(_project, ResolveSyncFolder(), _project.LoadedFromFile);
            var bytes = AiAnalyticsExpeditionReportExporter.BuildMarkdownUtf8(
                Health,
                InsightRows.ToList(),
                diff,
                BatchQcRows.ToList(),
                ResultRows.ToList(),
                _project.Name);
            File.WriteAllBytes(dlg.FileName, bytes);
            StatusMessage = $"Expedition report saved to {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Report export failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenSketchEditor))]
    private void OpenSketchEditor()
    {
        SurveyWorkspaceNavigator.OpenSketchEditorForDesign(runProceduralAssist: false);
        StatusMessage = "Opened Sketch Editor — QC passed for cartography workflow.";
    }

    private bool CanOpenSketchEditor() => SketchEditorReady && _project != null;

    [RelayCommand(CanExecute = nameof(CanCompareWithPreviousBackup))]
    private void CompareWithPreviousBackup()
    {
        if (_project == null)
            return;
        var syncFolder = ResolveSyncFolder();
        if (string.IsNullOrWhiteSpace(syncFolder) || !Directory.Exists(syncFolder))
        {
            StatusMessage = "Configure Desktop Sync folder first.";
            return;
        }

        var zips = Directory.EnumerateFiles(syncFolder, "CaveAI_Backup_*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        if (zips.Count < 2)
        {
            StatusMessage = "Need at least two CaveAI_Backup_*.zip files in the sync folder.";
            return;
        }

        var latestPath = !string.IsNullOrWhiteSpace(_project.LoadedFromFile) && File.Exists(_project.LoadedFromFile)
            ? _project.LoadedFromFile
            : zips[0];
        var previousPath = zips.FirstOrDefault(z => !string.Equals(z, latestPath, StringComparison.OrdinalIgnoreCase)) ?? zips[1];
        var owner = Application.Current.MainWindow;
        SurveyCompareWindow.ShowWithPaths(owner, latestPath, previousPath, _project.Name);
        StatusMessage = $"Compare: {Path.GetFileName(latestPath)} vs {Path.GetFileName(previousPath)}";
    }

    private bool CanCompareWithPreviousBackup() => _project != null;

    private bool CanExportResults() => ResultRows.Count > 0;

    public void SelectStationFromRow(AiAnalyticsMetricRow? row)
    {
        if (row == null)
            return;
        if (!string.IsNullOrWhiteSpace(row.Station) && row.Station != "—")
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
            if (_project != null && !IsRunning)
                await RefreshDashboardAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshDashboard))]
    private async Task RefreshDashboardAsync()
    {
        if (IsRunning)
            return;

        IsRunning = true;
        ShowProgress = true;
        StatusMessage = "Running offline survey intelligence…";

        var project = _project;
        var ctx = AndroidContext;
        var syncFolder = ResolveSyncFolder();

        try
        {
            var dashboard = await Task.Run(() =>
            {
                var health = SurveyIntelligenceEngine.BuildHealth(project);
                var insights = SurveyIntelligenceEngine.BuildInsights(project, ctx);
                var diff = SurveyIntelligenceEngine.CompareWithPreviousBackup(
                    project,
                    syncFolder,
                    project?.LoadedFromFile);
                var batch = SurveyIntelligenceEngine.ScanSyncFolderBatchQc(syncFolder);
                var findings = SurveyIntelligenceEngine.BuildCombinedFindings(project, ctx);
                return (health, insights, diff, batch, findings);
            }).ConfigureAwait(true);

            Health = dashboard.health;
            SketchEditorReady = dashboard.health.QcPassReadyForSketch;
            OpenLoopClosureAssistantCommand.NotifyCanExecuteChanged();

            InsightRows.Clear();
            foreach (var row in dashboard.insights)
                InsightRows.Add(row);

            BatchQcRows.Clear();
            foreach (var row in dashboard.batch)
                BatchQcRows.Add(row);

            BackupDiffSummary = dashboard.diff.Summary;
            BackupDiffDetail = BuildBackupDiffDetail(dashboard.diff);

            ResultRows.Clear();
            foreach (var row in dashboard.findings)
                ResultRows.Add(row);

            if (project != null)
            {
                var name = string.IsNullOrWhiteSpace(project.Name) ? "(unnamed project)" : project.Name.Trim();
                UpdateSyncHubInventoryLine();
                var priorityCount = dashboard.findings.Count(r => r.IsPriorityAlert);
                var importNote = ctx is { HasObservations: true }
                    ? $" · {ctx.Observations.Count} Android notes"
                    : "";
                var alertNote = priorityCount > 0 ? $" · {priorityCount} priority alert(s)" : "";
                ResultsSummary = $"{name} · {DateTime.Now:yyyy-MM-dd HH:mm}{importNote}{alertNote}";
            }
            else
            {
                ResultsSummary = "";
            }

            StatusMessage = project == null
                ? "Load a project to populate the dashboard."
                : "Survey intelligence dashboard updated.";
            ExportResultsCsvCommand.NotifyCanExecuteChanged();
            ExportExpeditionReportCommand.NotifyCanExecuteChanged();
            OpenSketchEditorCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            ShowProgress = false;
            IsRunning = false;
        }
    }

    private bool CanRefreshDashboard() => !IsRunning;

    private void ApplySyncFolder(string folder, bool reload)
    {
        _syncSettings.SyncFolderPath = folder;
        SyncFolderDisplay = folder;
        PersistSyncSettings();
        RestartSyncWatcher();
        UpdateLatestBackupSummary();
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

        var folder = ResolveSyncFolder();
        SyncFolderDisplay = folder ?? _syncSettings.SyncFolderPath ?? "No sync folder found";
        _syncWatcher.Watch(folder, _project?.Name);
    }

    private string? ResolveSyncFolder()
    {
        var hint = _project?.LoadedFromFile is { Length: > 0 } lf
            ? Path.GetDirectoryName(lf)
            : null;
        return AndroidSurveySyncService.ResolveSyncFolder(_syncSettings.SyncFolderPath, hint);
    }

    private void UpdateLatestBackupSummary()
    {
        var folder = ResolveSyncFolder();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            LatestBackupSummary = "No sync folder — set Desktop Sync path.";
            return;
        }

        var latest = Directory.EnumerateFiles(folder, "CaveAI_Backup_*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latest == null)
        {
            LatestBackupSummary = "No CaveAI_Backup_*.zip in sync folder yet.";
            return;
        }

        var when = File.GetLastWriteTime(latest);
        LatestBackupSummary = $"Latest backup: {Path.GetFileName(latest)} ({when:yyyy-MM-dd HH:mm})";
    }

    private void TryRefreshFromSyncFolder(bool silent)
    {
        var folder = ResolveSyncFolder();
        if (folder == null)
        {
            if (!silent)
                StatusMessage = "Sync folder not found — use Import Android Project to choose a folder.";
            return;
        }

        UpdateLatestBackupSummary();

        var bundle = AndroidSurveySyncService.TryLoadBundle(folder, _project?.Name);
        if (bundle == null || !bundle.Context.HasObservations)
        {
            if (!silent)
                StatusMessage =
                    $"No Android observations in {folder} ({AndroidSurveySyncService.DatabaseFileName} / {AndroidSurveySyncService.ExportFileName}).";
            if (_project != null && _syncSettings.AutoRunAnalysisOnSync && !IsRunning)
                _ = RefreshDashboardAsync();
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

        if (_syncSettings.AutoRunAnalysisOnSync && !IsRunning)
            _ = RefreshDashboardAsync();
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
            UpdateLatestBackupSummary();
            StatusMessage =
                $"Auto-sync: {bundle.Context.Observations.Count} observation(s) updated from {bundle.SyncFolder}.";

            AndroidDesktopSyncHub.NotifySyncFilesChanged(bundle.SyncFolder, AndroidSurveySyncService.DatabaseFileName);

            if (_syncSettings.AutoRunAnalysisOnSync && !IsRunning)
                _ = RefreshDashboardAsync();
        });
    }

    private void OnExternalSyncSettingsChanged(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _syncSettings = AppUiSettingsStore.LoadOrDefault().AndroidSync;
            AutoSyncEnabled = _syncSettings.AutoSyncEnabled;
            AutoRunAnalysisOnSync = _syncSettings.AutoRunAnalysisOnSync;
            AutoRunAnalysisOnProjectLoad = _syncSettings.AutoRunAnalysisOnProjectLoad;
            AutoReloadBackupZip = _syncSettings.AutoReloadBackupZip;
            RestartSyncWatcher();
            UpdateLatestBackupSummary();
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

    private void UpdateSyncHubInventoryLine()
    {
        if (_project == null)
        {
            SyncHubInventoryLine = "";
            return;
        }

        var pendingGeo = CaveAiOfflineBrain.CountPendingGeoSamples(_project);
        var mapRows = Application.Current.MainWindow?.DataContext is MainViewModel vm
            ? vm.MapInventoryRows.Count
            : 0;
        var mapNote = mapRows > 0 ? $"{mapRows} map inventory row(s)" : "no map_inventory.json";
        SyncHubInventoryLine =
            $"Android sync hub: {mapNote} · {pendingGeo} geo/bio pending · re-import same cave name shows conflict banner on main window.";
    }

    private static string BuildBackupDiffDetail(SurveyBackupDiff diff)
    {
        if (!diff.HasComparison)
            return "";

        var parts = new List<string>();
        if (diff.AddedStationNames.Count > 0)
            parts.Add("New stations: " + string.Join(", ", diff.AddedStationNames.Take(20)));
        if (diff.AddedLegLabels.Count > 0)
            parts.Add("New legs: " + string.Join(", ", diff.AddedLegLabels.Take(20)));
        return string.Join("\n", parts);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "survey" : name.Trim();
    }

    private void ClearResults(string statusMessage)
    {
        ResultRows.Clear();
        InsightRows.Clear();
        BatchQcRows.Clear();
        ResultsSummary = "";
        BackupDiffSummary = "Configure Desktop Sync to compare with previous backup.";
        BackupDiffDetail = "";
        Health = new SurveyHealthSnapshot();
        SketchEditorReady = false;
        StatusMessage = statusMessage;
        ExportResultsCsvCommand.NotifyCanExecuteChanged();
        ExportExpeditionReportCommand.NotifyCanExecuteChanged();
        OpenSketchEditorCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _syncWatcher.SyncChanged -= OnSyncFolderChanged;
        AndroidDesktopSyncHub.SyncSettingsChanged -= OnExternalSyncSettingsChanged;
        _syncWatcher.Dispose();
    }
}
