using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Views;
using Wpf = System.Windows;

namespace CaveAiProForWindows.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private string _windowTitle = "CAVE AI PRO — Survey workstation";

    [ObservableProperty] private string _sourcePathDisplay = "";

    [ObservableProperty] private string _statusMessage =
        "Ready — open a CaveAI Pro backup (.json or .zip) for survey QC, exports (Survex / Therion / DXF), and batch office workflows. Ctrl+O or drag-and-drop.";

    [ObservableProperty] private bool _showIntegrityBanner;

    [ObservableProperty] private string _integrityBannerText = "";

    [ObservableProperty] private string _integrityBannerColorHex = "#FF65676B";

    [ObservableProperty] private string _integrityDetailText = "";

    [ObservableProperty] private string _backupManifestSummary = "";

    [ObservableProperty]
    private string _archiveListHint =
        "Open a .zip backup to browse entries — double-click a file to extract to temp and open with the default app; right-click for more.";

    [ObservableProperty] private ObservableCollection<ZipArchiveEntryItem> _zipArchiveEntries = new();

    [ObservableProperty] private ZipArchiveEntryItem? _selectedZipEntry;

    [ObservableProperty] private ObservableCollection<CaveProjectDocument> _projects = new();

    [ObservableProperty] private ObservableCollection<CaveRegistryRow> _caveRegistryRows = new();

    [ObservableProperty] private ObservableCollection<KnownCaveRecord> _knownCaveRows = new();

    [ObservableProperty] private ObservableCollection<BioMineralCatalogRow> _bioMineralCatalogRows = new();

    [ObservableProperty] private CaveProjectDocument? _selectedProject;

    [ObservableProperty] private string _registryCaveFilter = "";

    [ObservableProperty] private string _knownCaveCatalogFilter = "";

    [ObservableProperty] private string _registryBioFilter = "";

    [ObservableProperty] private string _shotsFilterText = "";

    /// <summary>Quick filter for the project list (name / date contains, case-insensitive).</summary>
    [ObservableProperty] private string _projectListFilter = "";

    [ObservableProperty] private bool _showSchemaNote;

    [ObservableProperty] private string _schemaNoteText = "";

    /// <summary>User has accepted the in-app legal disclaimer — required for exports, AI tab, and advanced tools.</summary>
    [ObservableProperty] private bool _legalTermsAccepted;

    [ObservableProperty] private bool _isCloudPublishing;

    [ObservableProperty] private bool _showCloudPublishProgress;

    [ObservableProperty] private bool _cloudPublishIndeterminate = true;

    [ObservableProperty] private double _cloudPublishProgressValue;

    [ObservableProperty] private string _cloudPublishStatusMessage = "";

    /// <summary>Full JSON scan of <c>data.json</c> (per project: shots/photos/audio, rocks, catalog, vectorLines, keys).</summary>
    [ObservableProperty] private string _backupDataAnalyticsText = "";

    public ObservableCollection<ShotRecord> ShotsView { get; } = new();

    public ObservableCollection<StationQcRowViewModel> StationQcRows { get; } = new();

    /// <summary>Fired when traverse data or plan station overrides change — map views should redraw.</summary>
    public event EventHandler? SurveyDataChanged;

    /// <summary>First opened .json or .zip on disk (null when workspace is empty or merged from multiple files).</summary>
    public string? PrimarySourceFilePath => _primarySourcePath;

    /// <summary>Set by <see cref="MainWindow"/> — persists Sketch Editor design layer + AI assets onto the project before save.</summary>
    public Action<CaveProjectDocument>? PersistProjectBeforeSave { get; set; }

    /// <summary>Set by <see cref="MainWindow"/> — opens print preview for the active Plan / Section / X-Ray tab.</summary>
    public Action? ShowPrintPreview { get; set; }

    /// <summary>Set by <see cref="MainWindow"/> — owner window for modals during Push to Cloud.</summary>
    public Func<Wpf.Window?>? GetOwnerWindow { get; set; }

    /// <summary>Set by <see cref="MainWindow"/> — captures survey JSON and AI assets before cloud upload.</summary>
    public Func<CloudPublishArtifactCapture?>? CaptureCloudPublishArtifacts { get; set; }

    public ObservableCollection<MapAssetRow> MapAssetRows { get; } = new();

    /// <summary>Rows from optional Android <c>map_inventory.json</c> inside an open ZIP.</summary>
    public ObservableCollection<MapInventoryRow> MapInventoryRows { get; } = new();

    /// <summary>Rows for the main window INTEGRITY tab (mirrors manifest verification / banner).</summary>
    public ObservableCollection<IntegrityIssueRow> IntegrityIssueRows { get; } = new();

    /// <summary>Topology / traverse QC rows for the main SURVEY QC tab (populated via TraverseQcStats.BuildSurveyQcIssueRows).</summary>
    public ObservableCollection<SurveyQcIssueRow> SurveyQcIssueRows { get; } = new();

    public ObservableCollection<string> RecentPaths { get; } = new();

    private string? _zipPath;
    /// <summary>First .zip among opened paths when multiple files are loaded — used only to resolve embedded <c>maps/</c> paths (integrity UI still uses <see cref="_zipPath"/>).</summary>
    private string? _auxiliaryZipForMaps;
    private CancellationTokenSource? _loadCts;
    private string? _lastExtractRoot;
    private string? _primarySourcePath;
    private int _sourceFileCount;
    private IntegrityReport? _integrityReport;
    private readonly List<CaveRegistryRow> _caveRegistryMaster = new();
    private readonly List<KnownCaveRecord> _knownCaveMaster = new();
    private readonly List<BioMineralCatalogRow> _bioCatalogMaster = new();
    /// <summary>Absolute paths of map files added via File → Open standalone map(s) or drag-drop; merged into the Maps tab.</summary>
    private readonly List<string> _standaloneMapPaths = new();
    private CancellationTokenSource? _cloudPublishCts;

    /// <summary>When a single .zip backup is open, embedded map paths resolve against this file (Plan/Section underlay).</summary>
    public string? ActiveZipPath => _zipPath;

    /// <summary>
    /// ZIP used to extract <c>maps/</c> / <c>export_assets/</c> for Plan/Section and Maps tab actions — includes sibling backup next to an exported JSON, or the first zip when multiple paths were opened.
    /// </summary>
    public string? ActiveZipPathForMaps =>
        _zipPath ?? _auxiliaryZipForMaps ?? ZipMapSiblingResolver.TryResolve(SelectedProject?.LoadedFromFile, SelectedProject);

    public string SummaryText =>
        SelectedProject == null
            ? "Select a project in the list."
            : ExplorationAnalytics.BuildSummaryText(SelectedProject);

    public string StatisticsText => TraverseQcStats.BuildSummaryText(SelectedProject);

    /// <summary>Filtered project list for the sidebar (uses <see cref="ProjectListFilter"/>).</summary>
    public ICollectionView ProjectsForList => CollectionViewSource.GetDefaultView(Projects);

    /// <summary>True when no survey file and no Android Cave Library snapshot — welcome overlay.</summary>
    public bool HasNoProjects => Projects.Count == 0 && _knownCaveMaster.Count == 0;

    public MainViewModel()
    {
        foreach (var p in RecentPathsStore.Load())
            RecentPaths.Add(p);
        LegalTermsAccepted = LegalTermsAcceptanceStore.Load();
        MapAssetRows.CollectionChanged += (_, _) => ExportMapsReportCommand.NotifyCanExecuteChanged();
        HookProjectListViewFilter(Projects);
        RefreshSurveyQcIssueRows();
        NotifyLegalGateCommands();
    }

    partial void OnLegalTermsAcceptedChanged(bool value)
    {
        LegalTermsAcceptanceStore.Save(value);
        NotifyLegalGateCommands();
    }

    /// <summary>When false, export / extract / compare / inspector flows stay disabled.</summary>
    private bool LegalTermsGateOpen() => LegalTermsAccepted;

    private void NotifyLegalGateCommands()
    {
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportSurvexCommand.NotifyCanExecuteChanged();
        ExportTherionCommand.NotifyCanExecuteChanged();
        ExportStationsCsvCommand.NotifyCanExecuteChanged();
        ExportAllProjectsToFolderCommand.NotifyCanExecuteChanged();
        ExportSurveyQcReportCommand.NotifyCanExecuteChanged();
        ExportPlanSvgCommand.NotifyCanExecuteChanged();
        ExportPlanDxfCommand.NotifyCanExecuteChanged();
        ExportSectionSvgCommand.NotifyCanExecuteChanged();
        ExportSectionDxfCommand.NotifyCanExecuteChanged();
        ExportMapsReportCommand.NotifyCanExecuteChanged();
        ExportRegistryCsvCommand.NotifyCanExecuteChanged();
        ExtractPhotosCommand.NotifyCanExecuteChanged();
        ExtractFullArchiveCommand.NotifyCanExecuteChanged();
        CompareBackupsCommand.NotifyCanExecuteChanged();
        ExtractSelectedZipEntryToDiskCommand.NotifyCanExecuteChanged();
        ExtractMapAssetToDiskCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        PublishToCloudCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCloudPublishingChanged(bool value) => PublishToCloudCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProjectChanged(CaveProjectDocument? value)
    {
        RefreshShotsView();

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(StatisticsText));
        RefreshSurveyQcIssueRows();
        RefreshStationQc();
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportSurvexCommand.NotifyCanExecuteChanged();
        ExportTherionCommand.NotifyCanExecuteChanged();
        ExportStationsCsvCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ActiveZipPathForMaps));
        CopySummaryCommand.NotifyCanExecuteChanged();
        ExportPlanSvgCommand.NotifyCanExecuteChanged();
        ExportPlanDxfCommand.NotifyCanExecuteChanged();
        ExportSectionSvgCommand.NotifyCanExecuteChanged();
        ExportSectionDxfCommand.NotifyCanExecuteChanged();
        ExportSurveyQcReportCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        PrintPreviewCommand.NotifyCanExecuteChanged();
        PublishToCloudCommand.NotifyCanExecuteChanged();
        StatusMessage = value == null
            ? "No project selected."
            : $"{value.Name} — {value.Shots.Count} shot(s)";
    }

    partial void OnRegistryCaveFilterChanged(string value) => ApplyRegistryFilters();

    partial void OnKnownCaveCatalogFilterChanged(string value) => ApplyKnownCaveCatalogFilters();

    partial void OnRegistryBioFilterChanged(string value) => ApplyRegistryFilters();

    partial void OnShotsFilterTextChanged(string value) => RefreshShotsView();

    partial void OnProjectListFilterChanged(string value)
    {
        CollectionViewSource.GetDefaultView(Projects)?.Refresh();
        CoerceSelectedProjectAfterProjectFilter();
    }

    partial void OnProjectsChanged(ObservableCollection<CaveProjectDocument> value)
    {
        HookProjectListViewFilter(value);
        OnPropertyChanged(nameof(ProjectsForList));
        OnPropertyChanged(nameof(HasNoProjects));
        ExportAllProjectsToFolderCommand.NotifyCanExecuteChanged();
        CloseWorkspaceCommand.NotifyCanExecuteChanged();
        NotifyLegalGateCommands();
    }

    private void HookProjectListViewFilter(ObservableCollection<CaveProjectDocument> list)
    {
        var view = CollectionViewSource.GetDefaultView(list);
        view.Filter = ProjectRowFilter;
        view.Refresh();
    }

    private bool ProjectRowFilter(object obj)
    {
        if (obj is not CaveProjectDocument p)
            return false;
        var f = ProjectListFilter.Trim();
        if (f.Length == 0)
            return true;
        return (p.Name ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)
            || (p.Date ?? "").Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    private void CoerceSelectedProjectAfterProjectFilter()
    {
        var view = CollectionViewSource.GetDefaultView(Projects);
        if (view == null || SelectedProject == null)
            return;
        if (!view.Cast<CaveProjectDocument>().Contains(SelectedProject))
            SelectedProject = view.Cast<CaveProjectDocument>().FirstOrDefault();
    }

    private void RefreshShotsView()
    {
        ShotsView.Clear();
        if (SelectedProject == null)
            return;
        var f = ShotsFilterText.Trim();
        IEnumerable<ShotRecord> q = SelectedProject.Shots;
        if (!string.IsNullOrEmpty(f))
        {
            q = q.Where(s =>
                (s.FromStation?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.ToStation?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.Notes?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                s.Distance.ToString(CultureInfo.InvariantCulture).Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var s in q)
            ShotsView.Add(s);
    }

    private void RefreshStationQc()
    {
        StationQcRows.Clear();
        if (SelectedProject == null)
            return;
        foreach (var spec in TraverseQcStats.BuildStationCoordinateSpecs(SelectedProject))
        {
            StationQcRows.Add(new StationQcRowViewModel(
                SelectedProject,
                spec.Name,
                spec.X,
                spec.Y,
                spec.Z,
                spec.TraverseLegsFrom,
                spec.TraverseLegsTo,
                OnStationCoordinateOverrideEdited));
        }
    }

    private void OnStationCoordinateOverrideEdited()
    {
        OnPropertyChanged(nameof(StatisticsText));
        RefreshSurveyQcIssueRows();
        SurveyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSurveyQcIssueRows()
    {
        SurveyQcIssueRows.Clear();
        foreach (var row in TraverseQcStats.BuildSurveyQcIssueRows(SelectedProject))
            SurveyQcIssueRows.Add(row);
    }

    /// <summary>Call after a shots grid cell is committed so station QC, stats text, and map renderers refresh.</summary>
    public void NotifySurveyDataEdited()
    {
        RefreshStationQc();
        OnPropertyChanged(nameof(StatisticsText));
        RefreshSurveyQcIssueRows();
        CollectionViewSource.GetDefaultView(Projects)?.Refresh();
        SurveyDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyRegistryFilters()
    {
        var fc = RegistryCaveFilter.Trim();
        var fb = RegistryBioFilter.Trim();
        CaveRegistryRows.Clear();
        foreach (var r in _caveRegistryMaster.Where(x =>
                     string.IsNullOrEmpty(fc) ||
                     x.CaveName.Contains(fc, StringComparison.OrdinalIgnoreCase) ||
                     x.Date.Contains(fc, StringComparison.OrdinalIgnoreCase) ||
                     x.SourceFile.Contains(fc, StringComparison.OrdinalIgnoreCase)))
            CaveRegistryRows.Add(r);
        BioMineralCatalogRows.Clear();
        foreach (var r in _bioCatalogMaster.Where(x =>
                     string.IsNullOrEmpty(fb) ||
                     x.CaveName.Contains(fb, StringComparison.OrdinalIgnoreCase) ||
                     x.Title.Contains(fb, StringComparison.OrdinalIgnoreCase) ||
                     x.Species.Contains(fb, StringComparison.OrdinalIgnoreCase) ||
                     x.Mineral.Contains(fb, StringComparison.OrdinalIgnoreCase) ||
                     x.Details.Contains(fb, StringComparison.OrdinalIgnoreCase)))
            BioMineralCatalogRows.Add(r);
    }

    private void ApplyKnownCaveCatalogFilters()
    {
        var f = KnownCaveCatalogFilter.Trim();
        KnownCaveRows.Clear();
        foreach (var r in _knownCaveMaster.Where(x =>
                     string.IsNullOrEmpty(f) ||
                     x.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                     x.Description.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                     x.Type.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                     x.Id.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                     x.Area.Contains(f, StringComparison.OrdinalIgnoreCase)))
            KnownCaveRows.Add(r);
        OnPropertyChanged(nameof(HasNoProjects));
    }

    private bool CanCloseWorkspace() =>
        Projects.Count > 0 ||
        _knownCaveMaster.Count > 0 ||
        _standaloneMapPaths.Count > 0 ||
        !string.IsNullOrEmpty(_zipPath) ||
        !string.IsNullOrEmpty(_primarySourcePath) ||
        MapInventoryRows.Count > 0;

    /// <summary>Unloads all opened backups, Cave Library snapshot, standalone maps, and ZIP browser state so files are no longer part of this session.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseWorkspace))]
    private void CloseWorkspace()
    {
        _loadCts?.Cancel();

        CaveMapsMarkerPathResolver.ClearCache();

        _zipPath = null;
        _auxiliaryZipForMaps = null;
        _primarySourcePath = null;
        _sourceFileCount = 0;
        _integrityReport = null;
        _lastExtractRoot = null;

        _knownCaveMaster.Clear();
        _standaloneMapPaths.Clear();

        MapInventoryRows.Clear();
        BackupDataAnalyticsText = "";

        ProjectListFilter = "";
        SelectedProject = null;
        Projects = new ObservableCollection<CaveProjectDocument>();

        _caveRegistryMaster.Clear();
        _bioCatalogMaster.Clear();
        ApplyKnownCaveCatalogFilters();
        ApplyRegistryFilters();

        RefreshMapAssets(new List<CaveProjectDocument>());

        SourcePathDisplay = "";
        WindowTitle = "CAVE AI PRO — Survey workstation";
        ShowSchemaNote = false;
        SchemaNoteText = "";
        StatusMessage =
            "Ready — open a CaveAI Pro backup (.json or .zip) for survey QC, exports (Survex / Therion / DXF), and batch office workflows. Ctrl+O or drag-and-drop.";

        ApplyIntegrityUi();
        RefreshArchivePanel();
        OnPropertyChanged(nameof(ActiveZipPath));
        OnPropertyChanged(nameof(ActiveZipPathForMaps));
        ExtractPhotosCommand.NotifyCanExecuteChanged();
        ExtractFullArchiveCommand.NotifyCanExecuteChanged();
        OpenLastExtractedFolderCommand.NotifyCanExecuteChanged();
        RevealCurrentFileInExplorerCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        ExportRegistryCsvCommand.NotifyCanExecuteChanged();
        NotifyZipEntryCommands();
        ExportMapsReportCommand.NotifyCanExecuteChanged();
        CloseWorkspaceCommand.NotifyCanExecuteChanged();
        ClearStandaloneMapsCommand.NotifyCanExecuteChanged();
        NotifyLegalGateCommands();
    }

    [RelayCommand]
    private void Exit() => Wpf.Application.Current.Shutdown();

    [RelayCommand]
    private void About()
    {
        var owner = Wpf.Application.Current.MainWindow;
        new AboutWindow { Owner = owner }.ShowDialog();
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await AppUpdateService.CheckForUpdatesAsync(Wpf.Application.Current.MainWindow, silent: false)
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenPublicLibraryCatalog()
    {
        try
        {
            PublicLibraryCatalog.ShowMapInAppWindow(Wpf.Application.Current.MainWindow);
            StatusMessage = $"Public Cave Library · {PublicLibraryCatalog.WebOrigin}";
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                ex.Message,
                "Public Cave Library",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void OpenWebCaveAi()
    {
        try
        {
            PublicLibraryCatalog.ShowCaveAiInAppWindow(Wpf.Application.Current.MainWindow);
            StatusMessage = $"Cave AI web · {PublicLibraryCatalog.WebOrigin}";
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                ex.Message,
                "Cave AI",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ShowKeyboardShortcuts()
    {
        var owner = Wpf.Application.Current.MainWindow;
        Wpf.MessageBox.Show(
            owner,
            "Ctrl+O — Open backup (.json / .zip)\n" +
            "Ctrl+W — Close workspace (unload all opened files from this session)\n" +
            "F1 — About\n" +
            "Drag and drop — same file types as Open; you can also drop standalone map files (GeoTIFF, PNG, …).\n\n" +
            "Map (PLAN / SECTION / SKETCH EDITOR, when keyboard focus is in the survey tabs — not in a text field):\n" +
            "Ctrl+1 — Pan / zoom · Ctrl+2 — Select · Ctrl+3 — Draw · Ctrl+4 — Symbol\n" +
            "Ctrl+0 — Reset zoom / pan · Ctrl+Plus or Ctrl+Numpad+ — Zoom in · Ctrl+Minus or Ctrl+Numpad− — Zoom out\n" +
            "Esc — Clear survey pick (highlight + restore survey overview pane if you collapsed it with an empty click). Toolbar Zoom in/out also works on PLAN and SECTION.",
            "Keyboard shortcuts",
            Wpf.MessageBoxButton.OK,
            Wpf.MessageBoxImage.Information);
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = CaveAiBackupFileDialogFilters.OpenBackupTitle,
            Filter = CaveAiBackupFileDialogFilters.OpenBackupFilter,
            Multiselect = true,
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        LoadFromPaths(dlg.FileNames);
    }

    [RelayCommand]
    private void OpenStandaloneMaps()
    {
        var globs = StandaloneMapFileSupport.Extensions
            .Select(static e => e.StartsWith('.') ? "*" + e : "*." + e)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase);
        var mapFilter = "Map & GIS files|" + string.Join(";", globs) + "|All files|*.*";
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open standalone map file(s) — GeoTIFF, rasters, PDF, KML, GPX, DXF, …",
            Filter = mapFilter,
            Multiselect = true,
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        var n = AddStandaloneMapPaths(dlg.FileNames);
        if (n == 0)
        {
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                "No supported map files were selected (see File → Open standalone map(s) for recognised types).",
                "Standalone maps",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
            return;
        }

        StatusMessage = n == 1
            ? "Added 1 standalone map — File → Export → Maps report (CSV) for paths."
            : $"Added {n} standalone maps — File → Export → Maps report (CSV) for paths.";
        ClearStandaloneMapsCommand.NotifyCanExecuteChanged();
        CloseWorkspaceCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanClearStandaloneMaps))]
    private void ClearStandaloneMaps()
    {
        if (_standaloneMapPaths.Count == 0)
            return;
        _standaloneMapPaths.Clear();
        RefreshMapAssets(Projects.ToList());
        StatusMessage = "Standalone map list cleared.";
        ClearStandaloneMapsCommand.NotifyCanExecuteChanged();
        CloseWorkspaceCommand.NotifyCanExecuteChanged();
    }

    private bool CanClearStandaloneMaps() => _standaloneMapPaths.Count > 0;

    /// <summary>Adds absolute paths of existing standalone map files to the session Maps list.</summary>
    public int AddStandaloneMapPaths(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim());
            }
            catch
            {
                continue;
            }

            if (!StandaloneMapFileSupport.IsStandaloneMapFile(full))
                continue;
            if (_standaloneMapPaths.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)))
                continue;
            _standaloneMapPaths.Add(full);
            added++;
        }

        if (added == 0)
            return 0;
        RefreshMapAssets(Projects.ToList());
        ClearStandaloneMapsCommand.NotifyCanExecuteChanged();
        CloseWorkspaceCommand.NotifyCanExecuteChanged();
        return added;
    }

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        LoadFromPath(path);
    }

    /// <summary>Opens a path from drag-and-drop or automation; only .json / .zip are accepted.</summary>
    public void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext != ".json" && ext != ".zip")
        {
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                "Drop a .json database file or a CaveAI Pro .zip backup.",
                "Unsupported file",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
            return;
        }

        LoadFromPath(path);
    }

    private void LoadFromPath(string path) => LoadFromPaths(new[] { path });

    /// <summary>Loads one or more JSON/ZIP files and merges all projects (heavy work runs off the UI thread).</summary>
    public void LoadFromPaths(IReadOnlyList<string> paths)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = RunLoadFromPathsAsync(paths, _loadCts.Token);
    }

    private async Task RunLoadFromPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        try
        {
            _lastExtractRoot = null;
            OpenLastExtractedFolderCommand.NotifyCanExecuteChanged();
            _auxiliaryZipForMaps = null;

            StatusMessage = "Loading backup…";

            var progress = new Progress<string>(s => StatusMessage = s);
            LoadFromPathsWorkResult work;
            try
            {
                work = await Task.Run(() => LoadFromPathsWorker.Execute(paths, progress, cancellationToken), cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                StatusMessage =
                    "Ready — open a backup from CaveAI Pro on Android (Google Play): .json or .zip (Ctrl+O or drag-and-drop). Desktop app for PC (x64) only.";
                CloseWorkspaceCommand.NotifyCanExecuteChanged();
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                StatusMessage =
                    "Ready — open a backup from CaveAI Pro on Android (Google Play): .json or .zip (Ctrl+O or drag-and-drop). Desktop app for PC (x64) only.";
                CloseWorkspaceCommand.NotifyCanExecuteChanged();
                return;
            }

            var merged = work.Merged;
            var libraryAccumulator = work.LibraryRecords;
            var orderedPaths = work.LoadedPaths;

            BackupDataAnalyticsText = work.BackupDataAnalyticsText;

            if (merged.Count == 0 && libraryAccumulator.Count == 0)
            {
                if (work.JsonSurveyLoadFailures.Count > 0)
                {
                    var esb = new StringBuilder();
                    esb.AppendLine("Could not read survey data from the following JSON file(s). Check that the file is a CaveAI Pro database export (array of projects or wrapped \"projects\" array), not another JSON format.");
                    foreach (var f in work.JsonSurveyLoadFailures)
                        esb.AppendLine("• " + f.FileName + ": " + f.Message);
                    UserErrorReporter.ShowInformation(
                        Wpf.Application.Current.MainWindow,
                        esb.ToString().TrimEnd(),
                        "Open — JSON");
                }
                else
                {
                    UserErrorReporter.ShowInformation(
                        Wpf.Application.Current.MainWindow,
                        "No valid survey data or Cave Library (cave_library.json) was loaded.",
                        "Open");
                }

                StatusMessage =
                    "Ready — open a backup from CaveAI Pro on Android (Google Play): .json or .zip (Ctrl+O or drag-and-drop). Desktop app for PC (x64) only.";
                CloseWorkspaceCommand.NotifyCanExecuteChanged();
                return;
            }

            _sourceFileCount = orderedPaths.Count;
            _primarySourcePath = orderedPaths.Count > 0 ? orderedPaths[0] : null;
            _zipPath = work.IntegrityZipPath;
            _auxiliaryZipForMaps = work.AuxiliaryZipForMaps;
            OnPropertyChanged(nameof(ActiveZipPath));
            OnPropertyChanged(nameof(ActiveZipPathForMaps));
            _integrityReport = work.IntegrityReport;

            _knownCaveMaster.Clear();
            foreach (var k in libraryAccumulator)
                _knownCaveMaster.Add(k);
            ApplyKnownCaveCatalogFilters();

            MapInventoryRows.Clear();
            foreach (var row in work.MapInventoryRows)
                MapInventoryRows.Add(row);

            // New collection replaces the default ICollectionView; clear filter and sync view current item
            // so the sidebar ListBox (bound to ProjectsForList) actually highlights the first cave.
            ProjectListFilter = "";
            Projects = new ObservableCollection<CaveProjectDocument>(merged);
            RefreshCaveRegistryAndCatalog(merged);
            var first = merged.Count > 0 ? merged[0] : null;
            if (first != null)
            {
                var view = CollectionViewSource.GetDefaultView(Projects);
                if (view != null)
                    view.MoveCurrentTo(first);
            }

            SelectedProject = first;
            AppUiSettingsStore.ApplyFullOverlaysAfterImport();
            SourcePathDisplay = orderedPaths.Count <= 1
                ? (orderedPaths.Count == 1 ? orderedPaths[0] : "")
                : $"{orderedPaths.Count} files: " + string.Join("; ", orderedPaths.Take(3)) + (orderedPaths.Count > 3 ? " …" : "");
            RefreshRecentUi();

            ApplyIntegrityUi();
            ApplySchemaNote();
            var libN = _knownCaveMaster.Count;
            if (merged.Count > 0)
            {
                StatusMessage = libN > 0
                    ? $"Loaded {merged.Count} project(s) — {libN} Cave Library card(s) — {MapAssetRows.Count} map asset row(s)."
                    : $"Loaded {merged.Count} project(s) from {orderedPaths.Count} file(s) — {MapAssetRows.Count} map asset row(s).";
            }
            else
            {
                StatusMessage =
                    $"Loaded {libN} Cave Library card(s) (Android personal catalog). Open a survey backup (.zip / data.json) to link and edit projects.";
            }

            if (work.JsonSurveyLoadFailures.Count > 0)
            {
                var wsb = new StringBuilder();
                wsb.AppendLine("Some JSON files were not loaded as survey projects (other files in this session loaded OK):");
                foreach (var f in work.JsonSurveyLoadFailures)
                    wsb.AppendLine("• " + f.FileName + ": " + f.Message);
                UserErrorReporter.ShowWarning(
                    Wpf.Application.Current.MainWindow,
                    wsb.ToString().TrimEnd(),
                    "Open — JSON");
            }

            WindowTitle = orderedPaths.Count <= 1
                ? $"CAVE AI PRO — {Path.GetFileName(orderedPaths[0])}"
                : $"CAVE AI PRO — {orderedPaths.Count} files";
            ExtractPhotosCommand.NotifyCanExecuteChanged();
            ExtractFullArchiveCommand.NotifyCanExecuteChanged();
            RevealCurrentFileInExplorerCommand.NotifyCanExecuteChanged();
            SaveProjectCommand.NotifyCanExecuteChanged();
            ExportRegistryCsvCommand.NotifyCanExecuteChanged();
            RefreshArchivePanel();
            NotifyZipEntryCommands();
            CloseWorkspaceCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            UserErrorReporter.ShowWarning(Wpf.Application.Current.MainWindow, ex.Message, "Open failed");
            StatusMessage =
                "Ready — open a backup from CaveAI Pro on Android (Google Play): .json or .zip (Ctrl+O or drag-and-drop). Desktop app for PC (x64) only.";
            CloseWorkspaceCommand.NotifyCanExecuteChanged();
        }
    }

    private void ApplySchemaNote()
    {
        ShowSchemaNote = false;
        SchemaNoteText = "";
        if (string.IsNullOrEmpty(_primarySourcePath))
            return;

        ShowSchemaNote = true;
        if (!string.IsNullOrEmpty(_zipPath))
        {
            var v = BackupManifestReader.TryReadAndroidAppVersion(_zipPath);
            SchemaNoteText = v != null
                ? $"Android backup app_version: {v}  ·  Desktop app: {AppMetadata.InformationalVersion}"
                : $"ZIP backup — desktop app: {AppMetadata.InformationalVersion} (no app_version in manifest)";
            return;
        }

        if (_sourceFileCount > 1)
        {
            SchemaNoteText =
                $"Merged from {_sourceFileCount} files (no single ZIP manifest). Desktop app: {AppMetadata.InformationalVersion}";
            return;
        }

        if (string.Equals(Path.GetExtension(_primarySourcePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            SchemaNoteText =
                $"Opened JSON export — no ZIP backup manifest. Desktop app: {AppMetadata.InformationalVersion}";
            return;
        }

        SchemaNoteText = $"Desktop app: {AppMetadata.InformationalVersion}";
    }

    private void RefreshCaveRegistryAndCatalog(IReadOnlyList<CaveProjectDocument> projects)
    {
        _caveRegistryMaster.Clear();
        _bioCatalogMaster.Clear();
        foreach (var r in BiosMineralsAndRegistryBuilder.BuildCaveRegistry(projects))
            _caveRegistryMaster.Add(r);
        foreach (var r in BiosMineralsAndRegistryBuilder.BuildBioMineralCatalog(projects))
            _bioCatalogMaster.Add(r);
        ApplyRegistryFilters();
        RefreshMapAssets(projects);
    }

    private void RefreshMapAssets(IReadOnlyList<CaveProjectDocument> projects)
    {
        for (var i = _standaloneMapPaths.Count - 1; i >= 0; i--)
        {
            try
            {
                if (!File.Exists(Path.GetFullPath(_standaloneMapPaths[i])))
                    _standaloneMapPaths.RemoveAt(i);
            }
            catch
            {
                _standaloneMapPaths.RemoveAt(i);
            }
        }

        MapAssetRows.Clear();
        var collected = MapAssetsCollector.Collect(projects, includeTraverseShotMedia: false).ToList();
        collected.Sort(MapAssetSortKeys.CompareRows);
        foreach (var row in collected)
            MapAssetRows.Add(row);
        AppendStandaloneMapPathsTo(MapAssetRows);

        ExportMapsReportCommand.NotifyCanExecuteChanged();
    }

    private void AppendStandaloneMapPathsTo(IList<MapAssetRow> rows)
    {
        foreach (var path in _standaloneMapPaths)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!File.Exists(full))
                    continue;
                rows.Add(new MapAssetRow("(standalone)", "External map file", full, null));
            }
            catch
            {
                /* skip invalid path */
            }
        }
    }

    private CaveProjectDocument? ResolveProjectForMapRow(MapAssetRow? row)
    {
        if (row == null)
            return SelectedProject;
        if (SelectedProject != null &&
            string.Equals((row.ProjectName ?? "").Trim(), (SelectedProject.Name ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return SelectedProject;
        foreach (var p in Projects)
        {
            if (string.Equals((p.Name ?? "").Trim(), (row.ProjectName ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return p;
        }

        return SelectedProject;
    }

    /// <summary>Open ZIP, first ZIP when multi-open, or a sibling ZIP next to exported JSON that contains the map entries.</summary>
    private string? ZipPathForMapOperations(MapAssetRow? row)
    {
        var primary = _zipPath ?? _auxiliaryZipForMaps;
        if (!string.IsNullOrEmpty(primary))
            return primary;
        var proj = ResolveProjectForMapRow(row);
        var jsonPath = !string.IsNullOrWhiteSpace(row?.SourceFile) ? row.SourceFile : proj?.LoadedFromFile;
        return ZipMapSiblingResolver.TryResolve(jsonPath, proj);
    }

    /// <summary>Folder next to the project .json so relative map paths from Android exports can resolve on disk.</summary>
    private string? ResolveMapSiblingBaseDirectory(MapAssetRow? row)
    {
        if (row == null)
            return null;
        if (string.Equals(row.ProjectName, "(standalone)", StringComparison.OrdinalIgnoreCase))
        {
            var sf = row.SourceFile.Trim();
            if (sf.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.GetFullPath(sf)))
                return Path.GetDirectoryName(Path.GetFullPath(sf));
            return null;
        }

        if (SelectedProject != null &&
            string.Equals(row.ProjectName, SelectedProject.Name, StringComparison.OrdinalIgnoreCase))
        {
            var lf = SelectedProject.LoadedFromFile;
            if (!string.IsNullOrEmpty(lf) &&
                lf.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.GetFullPath(lf)))
                return Path.GetDirectoryName(Path.GetFullPath(lf));
        }

        var src = row.SourceFile.Trim();
        if (!string.IsNullOrEmpty(src) &&
            src.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.GetFullPath(src)))
            return Path.GetDirectoryName(Path.GetFullPath(src));

        return null;
    }

    private void RefreshArchivePanel()
    {
        ZipArchiveEntries.Clear();
        SelectedZipEntry = null;
        try
        {
            if (string.IsNullOrEmpty(_zipPath))
            {
                BackupManifestSummary = "";
                ArchiveListHint =
                    "Open a .zip backup to browse entries — double-click a file to extract to temp and open with the default app; right-click for more.";
                return;
            }

            BackupManifestSummary = BackupManifestReader.TryReadSummary(_zipPath);
            var items = ZipArchiveCatalog.GetEntryItems(_zipPath);
            foreach (var e in items)
                ZipArchiveEntries.Add(e);
            ArchiveListHint = $"{items.Count} entr(y/ies) in this ZIP. Paths use “/” as in the archive.";
        }
        catch (Exception ex)
        {
            ArchiveListHint = "Could not read ZIP: " + ex.Message;
        }
    }

    private void NotifyZipEntryCommands()
    {
        OpenSelectedZipEntryCommand.NotifyCanExecuteChanged();
        ExtractSelectedZipEntryToDiskCommand.NotifyCanExecuteChanged();
        CopySelectedZipEntryPathCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedZipEntryChanged(ZipArchiveEntryItem? value) => NotifyZipEntryCommands();

    partial void OnSourcePathDisplayChanged(string value)
    {
        RevealCurrentFileInExplorerCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
    }

    private static string CombineTempWithArchivePath(string tempRoot, string archiveSlashPath)
    {
        var parts = archiveSlashPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var segments = new List<string> { tempRoot };
        segments.AddRange(parts);
        return Path.Combine(segments.ToArray());
    }

    private bool HasSourceOnDisk() =>
        !string.IsNullOrWhiteSpace(_primarySourcePath) && File.Exists(_primarySourcePath);

    private bool HasLastExtractRoot() =>
        !string.IsNullOrEmpty(_lastExtractRoot) && Directory.Exists(_lastExtractRoot!);

    private bool CanOpenSelectedZipFile() =>
        !string.IsNullOrEmpty(_zipPath) && SelectedZipEntry != null && !SelectedZipEntry.IsDirectory;

    private bool CanExtractSelectedZipEntryToDisk() => LegalTermsGateOpen() && CanOpenSelectedZipFile();

    private bool HasSelectedZipEntry() => SelectedZipEntry != null;

    [RelayCommand(CanExecute = nameof(HasSourceOnDisk))]
    private void RevealCurrentFileInExplorer()
    {
        if (string.IsNullOrEmpty(_primarySourcePath) || !File.Exists(_primarySourcePath))
            return;
        var p = Path.GetFullPath(_primarySourcePath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{p}\"",
            UseShellExecute = true,
        });
    }

    private bool CanSaveProject() =>
        LegalTermsGateOpen() &&
        SelectedProject != null &&
        HasSourceOnDisk() &&
        _sourceFileCount == 1 &&
        (Path.GetExtension(_primarySourcePath!).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
         Path.GetExtension(_primarySourcePath!).Equals(".zip", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// After a successful AI render, writes PNG assets + metadata and auto-saves the open .json/.zip
    /// (only when a single source file is loaded).
    /// </summary>
    public bool TryAutoPersistGenerativeRender(
        CaveProjectDocument project,
        byte[] aiMapPng,
        byte[]? structureMaskPng)
    {
        if (string.IsNullOrEmpty(_primarySourcePath) || _sourceFileCount != 1)
            return false;
        if (!File.Exists(_primarySourcePath))
            return false;

        var ext = Path.GetExtension(_primarySourcePath);
        if (!ext.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            GenerativeAssetPersistenceService.TryPersistAfterRender(
                project,
                _primarySourcePath,
                Projects.ToList(),
                aiMapPng,
                structureMaskPng,
                beforeSerialize: p =>
                {
                    if (ReferenceEquals(p, project))
                        PersistProjectBeforeSave?.Invoke(p);
                });

            StatusMessage =
                $"AI render saved — map + structure mask written to {Path.GetFileName(_primarySourcePath)}.";
            return true;
        }
        catch (Exception ex)
        {
            UserErrorReporter.ShowWarning(
                Wpf.Application.Current.MainWindow,
                ex.Message,
                "Save AI render");
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private void SaveProject()
    {
        if (SelectedProject == null || string.IsNullOrEmpty(_primarySourcePath))
            return;

        try
        {
            ProjectPersistenceService.Save(new ProjectPersistenceService.SaveRequest
            {
                Projects = Projects.ToList(),
                PrimarySourcePath = _primarySourcePath,
                BeforeSerialize = p =>
                {
                    if (ReferenceEquals(p, SelectedProject))
                        PersistProjectBeforeSave?.Invoke(p);
                },
            });

            StatusMessage =
                $"Saved {Projects.Count} project(s) — sketch mapObjects and metadata written to {Path.GetFileName(_primarySourcePath)}.";
        }
        catch (Exception ex)
        {
            UserErrorReporter.ShowWarning(
                Wpf.Application.Current.MainWindow,
                ex.Message,
                "Save project");
        }
    }

    private bool CanPrintPreview() => SelectedProject != null;

    [RelayCommand(CanExecute = nameof(CanPrintPreview))]
    private void PrintPreview() => ShowPrintPreview?.Invoke();

    [RelayCommand(CanExecute = nameof(HasLastExtractRoot))]
    private void OpenLastExtractedFolder()
    {
        if (string.IsNullOrEmpty(_lastExtractRoot) || !Directory.Exists(_lastExtractRoot)) return;
        Process.Start(new ProcessStartInfo(_lastExtractRoot) { UseShellExecute = true });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProject))]
    private void CopySummary()
    {
        Wpf.Clipboard.SetText(SummaryText);
        StatusMessage = "Summary copied to clipboard.";
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedZipFile))]
    private void OpenSelectedZipEntry()
    {
        if (string.IsNullOrEmpty(_zipPath) || SelectedZipEntry is not { IsDirectory: false } sel) return;
        try
        {
            var session = Path.Combine(Path.GetTempPath(), "CaveAiProWindows", Guid.NewGuid().ToString("N"));
            var dest = CombineTempWithArchivePath(session, sel.ArchiveRelativePath);
            ZipEntryExtractor.ExtractFile(_zipPath, sel.ArchiveRelativePath, dest);
            Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
            StatusMessage = $"Opened from temp: {sel.ArchiveRelativePath}";
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Open entry", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExtractSelectedZipEntryToDisk))]
    private void ExtractSelectedZipEntryToDisk()
    {
        if (string.IsNullOrEmpty(_zipPath) || SelectedZipEntry is not { IsDirectory: false } sel) return;
        var leaf = Path.GetFileName(sel.ArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(leaf)) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Extract file from ZIP",
            FileName = leaf,
            Filter = "All files|*.*",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            ZipEntryExtractor.ExtractFile(_zipPath, sel.ArchiveRelativePath, dlg.FileName);
            StatusMessage = "File extracted.";
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                $"Saved to:\n{dlg.FileName}",
                "Archive",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Extract failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedZipEntry))]
    private void CopySelectedZipEntryPath()
    {
        if (SelectedZipEntry == null) return;
        Wpf.Clipboard.SetText(SelectedZipEntry.ArchiveRelativePath);
        StatusMessage = "Path in archive copied.";
    }

    private bool HasAnyProjects() => Projects.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportPlanSvg()
    {
        if (SelectedProject == null) return;
        if (PlanSceneBuilder.TryBuild(SelectedProject, SurveyStationGeometry.AndroidViewModePlan) == null)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "No plan geometry for SVG.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
            return;
        }

        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export plan SVG",
            Filter = "SVG|*.svg",
            FileName = $"{safe}_plan.svg",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            using var fs = File.Create(dlg.FileName);
            SurveySvgExporter.WritePlanSvg(
                SelectedProject,
                fs,
                SurveyStationGeometry.AndroidViewModePlan,
                SurveyVisualizationMode.Standard,
                PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan));
            StatusMessage = "Plan SVG saved.";
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "SVG saved.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportPlanDxf()
    {
        if (SelectedProject == null) return;
        if (PlanSceneBuilder.TryBuild(SelectedProject, SurveyStationGeometry.AndroidViewModePlan) == null)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "No plan geometry for DXF.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
            return;
        }

        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export plan DXF",
            Filter = "DXF|*.dxf",
            FileName = $"{safe}_plan.dxf",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            using var w = new StreamWriter(dlg.FileName, false, new UTF8Encoding(false));
            SurveyDxfExporter.WritePlanDxf(SelectedProject, w, SurveyStationGeometry.AndroidViewModePlan);
            StatusMessage = "Plan DXF saved.";
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "DXF saved.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportSectionSvg()
    {
        if (SelectedProject == null) return;
        if (PlanSceneBuilder.TryBuild(SelectedProject, SurveyStationGeometry.AndroidViewModeSection) == null)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "No section geometry for SVG.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
            return;
        }

        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export section SVG",
            Filter = "SVG|*.svg",
            FileName = $"{safe}_section.svg",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            using var fs = File.Create(dlg.FileName);
            SurveySvgExporter.WritePlanSvg(
                SelectedProject,
                fs,
                SurveyStationGeometry.AndroidViewModeSection,
                SurveyVisualizationMode.Standard,
                PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Section));
            StatusMessage = "Section SVG saved.";
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "SVG saved.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportSectionDxf()
    {
        if (SelectedProject == null) return;
        if (PlanSceneBuilder.TryBuild(SelectedProject, SurveyStationGeometry.AndroidViewModeSection) == null)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "No section geometry for DXF.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
            return;
        }

        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export section DXF",
            Filter = "DXF|*.dxf",
            FileName = $"{safe}_section.dxf",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            using var w = new StreamWriter(dlg.FileName, false, new UTF8Encoding(false));
            SurveyDxfExporter.WritePlanDxf(SelectedProject, w, SurveyStationGeometry.AndroidViewModeSection);
            StatusMessage = "Section DXF saved.";
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "DXF saved.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportMapsReport))]
    private void ExportMapsReport()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export maps report (CSV)",
            Filter = "CSV|*.csv",
            FileName = "caveai_maps_report.csv",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            var snapshot = MapAssetsCollector.Collect(Projects.ToList(), includeTraverseShotMedia: true).ToList();
            snapshot.Sort(MapAssetSortKeys.CompareRows);
            AppendStandaloneMapPathsTo(snapshot);
            File.WriteAllBytes(dlg.FileName, MapAssetsReportExporter.BuildUtf8Bom(snapshot));
            StatusMessage = $"Maps report saved — {snapshot.Count} row(s).";
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                $"CSV saved (UTF-8 BOM).\n{snapshot.Count} row(s) (includes traverse photo/audio paths when present).",
                "Export",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SelectKnownCaveFromLibrary(KnownCaveRecord? cave)
    {
        if (cave == null)
            return;
        if (Projects.Count == 0)
        {
            StatusMessage =
                $"No survey loaded — “{cave.Name}” is a library card only. Open a backup that contains data.json with linked surveys.";
            return;
        }

        foreach (var p in Projects)
        {
            var link = TryLinkedLibraryCaveId(p);
            if (!string.IsNullOrEmpty(link) &&
                !string.IsNullOrEmpty(cave.Id) &&
                string.Equals(link, cave.Id, StringComparison.OrdinalIgnoreCase))
            {
                SelectedProject = p;
                StatusMessage = $"{p.Name} — selected via Cave Library id link.";
                return;
            }
        }

        foreach (var p in Projects)
        {
            if (string.Equals((p.Name ?? "").Trim(), cave.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                SelectedProject = p;
                StatusMessage = $"{p.Name} — matched by cave name (confirm this is the intended survey).";
                return;
            }
        }

        StatusMessage =
            $"No linked survey for “{cave.Name}”. Link projects on Android (linkedLibraryCaveId) or use the same cave name in the backup.";
    }

    private static string? TryLinkedLibraryCaveId(CaveProjectDocument p)
    {
        if (!string.IsNullOrWhiteSpace(p.LinkedLibraryCaveId))
            return p.LinkedLibraryCaveId.Trim();
        if (p.ExtensionData?.TryGetValue("linkedLibraryCaveId", out var el) != true)
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    [RelayCommand]
    private void SelectCaveFromRegistryRow(CaveRegistryRow? row)
    {
        if (row == null)
            return;
        foreach (var p in Projects)
        {
            var n = string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name;
            if (!string.Equals(n, row.CaveName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(p.LoadedFromFile ?? "", row.SourceFile ?? "", StringComparison.OrdinalIgnoreCase))
                continue;
            SelectedProject = p;
            return;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportRegistryCsvGated))]
    private void ExportRegistryCsv()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export registry CSV",
            Filter = "CSV|*.csv",
            FileName = "caveai_registry.csv",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, RegistryCsvExporter.BuildUtf8Bom(_caveRegistryMaster, _bioCatalogMaster));
            StatusMessage = "Registry CSV saved.";
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "CSV saved (UTF-8 BOM).", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(LegalTermsGateOpen))]
    private void CompareBackups()
    {
        var owner = Wpf.Application.Current.MainWindow;
        new CompareBackupsWindow { Owner = owner }.ShowDialog();
    }

    private void RefreshRecentUi()
    {
        RecentPaths.Clear();
        foreach (var p in RecentPathsStore.Load())
            RecentPaths.Add(p);
    }

    private void ApplyIntegrityUi()
    {
        IntegrityIssueRows.Clear();
        var r = _integrityReport;
        if (r == null)
        {
            ShowIntegrityBanner = false;
            IntegrityDetailText = "";
            return;
        }

        ShowIntegrityBanner = true;
        if (r.SkippedReason != null)
        {
            IntegrityBannerText = "Integrity: " + r.SkippedReason;
            IntegrityBannerColorHex = "#FFD29922";
            IntegrityDetailText = r.SkippedReason;
            IntegrityIssueRows.Add(new IntegrityIssueRow("Notice", r.SkippedReason));
            return;
        }

        if (r.IsCompleteSuccess)
        {
            IntegrityBannerText = $"Integrity verified — {r.FilesMatched}/{r.FilesChecked} files (SHA-256).";
            IntegrityBannerColorHex = "#FF3FB950";
            IntegrityDetailText = IntegrityBannerText;
            IntegrityIssueRows.Add(new IntegrityIssueRow("OK", IntegrityBannerText));
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Matched {r.FilesMatched}/{r.FilesChecked}, missing in ZIP: {r.FilesMissingInZip}.");
        foreach (var line in r.Mismatches)
            sb.AppendLine("• " + line);
        IntegrityDetailText = sb.ToString().TrimEnd();
        IntegrityBannerText = $"Integrity issues — see INTEGRITY tab ({r.Mismatches.Count} line(s)).";
        IntegrityBannerColorHex = "#FFF85149";

        IntegrityIssueRows.Add(new IntegrityIssueRow(
            "Summary",
            $"Matched {r.FilesMatched} of {r.FilesChecked} manifest file(s); missing inside ZIP: {r.FilesMissingInZip}."));
        foreach (var line in r.Mismatches)
            IntegrityIssueRows.Add(new IntegrityIssueRow("Issue", line));
    }

    private bool HasSelectedProject() => SelectedProject != null;

    private bool HasZip() => !string.IsNullOrEmpty(_zipPath);

    private bool CanExportWithSelectedProject() => LegalTermsGateOpen() && SelectedProject != null;

    private bool CanExportSurvex() =>
        LegalTermsGateOpen() && SelectedProject?.Shots?.Any(s => s.IsTraverseLeg) == true;

    private bool CanExportTherion() => CanExportSurvex();

    private bool CanExportStationsCsv() => CanExportSurvex();

    private bool CanExtractFromZip() => LegalTermsGateOpen() && HasZip();

    private bool CanExportRegistryCsvGated() => LegalTermsGateOpen() && HasAnyProjects();

    private bool CanExportMapsReport() => LegalTermsGateOpen() && MapAssetRows.Count > 0;

    private bool CanExportAllProjectsToFolder() => LegalTermsGateOpen() && Projects.Count >= 1;

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportCsv()
    {
        if (SelectedProject == null) return;
        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export shots CSV",
            Filter = "CSV (Excel-friendly)|*.csv|All files|*.*",
            DefaultExt = ".csv",
            FileName = $"{safe}_shots.csv",
            AddExtension = true,
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, ExplorationAnalytics.ExportShotsToCsvUtf8Bom(SelectedProject));
            StatusMessage = "CSV saved: " + dlg.FileName;
            SnackbarService.ShowFileSaved(dlg.FileName, "CSV saved —");
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportSurvex))]
    private void ExportSurvex()
    {
        if (SelectedProject == null) return;
        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Survex centerline",
            Filter = "Survex centerline|*.svx|All files|*.*",
            DefaultExt = ".svx",
            FileName = $"{safe}.svx",
            AddExtension = true,
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, SurvexExporter.BuildSvxUtf8Bom(SelectedProject));
            StatusMessage = "Survex saved: " + dlg.FileName;
            SnackbarService.ShowFileSaved(dlg.FileName, "Survex (.svx) saved —");
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportTherion))]
    private void ExportTherion()
    {
        if (SelectedProject == null) return;
        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Therion centerline",
            Filter = "Therion centerline|*.th|All files|*.*",
            DefaultExt = ".th",
            FileName = $"{safe}.th",
            AddExtension = true,
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, TherionExporter.BuildCenterlineThUtf8Bom(SelectedProject));
            StatusMessage = "Therion .th saved: " + dlg.FileName;
            SnackbarService.ShowFileSaved(dlg.FileName, "Therion (.th) saved —");
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportStationsCsv))]
    private void ExportStationsCsv()
    {
        if (SelectedProject == null) return;
        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export station coordinates",
            Filter = "CSV|*.csv",
            FileName = $"{safe}_stations_xyz.csv",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, StationCoordinatesCsvExporter.BuildUtf8Bom(SelectedProject));
            StatusMessage = "Stations CSV saved.";
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                "CSV saved: plan-frame x,y,z in metres (same geometry as the Plan tab), UTF-8 BOM.",
                "Export",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportAllProjectsToFolder))]
    private void ExportAllProjectsToFolder()
    {
        if (Projects.Count == 0)
            return;
        var dlg = new OpenFolderDialog
        {
            Title = "Folder for batch export (one set of files per loaded project)",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            var list = Projects.ToList();
            var result = SurveyBatchExporter.ExportAllToFolder(list, dlg.FolderName);
            StatusMessage = result.Errors.Count == 0
                ? $"Batch export: {result.FilesWritten} file(s) for {result.ProjectCount} project(s)."
                : $"Batch export: {result.FilesWritten} file(s); {result.Errors.Count} error(s).";
            var msg = result.Errors.Count == 0
                ? $"Wrote {result.FilesWritten} file(s) for {result.ProjectCount} project(s) to:\n{dlg.FolderName}"
                : $"Wrote {result.FilesWritten} file(s); some paths failed:\n\n" + string.Join("\n", result.Errors.Take(8))
                  + (result.Errors.Count > 8 ? $"\n… +{result.Errors.Count - 8} more" : "");
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                msg,
                "Batch export",
                Wpf.MessageBoxButton.OK,
                result.Errors.Count > 0 ? Wpf.MessageBoxImage.Warning : Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Batch export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportSurveyQcReport()
    {
        if (SelectedProject == null)
            return;
        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new SaveFileDialog
        {
            Title = "Survey & QC report (text)",
            Filter = "Text|*.txt|All|*.*",
            FileName = $"{safe}_survey_qc_report.txt",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            File.WriteAllBytes(dlg.FileName, SurveyOfficeReportExporter.BuildUtf8Bom(SelectedProject));
            StatusMessage = "Survey & QC report saved.";
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                "Report saved (UTF-8 BOM): summary, traverse statistics, and QC hints.",
                "Export",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExtractFromZip))]
    private void ExtractFullArchive()
    {
        if (string.IsNullOrEmpty(_zipPath)) return;
        var dlg = new OpenFolderDialog
        {
            Title = "Folder to extract the entire CaveAI ZIP (preserves paths: photos/, data.json, …)",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            var dest = Path.Combine(dlg.FolderName, Path.GetFileNameWithoutExtension(_zipPath) + "_extracted");
            var n = ZipFullExtractor.ExtractAll(_zipPath, dest);
            _lastExtractRoot = n > 0 ? dest : null;
            OpenLastExtractedFolderCommand.NotifyCanExecuteChanged();
            StatusMessage = n == 0 ? "No files extracted." : $"Extracted {n} file(s).";
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                n == 0
                    ? "No files were written."
                    : $"Extracted {n} file(s) to:\n{dest}",
                "Archive",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Extract failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExtractFromZip))]
    private void ExtractPhotos()
    {
        if (string.IsNullOrEmpty(_zipPath)) return;
        var dlg = new OpenFolderDialog
        {
            Title = "Folder to extract photos/ from the CaveAI ZIP",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            var n = ZipPhotoExtractor.ExtractPhotos(_zipPath, dlg.FolderName);
            StatusMessage = n == 0 ? "No photos in ZIP." : $"Extracted {n} photo file(s).";
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                n == 0 ? "No photos/ entries in this ZIP." : $"Extracted {n} file(s) to:\n{dlg.FolderName}",
                "Photos",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Extract failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenMapAsset(MapAssetRow? row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.UriOrPath))
            return;
        var err = MapAssetOpener.TryOpen(row.UriOrPath, ZipPathForMapOperations(row));
        if (err == null)
        {
            StatusMessage = $"Opened map asset ({row.Category}).";
            return;
        }

        Wpf.MessageBox.Show(
            Wpf.Application.Current.MainWindow,
            err,
            "Maps — open",
            Wpf.MessageBoxButton.OK,
            Wpf.MessageBoxImage.Information);
    }

    [RelayCommand]
    private void CopyMapUri(MapAssetRow? row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.UriOrPath))
            return;
        Wpf.Clipboard.SetText(row.UriOrPath);
        StatusMessage = "URI/path copied.";
    }

    [RelayCommand]
    private void RevealMapAssetInExplorer(MapAssetRow? row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.UriOrPath))
            return;
        if (!MapAssetOpener.TryEnsureLocalFilePath(row.UriOrPath, ZipPathForMapOperations(row), out var path, out var err, ResolveMapSiblingBaseDirectory(row)))
        {
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                err ?? "Could not resolve file.",
                "Maps — Explorer",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
            return;
        }

        var full = Path.GetFullPath(path!);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{full}\"",
            UseShellExecute = true,
        });
        StatusMessage = "File location shown in Explorer.";
    }

    [RelayCommand(CanExecute = nameof(LegalTermsGateOpen))]
    private void ExtractMapAssetToDisk(MapAssetRow? row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.UriOrPath))
            return;
        if (!MapAssetOpener.TryEnsureLocalFilePath(row.UriOrPath, ZipPathForMapOperations(row), out var resolved, out var resErr, ResolveMapSiblingBaseDirectory(row)))
        {
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                resErr ?? "Could not resolve the map to a local file.",
                "Maps — extract",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
            return;
        }

        var leaf = Path.GetFileName(resolved!);
        var saveDlg = new SaveFileDialog
        {
            Title = "Save map file as",
            FileName = string.IsNullOrEmpty(leaf) ? "map_asset" : leaf,
            Filter = "All files|*.*",
        };
        if (saveDlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            File.Copy(resolved!, saveDlg.FileName, overwrite: true);
            StatusMessage = "File saved.";
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                $"Saved to:\n{saveDlg.FileName}",
                "Maps",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Save failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    private bool CanPublishToCloud() =>
        LegalTermsGateOpen() && SelectedProject != null && !IsCloudPublishing;

    [RelayCommand(CanExecute = nameof(CanPublishToCloud))]
    private async Task PublishToCloudAsync()
    {
        if (_cloudPublishCts != null)
            return;

        _cloudPublishCts = new CancellationTokenSource();
        var ct = _cloudPublishCts.Token;
        string? lastError = null;

        try
        {
            IsCloudPublishing = true;
            ShowCloudPublishProgress = true;
            CloudPublishIndeterminate = true;
            CloudPublishProgressValue = 0;
            CloudPublishStatusMessage = "Starting publish…";

            var progress = new Progress<CloudPublishProgressUpdate>(update =>
            {
                CloudPublishStatusMessage = update.Message;
                ShowCloudPublishProgress = true;
                CloudPublishIndeterminate = update.IsIndeterminate;
                if (update.ProgressPercent is double p && !double.IsNaN(p))
                    CloudPublishProgressValue = p;
                StatusMessage = update.Message;
                if (update.HasError)
                    lastError = update.ErrorMessage;
            });

            var metadata = await CloudPublishWorkflow.RunAsync(new CloudPublishWorkflow.Request
            {
                GetProject = () => SelectedProject,
                GetLegalTermsAccepted = () => LegalTermsAccepted,
                CaptureArtifacts = () => CaptureCloudPublishArtifacts?.Invoke(),
                GetOwnerWindow = () => GetOwnerWindow?.Invoke(),
                PersistLinkedLibraryCaveId = _ =>
                {
                    if (SelectedProject != null)
                        PersistProjectBeforeSave?.Invoke(SelectedProject);
                    StatusMessage = "Linked library cave id updated — save project to persist to disk.";
                },
                Progress = progress,
                CancellationToken = ct,
            }).ConfigureAwait(true);

            var owner = GetOwnerWindow?.Invoke();
            if (metadata != null)
            {
                CloudPublishProgressValue = 100;
                SnackbarService.Show(owner, $"Published to Cave Library — {metadata.PublishedCaveDocId}");
            }
            else if (!string.IsNullOrWhiteSpace(lastError))
            {
                SnackbarService.Show(owner, lastError, durationMs: 6000);
            }
        }
        finally
        {
            _cloudPublishCts?.Dispose();
            _cloudPublishCts = null;
            IsCloudPublishing = false;
            CloudPublishIndeterminate = false;
            ShowCloudPublishProgress = false;
            PublishToCloudCommand.NotifyCanExecuteChanged();
        }
    }
}
