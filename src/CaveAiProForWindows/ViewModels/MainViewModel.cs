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
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.Legal;
using CaveAiProForWindows.Services.Localization;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Services.ReferenceCatalog;
using CaveAiProForWindows.Services.SurveyAnalysis;
using CaveAiProForWindows.Services.SurveyCloud;
using CaveAiProForWindows.Views;
using Wpf = System.Windows;

namespace CaveAiProForWindows.ViewModels;

public enum ReferenceSurveyResumeFromDiskResult
{
    NotFound,
    Cancelled,
    Resumed,
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private string _windowTitle = "CAVE AI PRO — Survey workstation";

    /// <summary>True when the open project has unsaved in-memory edits (pins, entrance, X-Ray bounds, …).</summary>
    [ObservableProperty] private bool _isDirty;

    /// <summary>Toolbar subtitle with assembly version (e.g. Survey workstation · v1.2.1).</summary>
    public string ToolbarVersionText { get; } = $"Survey workstation · v{AppMetadata.InformationalVersion}";

    [ObservableProperty] private string _sourcePathDisplay = "";

    [ObservableProperty] private string _statusMessage =
        "Ready — open a CaveAI Pro backup (.json or .zip) for survey QC, Survex/Therion import, Loop closure (Compass/WLS), exports (Survex / Therion / DXF), and batch office workflows. Ctrl+O or drag-and-drop.";

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

    [ObservableProperty] private bool _androidSyncBannerVisible;

    [ObservableProperty] private string _androidSyncBannerMessage = "";

    [ObservableProperty] private bool _accountBannerVisible;

    [ObservableProperty] private string _accountBannerMessage = "";

    [ObservableProperty] private bool _storeReviewBannerVisible = MicrosoftTestMode.IsActive;

    [ObservableProperty] private bool _updateAvailableBannerVisible;

    [ObservableProperty] private string _updateAvailableBannerMessage = "";

    private readonly CloudCommandsViewModel _cloudCommands;

    /// <summary>Cloud publish retry queue and local publish history.</summary>
    public CloudCommandsViewModel CloudCommands => _cloudCommands;

    private readonly WorkspaceRecentFilesViewModel _workspaceRecent;

    /// <summary>Recent backup paths (File menu and welcome sidebar).</summary>
    public WorkspaceRecentFilesViewModel WorkspaceRecent => _workspaceRecent;

    private readonly WorkspaceSessionViewModel _workspaceSession;

    /// <summary>Dirty-session UX, discard-confirm, and workspace close/unload coordination.</summary>
    public WorkspaceSessionViewModel WorkspaceSession => _workspaceSession;

    private string _pendingReleasePageUrl = "";

    [ObservableProperty] private int _collaborationUnreadCount;

    [ObservableProperty] private string _footerContextLine = "";

    [ObservableProperty] private string _authStatusChip = "";

    [ObservableProperty] private bool _showLoadProgress;

    [ObservableProperty] private string _loadProgressMessage = "";

    [ObservableProperty] private string _referenceLinkSummary = "";

    /// <summary>Full JSON scan of <c>data.json</c> (per project: shots/photos/audio, rocks, catalog, vectorLines, keys).</summary>
    [ObservableProperty] private string _backupDataAnalyticsText = "";

    public ObservableCollection<ShotRecord> ShotsView { get; } = new();

    public ObservableCollection<StationQcRowViewModel> StationQcRows { get; } = new();

    public event EventHandler? SurveyDataChanged;

    public void NotifySurveyDataChanged() => SurveyDataChanged?.Invoke(this, EventArgs.Empty);

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

    /// <summary>Set by <see cref="MainWindow"/> — opens Sketch Editor design mode from survey traverse.</summary>
    public Action<bool>? NavigateToDesignFromSurvey { get; set; }

    /// <summary>Set by <see cref="MainWindow"/> — selects the SURFACE map tab.</summary>
    public Action? NavigateToSurfaceTab { get; set; }

    /// <summary>Set by <see cref="MainWindow"/> — resets Plan/Section/X-ray/Surface/sketch surfaces when the workspace is cleared or replaced.</summary>
    public Action? ResetSurveyViewSurfaces { get; set; }

    public ObservableCollection<MapAssetRow> MapAssetRows { get; } = new();

    /// <summary>Rows from optional Android <c>map_inventory.json</c> inside an open ZIP.</summary>
    public ObservableCollection<MapInventoryRow> MapInventoryRows { get; } = new();

    /// <summary>Rows for the main window INTEGRITY tab (mirrors manifest verification / banner).</summary>
    public ObservableCollection<IntegrityIssueRow> IntegrityIssueRows { get; } = new();

    /// <summary>Topology / traverse QC rows for the main SURVEY QC tab (populated via TraverseQcStats.BuildSurveyQcIssueRows).</summary>
    public ObservableCollection<SurveyQcIssueRow> SurveyQcIssueRows { get; } = new();

    public ObservableCollection<string> RecentPaths => _workspaceRecent.RecentPaths;

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
    private string? _cloudAssetCacheDir;

    /// <summary>When a single .zip backup is open, embedded map paths resolve against this file (Plan/Section underlay).</summary>
    public string? ActiveZipPath => _zipPath;

    /// <summary>Local folder for Survey Cloud map assets (e.g. surface_lidar) when opened via File → Open from cloud.</summary>
    public string? CloudAssetCacheDir => _cloudAssetCacheDir;

    /// <summary>
    /// ZIP used to extract <c>maps/</c> / <c>export_assets/</c> for Plan/Section and Maps tab actions — includes sibling backup next to an exported JSON, or the first zip when multiple paths were opened.
    /// </summary>
    public string? ActiveZipPathForMaps =>
        _zipPath ?? _auxiliaryZipForMaps ?? ZipMapSiblingResolver.TryResolve(SelectedProject?.LoadedFromFile, SelectedProject);

    /// <summary>Active project for Surface tab (entrance lat/lon + surfaceLidarRaster).</summary>
    public CaveProjectDocument? SurfaceMapProject => SelectedProject;

    public string SummaryText =>
        SelectedProject == null
            ? "Select a project in the list."
            : ExplorationAnalytics.BuildSummaryText(SelectedProject);

    public string StatisticsText => TraverseQcStats.BuildSummaryText(SelectedProject);

    /// <summary>On-device survey hints for SURVEY QC tab (mirrors GEO/BIO and publication sheet panels).</summary>
    public string OfflineBrainHintText => CaveAiOfflineBrain.FormatStatusHintPanel(SelectedProject);

    [ObservableProperty] private string _caveAiQueryText = "";

    [ObservableProperty] private string _caveAiAnswerText =
        "Ask about depth, traverse length, loops, volume, or next step. Answers stay on this PC.";

    /// <summary>English site-identity line for status bar tooltip (mirrors Android <c>SiteIdentity.kt</c>).</summary>
    public string SiteIdentityTooltip =>
        SiteIdentity.SummarizeActiveProject(SelectedProject, _knownCaveMaster);

    /// <summary>Human-readable site type for the active project (e.g. Mine, Cave).</summary>
    public string SelectedProjectSiteTypeLabel =>
        SelectedProject == null ? "" : SurveySiteTypeResolver.GetMapLabel(SelectedProject);

    /// <summary>Editable site-type token for the selected project (persisted on Save).</summary>
    public string SelectedProjectSiteTypeToken
    {
        get
        {
            if (SelectedProject == null)
                return "";
            var raw = SelectedProject.SurveySiteType;
            if (!string.IsNullOrWhiteSpace(raw))
                return SurveySiteType.NormalizeToken(raw);
            return SurveySiteType.CanonicalToken(SurveySiteTypeResolver.Resolve(SelectedProject, _knownCaveMaster));
        }
        set
        {
            if (SelectedProject == null || string.IsNullOrWhiteSpace(value))
                return;
            var token = SurveySiteType.NormalizeToken(value);
            if (string.IsNullOrEmpty(token))
                return;
            if (string.Equals(SelectedProject.SurveySiteType, token, StringComparison.Ordinal))
                return;
            SelectedProject.SurveySiteType = token;
            OnPropertyChanged(nameof(SelectedProjectSiteTypeLabel));
            OnPropertyChanged(nameof(SiteIdentityTooltip));
            OnPropertyChanged(nameof(SelectedProjectSiteTypeToken));
            MarkDirty("Site type updated — Ctrl+S to save");
            StatusMessage = FormatSelectedProjectStatus(SelectedProject);
            SaveProjectCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasSelectedProjectForSiteType => SelectedProject != null;

    /// <summary>Filtered project list for the sidebar (uses <see cref="ProjectListFilter"/>).</summary>
    public ICollectionView ProjectsForList => CollectionViewSource.GetDefaultView(Projects);

    /// <summary>True when no survey file and no Android Cave Library snapshot — welcome overlay.</summary>
    public bool HasNoProjects => Projects.Count == 0 && _knownCaveMaster.Count == 0;

    public MainViewModel()
    {
        _cloudCommands = new CloudCommandsViewModel(this);
        _workspaceRecent = new WorkspaceRecentFilesViewModel(this);
        _workspaceSession = new WorkspaceSessionViewModel(this);
        LegalTermsAccepted = LegalTermsAcceptanceStore.Load();
        MapAssetRows.CollectionChanged += (_, _) => ExportMapsReportCommand.NotifyCanExecuteChanged();
        HookProjectListViewFilter(Projects);
        RefreshSurveyQcIssueRows();
        NotifyLegalGateCommands();
        CloudPublishWebViewHost.TokenCache.TokenUpdated += (_, _) => RefreshAuthStatusChip();
    }

    partial void OnLegalTermsAcceptedChanged(bool value)
    {
        RefreshAuthStatusChip();
        _cloudCommands.NotifyPublishStateChanged();
        LegalTermsAcceptanceStore.Save(value);
        if (value)
        {
            App.WriteStartupLog(
                $"Legal terms accepted (document v{LegalTexts.DocumentVersion}, UTC {DateTime.UtcNow:O})");
        }

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
        ExportUnifiedQcReportCommand.NotifyCanExecuteChanged();
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
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        PublishToCloudCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCloudPublishingChanged(bool value)
    {
        PublishToCloudCommand.NotifyCanExecuteChanged();
        _cloudCommands.NotifyPublishStateChanged();
    }

    partial void OnSelectedProjectChanged(CaveProjectDocument? value)
    {
        RefreshShotsView();

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(StatisticsText));
        OnPropertyChanged(nameof(OfflineBrainHintText));
        AskCaveAiCommand.NotifyCanExecuteChanged();
        SaveSurveyProjectToCloudCommand.NotifyCanExecuteChanged();
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
        ExportUnifiedQcReportCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        PrintPreviewCommand.NotifyCanExecuteChanged();
        PublishToCloudCommand.NotifyCanExecuteChanged();
        _cloudCommands.NotifyPublishStateChanged();
        RefreshReferenceLinkSummary();
        OnPropertyChanged(nameof(SiteIdentityTooltip));
        OnPropertyChanged(nameof(SelectedProjectSiteTypeLabel));
        OnPropertyChanged(nameof(SelectedProjectSiteTypeToken));
        OnPropertyChanged(nameof(HasSelectedProjectForSiteType));
        OnPropertyChanged(nameof(SurfaceMapProject));
        StatusMessage = value == null
            ? "No project selected."
            : FormatSelectedProjectStatus(value);
    }

    private static string FormatSelectedProjectStatus(CaveProjectDocument project)
    {
        var site = SurveySiteTypeResolver.GetMapLabel(project);
        var sitePart = string.IsNullOrWhiteSpace(site) ? "" : $" · {site}";
        return $"{project.Name}{sitePart} — {project.Shots.Count} shot(s)";
    }

    private void RefreshReferenceLinkSummary()
    {
        if (SelectedProject != null &&
            ReferenceSurveyLinkService.TryGetLink(SelectedProject, out var link) &&
            link != null)
        {
            ReferenceLinkSummary = AppStrings.ReferenceLinkedSummary(
                ReferenceSurveyLinkService.FormatSummary(link));
        }
        else
        {
            ReferenceLinkSummary = "";
        }
    }

    private async Task PromptReferenceLinkIfNeededAsync(CaveProjectDocument project)
    {
        try
        {
            var fetch = new ReferenceCatalogFetchService();
            var state = await fetch.LoadBrowseIndexAsync().ConfigureAwait(true);
            ReferenceSurveyLinkPrompt.TryPromptForProjects(
                Wpf.Application.Current.MainWindow,
                new[] { project },
                state.IndexEntries);
            RefreshReferenceLinkSummary();
        }
        catch
        {
            /* optional */
        }
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
        _cloudCommands.RefreshRetryCount();
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
        OnPropertyChanged(nameof(OfflineBrainHintText));
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
        OnPropertyChanged(nameof(OfflineBrainHintText));
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

    /// <summary>Marks the session dirty and shows * in the window title until cleared / successful save.</summary>
    public void MarkDirty(string? statusHint = null) => _workspaceSession.MarkDirty(statusHint);

    public void ClearDirty() => _workspaceSession.ClearDirty();

    private void SetWindowTitleBase(string titleWithoutAsterisk) =>
        _workspaceSession.SetWindowTitleBase(titleWithoutAsterisk);

    /// <summary>
    /// Returns false if the user cancelled. On Yes, attempts save; if save is impossible, warns and returns false.
    /// </summary>
    public bool ConfirmDiscardUnsavedChanges(string? contextLabel = null) =>
        _workspaceSession.ConfirmDiscardUnsavedChanges(contextLabel);

    private bool CanCloseWorkspace() => _workspaceSession.CanCloseWorkspace();

    /// <summary>Unloads all opened backups, Cave Library snapshot, standalone maps, and ZIP browser state so files are no longer part of this session.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseWorkspace))]
    private void CloseWorkspace() => _workspaceSession.TryCloseWorkspace();

    internal bool HasLoadedWorkspaceContentInternal() =>
        Projects.Count > 0 ||
        _knownCaveMaster.Count > 0 ||
        _standaloneMapPaths.Count > 0 ||
        !string.IsNullOrEmpty(_zipPath) ||
        !string.IsNullOrEmpty(_primarySourcePath) ||
        MapInventoryRows.Count > 0;

    private bool UnloadWorkspaceBeforeNewLoad() => _workspaceSession.UnloadWorkspaceBeforeNewLoad();

    internal void ClearWorkspaceDataCore()
    {
        _loadCts?.Cancel();

        CaveMapsMarkerPathResolver.ClearCache();

        _zipPath = null;
        _auxiliaryZipForMaps = null;
        _primarySourcePath = null;
        _sourceFileCount = 0;
        _integrityReport = null;
        _lastExtractRoot = null;
        _cloudAssetCacheDir = null;
        _loadedSurveyFingerprint = null;
        OnPropertyChanged(nameof(CloudAssetCacheDir));

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

        ShotsView.Clear();
        StationQcRows.Clear();
        SurveyQcIssueRows.Clear();
        ReferenceLinkSummary = "";

        SourcePathDisplay = "";
        ShowSchemaNote = false;
        SchemaNoteText = "";

        ApplyIntegrityUi();
        RefreshArchivePanel();
        OnPropertyChanged(nameof(ActiveZipPath));
        OnPropertyChanged(nameof(ActiveZipPathForMaps));

        WorkspaceSessionReset.ClearSurfaceMapViewport();
        WorkspaceSessionReset.ClearTransientSurveyUiState();
        ResetSurveyViewSurfaces?.Invoke();
        SurveyDataChanged?.Invoke(this, EventArgs.Empty);

        NotifyWorkspaceCommandStateChanged();
    }

    internal void NotifyWorkspaceCommandStateChanged()
    {
        ExtractPhotosCommand.NotifyCanExecuteChanged();
        ExtractFullArchiveCommand.NotifyCanExecuteChanged();
        OpenLastExtractedFolderCommand.NotifyCanExecuteChanged();
        RevealCurrentFileInExplorerCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
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

    /// <summary>Opens web Cave AI in the in-app Public Library WebView (embed=windows).</summary>
    [RelayCommand]
    private void OpenCaveAiWeb()
    {
        var url = SelectedProject != null &&
                  ReferenceSurveyLinkService.TryGetLink(SelectedProject, out var link) &&
                  link != null
            ? CaveAiWebUrls.BuildFromSurveyLink(link)
            : CaveAiWebUrls.BaseUrl;
        try
        {
            PublicLibraryCatalog.ShowInAppWindow(Wpf.Application.Current.MainWindow, url);
            StatusMessage = "Opened Cave AI in Public Library";
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not open Cave AI: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await AppUpdateService.CheckForUpdatesAsync(Wpf.Application.Current.MainWindow, silent: false)
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private void ExportDiagnosticBundle()
    {
        try
        {
            var path = DiagnosticBundleExporter.ExportToZip();
            StatusMessage = $"Diagnostic bundle saved — {path}";
            SnackbarService.Show(Wpf.Application.Current.MainWindow, StatusMessage);
        }
        catch (Exception ex)
        {
            UserErrorReporter.ShowWarning(Wpf.Application.Current.MainWindow, ex.Message, "Diagnostic bundle");
        }
    }

    [RelayCommand]
    private void CompareReferenceCommunity() =>
        ReferenceCommunityCompareWindow.ShowDialog(Wpf.Application.Current.MainWindow);

    [RelayCommand]
    private void CompareSurveyToReferenceLink()
    {
        var project = SelectedProject;
        if (project == null)
        {
            UserErrorReporter.ShowInformation(Wpf.Application.Current.MainWindow, "Select a cave project in the list first.", "Survey vs reference");
            return;
        }

        if (!ReferenceSurveyLinkService.TryGetLink(project, out _))
        {
            UserErrorReporter.ShowInformation(
                Wpf.Application.Current.MainWindow,
                "This project has no referenceCatalogLink. Link it from the Reference catalog window or open a backup that already includes the link.",
                "Survey vs reference");
            return;
        }

        ReferenceCommunityCompareWindow.ShowSurveyLinkCompare(Wpf.Application.Current.MainWindow, project);
    }

    [RelayCommand]
    private void OpenFieldTripPlanner() => FieldTripPlannerWindow.ShowDialog(Wpf.Application.Current.MainWindow);

    [RelayCommand]
    private void BatchSurveyQc() => BatchSurveyQcWindow.ShowDialog(Wpf.Application.Current.MainWindow);

    [RelayCommand]
    private void CancelLoad()
    {
        _loadCts?.Cancel();
        ShowLoadProgress = false;
        StatusMessage = "Load cancelled.";
    }

    [RelayCommand]
    private void OpenDiagnosticFolder()
    {
        if (DiagnosticLogPaths.TryOpenFolder())
            return;

        Wpf.MessageBox.Show(
            Wpf.Application.Current.MainWindow,
            "Could not open the diagnostic folder.\r\n\r\n" + DiagnosticLogPaths.AppDataDirectory,
            "CAVE AI PRO",
            Wpf.MessageBoxButton.OK,
            Wpf.MessageBoxImage.Warning);
    }

    private static bool TryNotifyCloudBlockedInTestMode(string featureTitle)
    {
        if (!MicrosoftTestMode.IsActive)
            return false;

        Wpf.MessageBox.Show(
            Wpf.Application.Current.MainWindow,
            MicrosoftTestMode.CloudFeatureBlockedMessage,
            featureTitle,
            Wpf.MessageBoxButton.OK,
            Wpf.MessageBoxImage.Information);
        return true;
    }

    [RelayCommand]
    private void OpenPublicLibraryCatalog()
    {
        OpenPublicLibraryWithPicker();
    }

    [RelayCommand]
    private void OpenPublicLibraryWithPicker()
    {
        if (TryNotifyCloudBlockedInTestMode("Public Cave Library"))
            return;

        var owner = Wpf.Application.Current.MainWindow;
        switch (PublicLibraryEntryPicker.Prompt(owner))
        {
            case PublicLibraryEntryPicker.Choice.NativeCatalog:
                try
                {
                    PublicLibraryCatalog.ShowNativeReferenceCatalog(owner);
                    StatusMessage = $"Reference catalog · {PublicLibraryCatalog.WebOrigin}";
                }
                catch (Exception ex)
                {
                    Wpf.MessageBox.Show(owner, ex.Message, "Public Cave Library", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
                }
                break;
            case PublicLibraryEntryPicker.Choice.WebMap:
                try
                {
                    PublicLibraryCatalog.ShowMapInAppWindow(owner);
                    StatusMessage = "Public Library — web map";
                }
                catch (Exception ex)
                {
                    Wpf.MessageBox.Show(owner, ex.Message, "Public Cave Library", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
                }
                break;
            case PublicLibraryEntryPicker.Choice.ExternalBrowser:
                try
                {
                    PublicLibraryCatalog.OpenMap();
                }
                catch (Exception ex)
                {
                    Wpf.MessageBox.Show(owner, ex.Message, "Public Cave Library", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
                }
                break;
        }
    }

    [RelayCommand]
    private void ShowCommandPalette()
    {
        var owner = Wpf.Application.Current.MainWindow;
        if (owner == null)
            return;
        var palette = new CommandPaletteWindow(this) { Owner = owner };
        palette.ShowDialog();
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
        _cloudAssetCacheDir = null;
        OnPropertyChanged(nameof(CloudAssetCacheDir));
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
    private void ImportSurvex() =>
        ImportCenterlineFile(
            title: "Import Survex centerline",
            filter: "Survex centerline|*.svx|All files|*.*",
            formatLabel: "Survex",
            survex: true);

    [RelayCommand]
    private void ImportTherion() =>
        ImportCenterlineFile(
            title: "Import Therion centerline",
            filter: "Therion centerline|*.th|All files|*.*",
            formatLabel: "Therion",
            survex: false);

    private void ImportCenterlineFile(string title, string filter, string formatLabel, bool survex)
    {
        var owner = Wpf.Application.Current.MainWindow;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = false,
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            CaveProjectDocument project;
            int traverse;
            int splay;
            IReadOnlyList<string> warnings;
            if (survex)
            {
                var imported = SurvexImporter.ParseFile(dlg.FileName);
                project = imported.Project;
                traverse = imported.TraverseLegs;
                splay = imported.SplayLegs;
                warnings = imported.Warnings;
            }
            else
            {
                var imported = TherionImporter.ParseFile(dlg.FileName);
                project = imported.Project;
                traverse = imported.TraverseLegs;
                splay = imported.SplayLegs;
                warnings = imported.Warnings;
            }

            if (!UnloadWorkspaceBeforeNewLoad())
                return;
            ProjectListFilter = "";
            Projects = new ObservableCollection<CaveProjectDocument>(new[] { project });
            RefreshCaveRegistryAndCatalog(Projects.ToList());
            SelectedProject = project;
            _loadedSurveyFingerprint = SurveyContentFingerprint.Compute(project);
            _primarySourcePath = dlg.FileName;
            SourcePathDisplay = dlg.FileName;
            SetWindowTitleBase($"CAVE AI PRO — {Path.GetFileName(dlg.FileName)}");
            ClearDirty();
            AppUiSettingsStore.ApplyFullOverlaysAfterImport();
            RefreshReferenceLinkSummary();
            ApplyIntegrityUi();
            OnPropertyChanged(nameof(ActiveZipPath));
            OnPropertyChanged(nameof(ActiveZipPathForMaps));

            var warn = warnings.Count > 0
                ? " Warnings: " + string.Join(" ", warnings.Take(3))
                : "";
            StatusMessage =
                $"Imported {formatLabel} “{project.Name}” — {traverse} traverse, {splay} splay.{warn}";
            SnackbarService.Show(owner, $"{formatLabel} imported — {traverse} traverse leg(s).");
            if (warnings.Count > 0)
            {
                Wpf.MessageBox.Show(
                    owner,
                    string.Join("\n", warnings),
                    $"{formatLabel} import notes",
                    Wpf.MessageBoxButton.OK,
                    Wpf.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(owner, ex.Message, $"{formatLabel} import failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
            StatusMessage = $"{formatLabel} import failed.";
        }
    }

    [RelayCommand]
    private async Task OpenSurveyProjectFromCloud()
    {
        if (MicrosoftTestMode.IsActive)
        {
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                "Survey Cloud is disabled in Microsoft certification test mode.",
                "Survey Cloud",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
            return;
        }

        var owner = GetOwnerWindow?.Invoke() ?? Wpf.Application.Current.MainWindow;
        try
        {
            var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
            if (token == null)
            {
                await DesktopAuthWindow.AcquireTokenAsync(owner, CloudPublishWebViewHost.TokenCache).ConfigureAwait(true);
                token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
            }

            if (token == null)
            {
                Wpf.MessageBox.Show(
                    owner,
                    "Sign in with the same Google account you use in CaveAI Pro on Android to open private cloud surveys.",
                    "Survey Cloud",
                    Wpf.MessageBoxButton.OK,
                    Wpf.MessageBoxImage.Information);
                return;
            }

            var meta = await SurveyCloudProjectPickerWindow.TryPickAsync(owner, token).ConfigureAwait(true);
            if (meta == null)
                return;

            StatusMessage = $"Downloading “{meta.CaveName}” from Survey Cloud…";
            var jsonBytes = await SurveyCloudProjectService.DownloadProjectJsonBytesAsync(token, meta).ConfigureAwait(true);
            var jsonPath = await SurveyCloudProjectService.DownloadProjectJsonToTempFileAsync(token, meta).ConfigureAwait(true);

            var cacheDir = Path.Combine(
                Path.GetTempPath(),
                "CaveAiProForWindows",
                "SurveyCloud",
                meta.ProjectId,
                "assets");
            Directory.CreateDirectory(cacheDir);
            await SurveyCloudProjectService.TryDownloadSurfaceLidarFromProjectJsonAsync(
                token, meta, jsonBytes, cacheDir).ConfigureAwait(true);

            _cloudAssetCacheDir = cacheDir;
            OnPropertyChanged(nameof(CloudAssetCacheDir));

            LoadFromPaths(new[] { jsonPath });
            StatusMessage = $"Opened cloud survey “{meta.CaveName}”. Surface LiDAR assets load on the Surface tab when present.";
        }
        catch (Exception ex)
        {
            UserErrorReporter.ShowWarning(owner, ex.Message, "Survey Cloud");
            StatusMessage = "Survey Cloud open failed.";
        }
    }

    private bool CanSaveSurveyProjectToCloud() =>
        LegalTermsGateOpen() && SelectedProject != null && !MicrosoftTestMode.IsActive;

    [RelayCommand(CanExecute = nameof(CanSaveSurveyProjectToCloud))]
    private async Task SaveSurveyProjectToCloud()
    {
        if (MicrosoftTestMode.IsActive)
        {
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                "Survey Cloud is disabled in Microsoft certification test mode.",
                "Survey Cloud",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
            return;
        }

        if (SelectedProject == null)
            return;

        var owner = GetOwnerWindow?.Invoke() ?? Wpf.Application.Current.MainWindow;
        try
        {
            var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
            if (token == null)
            {
                await DesktopAuthWindow.AcquireTokenAsync(owner, CloudPublishWebViewHost.TokenCache).ConfigureAwait(true);
                token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
            }

            if (token == null)
            {
                Wpf.MessageBox.Show(
                    owner,
                    "Sign in with the same Google account you use in CaveAI Pro on Android to save private cloud surveys.",
                    "Survey Cloud",
                    Wpf.MessageBoxButton.OK,
                    Wpf.MessageBoxImage.Information);
                return;
            }

            var confirm = Wpf.MessageBox.Show(
                owner,
                "Upload this survey to Survey Cloud?\n\n" +
                "The JSON is stored privately under your account (same path as Android). " +
                "Loop-closure plan overrides are included.",
                "Save to Survey Cloud",
                Wpf.MessageBoxButton.YesNo,
                Wpf.MessageBoxImage.Question);
            if (confirm != Wpf.MessageBoxResult.Yes)
                return;

            PersistProjectBeforeSave?.Invoke(SelectedProject);
            StatusMessage = $"Uploading “{SelectedProject.Name}” to Survey Cloud…";
            var meta = await SurveyCloudProjectService.UploadProjectAsync(token, SelectedProject).ConfigureAwait(true);
            MarkDirty();
            if (CanSaveProject())
                SaveProject();
            StatusMessage = $"Saved “{meta.CaveName}” to Survey Cloud ({meta.ShotCount} shots).";
            Wpf.MessageBox.Show(
                owner,
                $"Uploaded to Survey Cloud.\n\nPreview: {meta.PreviewUrl}",
                "Survey Cloud",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UserErrorReporter.ShowWarning(owner, ex.Message, "Survey Cloud");
            StatusMessage = "Survey Cloud upload failed.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanAskCaveAi))]
    private void AskCaveAi()
    {
        CaveAiAnswerText = CaveAiOfflineBrain.Answer(SelectedProject, CaveAiQueryText, _knownCaveMaster);
    }

    private bool CanAskCaveAi() => LegalTermsGateOpen() && SelectedProject != null;

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
    private void OpenRecent(string? path) => _workspaceRecent.OpenRecent(path);

    [RelayCommand]
    private void RemoveRecent(string? path) => _workspaceRecent.RemoveRecent(path);

    [RelayCommand]
    private void RevealRecentPath(string? path) => _workspaceRecent.RevealRecentPath(path);

    [RelayCommand]
    private void RenameRecentPath(string? path) => _workspaceRecent.RenameRecentPath(path);

    [RelayCommand]
    private void DeleteRecentPath(string? path) => _workspaceRecent.DeleteRecentPath(path);

    internal void LoadFromPathPublic(string path) => LoadFromPath(path);

    internal void ApplyPrimarySourcePathRename(string oldPath, string newPath)
    {
        if (!string.Equals(_primarySourcePath, oldPath, StringComparison.OrdinalIgnoreCase))
            return;

        _primarySourcePath = newPath;
        if (_zipPath != null && string.Equals(_zipPath, oldPath, StringComparison.OrdinalIgnoreCase))
            _zipPath = newPath;
        if (_auxiliaryZipForMaps != null &&
            string.Equals(_auxiliaryZipForMaps, oldPath, StringComparison.OrdinalIgnoreCase))
            _auxiliaryZipForMaps = newPath;

        SourcePathDisplay = newPath;
        SetWindowTitleBase($"CAVE AI PRO — {Path.GetFileName(newPath)}");
        RevealCurrentFileInExplorerCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
    }

    internal void CloseWorkspaceIfPrimary(string path)
    {
        if (string.Equals(_primarySourcePath, path, StringComparison.OrdinalIgnoreCase))
            CloseWorkspace();
    }

    internal Wpf.Window? OwnerWindow => GetOwnerWindow?.Invoke();

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

    public void LoadFromPath(string path) => LoadFromPaths(new[] { path });

    /// <summary>Opens a single in-memory project (e.g. started from Reference catalog).</summary>
    /// <returns>False if the user cancelled leaving a dirty workspace.</returns>
    public bool LoadReferenceSurveyProject(CaveProjectDocument project, KnownCaveRecord? libraryCard = null)
    {
        if (!UnloadWorkspaceBeforeNewLoad())
            return false;

        ProjectListFilter = "";
        Projects = new ObservableCollection<CaveProjectDocument>(new[] { project });
        NamedCartographyDocuments.EnsureNamedDocuments(project);
        _knownCaveMaster.Clear();
        if (libraryCard != null && !string.IsNullOrWhiteSpace(libraryCard.Id))
            _knownCaveMaster.Add(libraryCard);
        ApplyKnownCaveCatalogFilters();
        OnPropertyChanged(nameof(SiteIdentityTooltip));
        RefreshCaveRegistryAndCatalog(Projects.ToList());
        SelectedProject = project;
        _loadedSurveyFingerprint = SurveyContentFingerprint.Compute(project);
        _primarySourcePath = null;
        _sourceFileCount = 0;
        _zipPath = null;
        SourcePathDisplay = "Reference catalog";
        SetWindowTitleBase($"CAVE AI PRO — {project.Name}");
        AppUiSettingsStore.ApplyFullOverlaysAfterImport();
        RefreshReferenceLinkSummary();
        ApplyIntegrityUi();
        OnPropertyChanged(nameof(ActiveZipPath));
        OnPropertyChanged(nameof(ActiveZipPathForMaps));
        var owner = Wpf.Application.Current.MainWindow;
        SnackbarService.Show(owner, "Reference survey project created.");
        MarkDirty($"Started survey “{project.Name}” from reference catalog — File → Save as… (Android backup ZIP).");
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        CloseWorkspaceCommand.NotifyCanExecuteChanged();
        return true;
    }

    /// <summary>Android resume-if-same-pin: activate an already-open project with this reference id.</summary>
    public bool TryResumeReferenceSurvey(string referenceId)
    {
        var existing = ReferenceSurveyLinkService.FindExistingProject(Projects, referenceId);
        if (existing == null)
            return false;

        SelectedProject = existing;
        StatusMessage = $"Resumed survey “{existing.Name}” (same reference pin).";
        SnackbarService.Show(Wpf.Application.Current.MainWindow, "Resumed existing survey for this cave.");
        return true;
    }

    /// <summary>
    /// Reopens the last Android backup ZIP remembered for this reference pin and selects the matching project.
    /// </summary>
    public ReferenceSurveyResumeFromDiskResult TryResumeReferenceSurveyFromDisk(string referenceId)
    {
        var path = ReferenceSurveyResumeStore.TryGetPath(referenceId);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return ReferenceSurveyResumeFromDiskResult.NotFound;
        if (!UnloadWorkspaceBeforeNewLoad())
            return ReferenceSurveyResumeFromDiskResult.Cancelled;

        try
        {
            var projects = ExplorationDataLoader.LoadAuto(path).ToList();
            var match = ReferenceSurveyLinkService.FindExistingProject(projects, referenceId);
            if (match == null)
                return ReferenceSurveyResumeFromDiskResult.NotFound;

            var library = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? CaveLibraryJsonLoader.TryLoadFromZip(path)
                : Array.Empty<KnownCaveRecord>();

            ProjectListFilter = "";
            Projects = new ObservableCollection<CaveProjectDocument>(projects);
            foreach (var p in projects)
                NamedCartographyDocuments.EnsureNamedDocuments(p);
            _knownCaveMaster.Clear();
            foreach (var k in library)
                _knownCaveMaster.Add(k);
            ApplyKnownCaveCatalogFilters();
            OnPropertyChanged(nameof(SiteIdentityTooltip));
            RefreshCaveRegistryAndCatalog(projects);
            SelectedProject = match;
            _loadedSurveyFingerprint = SurveyContentFingerprint.Compute(match);
            AttachPrimarySourceAfterNewZip(path);
            ClearDirty();
            AppUiSettingsStore.ApplyFullOverlaysAfterImport();
            RefreshReferenceLinkSummary();
            StatusMessage = $"Resumed survey “{match.Name}” from {Path.GetFileName(path)}.";
            SnackbarService.Show(Wpf.Application.Current.MainWindow, "Resumed survey from last backup.");
            return ReferenceSurveyResumeFromDiskResult.Resumed;
        }
        catch
        {
            return ReferenceSurveyResumeFromDiskResult.NotFound;
        }
    }

    /// <summary>Loads one or more JSON/ZIP files and merges all projects (heavy work runs off the UI thread).</summary>
    public void LoadFromPaths(IReadOnlyList<string> paths)
    {
        if (!UnloadWorkspaceBeforeNewLoad())
            return;
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
            ShowLoadProgress = paths.Any(p =>
            {
                try
                {
                    return File.Exists(p) && new FileInfo(p).Length > 8 * 1024 * 1024;
                }
                catch
                {
                    return false;
                }
            });
            LoadProgressMessage = StatusMessage;

            var progress = new Progress<string>(s =>
            {
                StatusMessage = s;
                LoadProgressMessage = s;
            });
            LoadFromPathsWorkResult work;
            try
            {
                work = await Task.Run(() => LoadFromPathsWorker.Execute(paths, progress, cancellationToken), cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                ShowLoadProgress = false;
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
            // Migrate unlabeled (pre-named-maps) projects so the cartography panel is always consistent.
            foreach (var proj in merged)
                NamedCartographyDocuments.EnsureNamedDocuments(proj);
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
            if (_primarySourcePath != null &&
                _primarySourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(_primarySourcePath))
                _lastLoadedBackupWriteUtc = File.GetLastWriteTimeUtc(_primarySourcePath);
            _zipPath = work.IntegrityZipPath;
            _auxiliaryZipForMaps = work.AuxiliaryZipForMaps;
            OnPropertyChanged(nameof(ActiveZipPath));
            OnPropertyChanged(nameof(ActiveZipPathForMaps));
            _integrityReport = work.IntegrityReport;

            _knownCaveMaster.Clear();
            foreach (var k in libraryAccumulator)
                _knownCaveMaster.Add(k);
            ApplyKnownCaveCatalogFilters();
            OnPropertyChanged(nameof(SiteIdentityTooltip));

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
            _loadedSurveyFingerprint = SurveyContentFingerprint.Compute(first);
            RefreshReferenceLinkSummary();
            if (first != null)
                _ = PromptReferenceLinkIfNeededAsync(first);
            AppUiSettingsStore.ApplyFullOverlaysAfterImport();
            SourcePathDisplay = orderedPaths.Count <= 1
                ? (orderedPaths.Count == 1 ? orderedPaths[0] : "")
                : $"{orderedPaths.Count} files: " + string.Join("; ", orderedPaths.Take(3)) + (orderedPaths.Count > 3 ? " …" : "");
            _workspaceRecent.RefreshRecentUi();

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

            SetWindowTitleBase(orderedPaths.Count <= 1
                ? $"CAVE AI PRO — {Path.GetFileName(orderedPaths[0])}"
                : $"CAVE AI PRO — {orderedPaths.Count} files");
            ClearDirty();
            ExtractPhotosCommand.NotifyCanExecuteChanged();
            ExtractFullArchiveCommand.NotifyCanExecuteChanged();
            RevealCurrentFileInExplorerCommand.NotifyCanExecuteChanged();
            SaveProjectCommand.NotifyCanExecuteChanged();
            ExportRegistryCsvCommand.NotifyCanExecuteChanged();
            RefreshArchivePanel();
            NotifyZipEntryCommands();
            CloseWorkspaceCommand.NotifyCanExecuteChanged();
            ShowLoadProgress = false;
            _ = TryPromptReferenceLinksAsync(merged);
        }
        catch (Exception ex)
        {
            ShowLoadProgress = false;
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

    internal bool HasSourceOnDisk() =>
        !string.IsNullOrWhiteSpace(_primarySourcePath) && File.Exists(_primarySourcePath);

    internal int SourceFileCount => _sourceFileCount;

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

    private bool CanSaveProject() => _workspaceSession.CanSaveProject();

    private bool CanSaveProjectOrSaveAs() =>
        CanSaveProject() || CanSaveProjectAs();

    internal void NotifySaveProjectCanExecuteChanged()
    {
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveProjectOrSaveAs))]
    private void SaveProject()
    {
        if (CanSaveProject())
            _workspaceSession.SaveProject();
        else
            TrySaveProjectAsAndroidBackup();
    }

    private bool CanSaveProjectAs() => LegalTermsGateOpen() && SelectedProject != null;

    [RelayCommand(CanExecute = nameof(CanSaveProjectAs))]
    private void SaveProjectAs() => TrySaveProjectAsAndroidBackup();

    /// <summary>Writes an Android-shaped backup ZIP and makes it the Ctrl+S source.</summary>
    public bool TrySaveProjectAsAndroidBackup()
    {
        if (SelectedProject == null || !LegalTermsAccepted)
            return false;

        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save as Android backup ZIP",
            Filter = "CaveAI backup ZIP|*.zip",
            FileName = $"CaveAI_Backup_{safe}.zip",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return false;

        try
        {
            WriteAndroidBackupZip(dlg.FileName);
            AttachPrimarySourceAfterNewZip(dlg.FileName);
            ClearDirty();
            StatusMessage = "Android backup ZIP saved: " + dlg.FileName;
            SnackbarService.ShowFileSaved(dlg.FileName, "Android backup —");
            return true;
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                ex.Message,
                "Save as failed",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Error);
            return false;
        }
    }

    private string? ActiveSourceZipForExport =>
        string.Equals(Path.GetExtension(_primarySourcePath), ".zip", StringComparison.OrdinalIgnoreCase)
            ? _primarySourcePath
            : _zipPath;

    private void WriteAndroidBackupZip(string zipPath)
    {
        PersistProjectBeforeSave?.Invoke(SelectedProject!);
        AndroidBackupZipExporter.ExportSingleProject(
            SelectedProject!,
            zipPath,
            ActiveSourceZipForExport,
            PersistProjectBeforeSave,
            _knownCaveMaster);
    }

    private void AttachPrimarySourceAfterNewZip(string zipPath)
    {
        _primarySourcePath = zipPath;
        _sourceFileCount = 1;
        _zipPath = zipPath;
        if (File.Exists(zipPath))
            _lastLoadedBackupWriteUtc = File.GetLastWriteTimeUtc(zipPath);
        SourcePathDisplay = zipPath;
        SetWindowTitleBase($"CAVE AI PRO — {Path.GetFileName(zipPath)}");
        try
        {
            _integrityReport = IntegrityVerifier.VerifyZip(zipPath);
        }
        catch
        {
            _integrityReport = null;
        }

        ApplyIntegrityUi();
        ApplySchemaNote();
        RefreshArchivePanel();
        NotifyZipEntryCommands();
        OnPropertyChanged(nameof(ActiveZipPath));
        OnPropertyChanged(nameof(ActiveZipPathForMaps));
        RevealCurrentFileInExplorerCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        ExtractPhotosCommand.NotifyCanExecuteChanged();
        ExtractFullArchiveCommand.NotifyCanExecuteChanged();
        CloseWorkspaceCommand.NotifyCanExecuteChanged();

        if (SelectedProject != null &&
            ReferenceSurveyLinkService.TryGetLink(SelectedProject, out var link) &&
            !string.IsNullOrWhiteSpace(link?.Id))
        {
            ReferenceSurveyResumeStore.Remember(link!.Id, zipPath);
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

    [RelayCommand(CanExecute = nameof(LegalTermsGateOpen))]
    private void CompareSurveys()
    {
        var owner = Wpf.Application.Current.MainWindow;
        new SurveyCompareWindow { Owner = owner }.ShowDialog();
    }

    [RelayCommand(CanExecute = nameof(CanShowLoopClosureAssistant))]
    private void ShowLoopClosureAssistant()
    {
        if (SelectedProject == null)
            return;
        var owner = Wpf.Application.Current.MainWindow;
        new LoopClosureAssistantWindow(
            SelectedProject,
            canApplyInPlace: CanSaveProject(),
            applyInPlace: result =>
            {
                SurveyLoopClosureAdjuster.ApplyToPlanOverrides(SelectedProject, result);
                MarkDirty();
                if (CanSaveProject())
                    SaveProject();
                RefreshStationQc();
                OnPropertyChanged(nameof(StatisticsText));
                OnPropertyChanged(nameof(SummaryText));
            }) { Owner = owner }.ShowDialog();
    }

    private bool CanShowLoopClosureAssistant() => LegalTermsGateOpen() && SelectedProject != null;

    [RelayCommand(CanExecute = nameof(CanOpenPublicationSheet))]
    private void OpenPublicationSheet()
    {
        if (SelectedProject == null)
            return;
        var owner = Wpf.Application.Current.MainWindow;
        new PublicationSheetWindow(SelectedProject) { Owner = owner }.Show();
    }

    private bool CanOpenPublicationSheet() => LegalTermsGateOpen() && SelectedProject != null;

    [RelayCommand(CanExecute = nameof(CanRepublishToCloud))]
    private async Task RepublishToCloudAsync() =>
        await RunCloudPublishForProjectsAsync(new[] { SelectedProject! }, requireLinkedId: true).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanRepublishBatch))]
    private async Task RepublishAllWithAiAsync()
    {
        var targets = Projects.Where(HasRepublishableAiAssets).ToList();
        await RunCloudPublishForProjectsAsync(targets, requireLinkedId: true).ConfigureAwait(true);
    }

    private bool CanRepublishToCloud() =>
        LegalTermsGateOpen() && SelectedProject != null && !IsCloudPublishing &&
        !string.IsNullOrWhiteSpace(LinkedLibraryCaveIdResolver.TryGet(SelectedProject)) &&
        HasRepublishableAiAssets(SelectedProject);

    private bool CanRepublishBatch() =>
        LegalTermsGateOpen() && !IsCloudPublishing && Projects.Any(HasRepublishableAiAssets);

    private static bool HasRepublishableAiAssets(CaveProjectDocument? p) =>
        p != null && !string.IsNullOrWhiteSpace(ProjectAiAssetPersistence.TryReadAiMapRelativePath(p));

    [RelayCommand(CanExecute = nameof(LegalTermsGateOpen))]
    private async Task DownloadPublicLibraryBackupAsync()
    {
        if (TryNotifyCloudBlockedInTestMode("Public Library download"))
            return;

        var owner = Wpf.Application.Current.MainWindow;
        if (!LibraryCavePickerWindow.TryPick(owner, out var docId) || string.IsNullOrWhiteSpace(docId))
            return;

        var dlg = new SaveFileDialog
        {
            Title = "Download Public Library backup",
            Filter = "CaveAI ZIP|*.zip|JSON|*.json",
            FileName = docId + ".zip",
            DefaultExt = ".zip",
        };
        if (dlg.ShowDialog(owner) != true)
            return;

        try
        {
            IsCloudPublishing = true;
            ShowCloudPublishProgress = true;
            CloudPublishIndeterminate = true;
            CloudPublishStatusMessage = "Downloading from Public Library…";
            var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
            var progress = new Progress<string>(m =>
            {
                CloudPublishStatusMessage = m;
                StatusMessage = m;
            });
            var result = await PublicLibraryBackupDownloader.DownloadAsync(
                docId,
                dlg.FileName,
                token,
                progress).ConfigureAwait(true);
            StatusMessage = $"Downloaded {result.CaveName ?? docId} — {result.AssetCount} asset(s).";
            SnackbarService.Show(owner, StatusMessage);

            var openNow = Wpf.MessageBox.Show(
                owner,
                $"Saved to {result.OutputPath}\n\nOpen this backup in the workspace now?",
                "Download complete",
                Wpf.MessageBoxButton.YesNo,
                Wpf.MessageBoxImage.Question);
            if (openNow == Wpf.MessageBoxResult.Yes)
                LoadFromPath(result.OutputPath);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(owner, ex.Message, "Download failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
        }
        finally
        {
            IsCloudPublishing = false;
            ShowCloudPublishProgress = false;
        }
    }

    private async Task RunCloudPublishForProjectsAsync(
        IReadOnlyList<CaveProjectDocument> targets,
        bool requireLinkedId)
    {
        if (_cloudPublishCts != null || targets.Count == 0)
            return;

        if (TryNotifyCloudBlockedInTestMode("Publish to Cloud"))
            return;

        _cloudPublishCts = new CancellationTokenSource();
        var ct = _cloudPublishCts.Token;
        try
        {
            IsCloudPublishing = true;
            ShowCloudPublishProgress = true;
            CloudPublishIndeterminate = true;
            CloudPublishStatusMessage = "Re-publishing…";

            foreach (var project in targets)
            {
                if (requireLinkedId && string.IsNullOrWhiteSpace(LinkedLibraryCaveIdResolver.TryGet(project)))
                    continue;

                if (!await TryConfirmPublishReferenceMatchesAsync(project).ConfigureAwait(true))
                    continue;

                var previous = SelectedProject;
                SelectedProject = project;
                var progress = new Progress<CloudPublishProgressUpdate>(u =>
                {
                    CloudPublishStatusMessage = u.Message;
                    StatusMessage = u.Message;
                });

                await CloudPublishWorkflow.RunAsync(new CloudPublishWorkflow.Request
                {
                    GetProject = () => project,
                    GetLegalTermsAccepted = () => LegalTermsAccepted,
                    CaptureArtifacts = () => CloudPublishArtifactCollector.Collect(
                        project,
                        () => CaptureCloudPublishArtifacts?.Invoke(),
                        PersistProjectBeforeSave),
                    GetOwnerWindow = () => GetOwnerWindow?.Invoke(),
                    PersistLinkedLibraryCaveId = _ => PersistProjectBeforeSave?.Invoke(project),
                    Progress = progress,
                    CancellationToken = ct,
                }).ConfigureAwait(true);

                SelectedProject = previous;
            }

            StatusMessage = $"Re-published {targets.Count} project(s) to Public Library.";
        }
        finally
        {
            _cloudPublishCts?.Dispose();
            _cloudPublishCts = null;
            IsCloudPublishing = false;
            ShowCloudPublishProgress = false;
            RepublishToCloudCommand.NotifyCanExecuteChanged();
            RepublishAllWithAiCommand.NotifyCanExecuteChanged();
        }
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
        IntegrityBannerText = $"Integrity issues — click for details ({r.Mismatches.Count} line(s)).";
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
        var owner = Wpf.Application.Current.MainWindow;
        var optionsWindow = new Views.SurvexExportOptionsWindow(SelectedProject, owner);
        if (optionsWindow.ShowDialog() != true || optionsWindow.Result == null)
            return;

        var confirm = Wpf.MessageBox.Show(
            owner,
            "Survex export includes a provisional *fix coordinate anchor and default conventions.\n\n" +
            "Verify stations, units (metres / degrees), declination, and the fix before merging with production surveys or surface loops.",
            "CAVE AI PRO — Verify Survex export",
            Wpf.MessageBoxButton.OKCancel,
            Wpf.MessageBoxImage.Warning);
        if (confirm != Wpf.MessageBoxResult.OK)
            return;

        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Survex centerline",
            Filter = "Survex centerline|*.svx|All files|*.*",
            DefaultExt = ".svx",
            FileName = $"{safe}.svx",
            AddExtension = true,
        };
        if (dlg.ShowDialog(owner) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, SurvexExporter.BuildSvxUtf8Bom(SelectedProject, optionsWindow.Result));
            StatusMessage = "Survex saved: " + dlg.FileName;
            SnackbarService.ShowFileSaved(dlg.FileName, "Survex (.svx) saved —");
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(owner, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
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

    [RelayCommand(CanExecute = nameof(CanExportTherion))]
    private void ExportTherionProject()
    {
        if (SelectedProject == null) return;
        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Therion project folder",
            Filter = "Folder marker|*.therionfolder",
            FileName = $"{safe}.therionfolder",
            AddExtension = true,
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true) return;
        var folder = Path.Combine(Path.GetDirectoryName(dlg.FileName) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), safe + "-therion");
        try
        {
            var result = TherionProjectExporter.ExportProjectFolder(SelectedProject, folder);
            StatusMessage = $"Therion project exported ({result.WrittenFiles.Count} files): {folder}";
            SnackbarService.Show(Wpf.Application.Current.MainWindow, $"Therion project saved — {result.WrittenFiles.Count} files.");
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanShareCollaboration))]
    private async Task ShareCollaborationAsync()
    {
        if (SelectedProject == null) return;
        if (TryNotifyCloudBlockedInTestMode("Project collaboration"))
            return;
        await ProjectCollaborationWindow.ShareAndOpenAsync(SelectedProject, GetOwnerWindow?.Invoke() ?? Wpf.Application.Current.MainWindow);
    }

    private bool CanShareCollaboration() => LegalTermsGateOpen() && SelectedProject != null;

    private DateTime? _lastLoadedBackupWriteUtc;

    private string? _loadedSurveyFingerprint;

    private string? _pendingAndroidBackupPath;

    public void NotifyAndroidBackupDetected(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return;

        _pendingAndroidBackupPath = zipPath;
        var name = Path.GetFileName(zipPath);
        AndroidSyncBannerMessage = AppStrings.AndroidSyncBannerMessage(name);
        AndroidSyncBannerVisible = true;
        StatusMessage = AppStrings.AndroidSyncBannerStatus(name);
        SnackbarService.Show(Wpf.Application.Current.MainWindow, AppStrings.AndroidSyncSnackbar(name));
    }

    [RelayCommand]
    private void ReloadPendingAndroidBackup()
    {
        if (string.IsNullOrWhiteSpace(_pendingAndroidBackupPath))
            return;
        var path = _pendingAndroidBackupPath;
        AndroidSyncBannerVisible = false;
        _pendingAndroidBackupPath = null;
        LoadFromPath(path);
        _lastLoadedBackupWriteUtc = File.GetLastWriteTimeUtc(path);
        StatusMessage = AppStrings.AndroidSyncReloaded(Path.GetFileName(path));
        SnackbarService.Show(Wpf.Application.Current.MainWindow, AppStrings.AndroidSyncReloaded(Path.GetFileName(path)));
    }

    [RelayCommand]
    private void DismissAndroidSyncBanner()
    {
        AndroidSyncBannerVisible = false;
        _pendingAndroidBackupPath = null;
    }

    public void RefreshAccountBannerFromSession()
    {
        StoreReviewBannerVisible = MicrosoftTestMode.IsActive;
        if (MicrosoftTestMode.IsActive)
        {
            AccountBannerVisible = false;
            return;
        }

        if (AccountSessionState.ShouldShowWelcomeBanner &&
            !string.IsNullOrWhiteSpace(AccountSessionState.WelcomeBannerMessage))
        {
            AccountBannerMessage = AccountSessionState.WelcomeBannerMessage;
            AccountBannerVisible = true;
            return;
        }

        var summary = AccountSessionState.AccountAccessSummary;
        if (!string.IsNullOrWhiteSpace(summary) && AccountSessionState.LastEntitlement != null)
        {
            AccountBannerMessage = AccountStatusFormatter.FormatAccountBanner(AccountSessionState.LastEntitlement);
            AccountBannerVisible = true;
        }
    }

    /// <summary>Status bar: Android sync folder, subscription, last backup age.</summary>
    public void RefreshFooterStatus()
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        var syncFolder = AndroidSurveySyncService.ResolveSyncFolder(
            settings.AndroidSync.SyncFolderPath,
            string.IsNullOrWhiteSpace(_primarySourcePath) ? null : Path.GetDirectoryName(_primarySourcePath));

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(syncFolder))
        {
            var label = syncFolder;
            if (label.Length > 48)
                label = "…" + label[^45..];
            parts.Add($"Sync: {label}");
        }
        else
        {
            parts.Add("Sync: not configured");
        }

        var zip = AndroidSurveySyncService.TryFindLatestBackupZip(syncFolder);
        if (zip != null)
            parts.Add(FormatFooterBackupAge(DateTime.UtcNow - File.GetLastWriteTimeUtc(zip)));

        var entitlement = AccountSessionState.LastEntitlement;
        if (entitlement?.IsEntitled == true)
            parts.Add(AccountStatusFormatter.FormatAccessSummary(entitlement));
        else if (!string.IsNullOrWhiteSpace(FirebaseAuthSession.CurrentAccountEmail))
            parts.Add("Signed in");
        else
            parts.Add("Not signed in");

        FooterContextLine = string.Join(" · ", parts);
        RefreshAuthStatusChip();
    }

    private void RefreshAuthStatusChip()
    {
        var chips = new List<string>();
        if (!string.IsNullOrWhiteSpace(FirebaseAuthSession.CurrentAccountEmail))
            chips.Add("Signed in");
        else
            chips.Add("Not signed in");

        var entitlement = AccountSessionState.LastEntitlement;
        if (entitlement?.IsEntitled == true)
            chips.Add("Subscription OK");
        else if (!string.IsNullOrWhiteSpace(FirebaseAuthSession.CurrentAccountEmail))
            chips.Add("Subscription required");

        chips.Add(LegalTermsAccepted ? "Legal OK" : "Legal pending");

        var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
        if (token != null)
        {
            var mins = (int)Math.Max(0, (token.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalMinutes);
            chips.Add(mins <= 5 ? $"Token {mins}m" : $"Token ~{mins}m");
        }
        else if (!string.IsNullOrWhiteSpace(FirebaseAuthSession.CurrentAccountEmail))
        {
            chips.Add(FirebaseAuthTokenStore.TryLoadRefreshToken() != null ? "Token refresh pending" : "Token needed");
        }

        AuthStatusChip = string.Join(" · ", chips);
    }

    private static string FormatFooterBackupAge(TimeSpan age)
    {
        if (age.TotalMinutes < 2)
            return "Backup: just now";
        if (age.TotalHours < 1)
            return $"Backup: {(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1)
            return $"Backup: {(int)age.TotalHours}h ago";
        return $"Backup: {(int)age.TotalDays}d ago";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProjectForDesign))]
    private void OpenDesignFromSurvey()
    {
        if (SelectedProject == null)
            return;

        NavigateToDesignFromSurvey?.Invoke(false);
        StatusMessage = AppStrings.DesignFromSurveyStatus;
    }

    private bool HasSelectedProjectForDesign() => SelectedProject != null;

    [RelayCommand]
    private void DismissAccountBanner()
    {
        AccountBannerVisible = false;
        AccountSessionState.DismissWelcomeBanner();
    }

    [RelayCommand]
    private void OpenPlayStoreFromBanner()
    {
        Process.Start(new ProcessStartInfo(AccountLinks.PlayStoreAppUrl) { UseShellExecute = true });
    }

    /// <summary>Called when a new CaveAI_Backup_*.zip appears in the Android sync folder.</summary>
    public void TryAutoReloadAndroidBackup(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return;

        var settings = AppUiSettingsStore.LoadOrDefault().AndroidSync;
        var name = Path.GetFileName(zipPath);
        var owner = Wpf.Application.Current.MainWindow;

        if (!AndroidBackupOpenPrompt.AskOpenBackup(owner, name))
        {
            NotifyAndroidBackupDetected(zipPath);
            DesktopTrayBadgeService.SetBadge(owner, 1);
            return;
        }

        if (TryPromptSameProjectNameConflict(zipPath, name, out var reloadFromConflict))
        {
            if (!reloadFromConflict)
            {
                NotifyAndroidBackupDetected(zipPath);
                return;
            }
        }

        if (!settings.AutoReloadBackupZip)
        {
            LoadFromPath(zipPath);
            _lastLoadedBackupWriteUtc = File.GetLastWriteTimeUtc(zipPath);
            StatusMessage = AppStrings.AndroidSyncReloaded(name);
            SnackbarService.Show(owner, AppStrings.AndroidSyncReloaded(name));
            return;
        }

        if (string.Equals(_primarySourcePath, zipPath, StringComparison.OrdinalIgnoreCase))
        {
            var lastLoaded = _lastLoadedBackupWriteUtc;
            var diskWrite = File.GetLastWriteTimeUtc(zipPath);
            if (lastLoaded.HasValue && diskWrite <= lastLoaded.Value.AddSeconds(1))
                return;
            if (ShouldPromptAndroidSyncConflict(zipPath))
            {
                var choice = AndroidSyncConflictPrompt.Show(owner, name);
                if (choice == AndroidSyncConflictChoice.KeepPcSurvey)
                {
                    NotifyAndroidBackupDetected(zipPath);
                    return;
                }

                if (choice == AndroidSyncConflictChoice.SavePcCopyFirst)
                {
                    ExportAndroidBackupZip();
                    NotifyAndroidBackupDetected(zipPath);
                    return;
                }
            }

            LoadFromPath(zipPath);
            _lastLoadedBackupWriteUtc = File.GetLastWriteTimeUtc(zipPath);
            StatusMessage = AppStrings.AndroidSyncReloaded(name);
            SnackbarService.Show(owner, AppStrings.AndroidSyncReloaded(name));
            return;
        }

        LoadFromPath(zipPath);
        _lastLoadedBackupWriteUtc = File.GetLastWriteTimeUtc(zipPath);
        StatusMessage = AppStrings.AndroidSyncReloaded(name);
        SnackbarService.Show(owner, AppStrings.AndroidSyncReloaded(name));
    }

    private bool TryPromptSameProjectNameConflict(string zipPath, string zipFileName, out bool shouldReload)
    {
        shouldReload = true;
        if (Projects.Count == 0)
            return false;

        var zipWrite = File.GetLastWriteTimeUtc(zipPath);
        if (_lastLoadedBackupWriteUtc.HasValue && zipWrite <= _lastLoadedBackupWriteUtc.Value)
            return false;

        try
        {
            var incoming = ExplorationDataLoader.LoadFromCaveAiBackupZip(zipPath);
            foreach (var project in incoming)
            {
                if (!Projects.Any(p => string.Equals(p.Name, project.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                shouldReload = AndroidBackupOpenPrompt.AskProjectNameConflict(
                    Wpf.Application.Current.MainWindow,
                    project.Name,
                    zipFileName);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private bool ShouldPromptAndroidSyncConflict(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(_loadedSurveyFingerprint) || SelectedProject == null)
            return false;

        var incoming = SurveyContentFingerprint.TryComputeFromBackupZip(zipPath, SelectedProject.Name);
        return !string.IsNullOrWhiteSpace(incoming) &&
               !string.Equals(incoming, _loadedSurveyFingerprint, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>When JSON sync files change, attempt to load the newest backup ZIP in the same folder.</summary>
    public void TryAutoReloadFromSyncFolder(string syncFolder)
    {
        if (string.IsNullOrWhiteSpace(syncFolder))
            return;
        var zip = AndroidSurveySyncService.TryFindLatestBackupZip(syncFolder);
        if (zip != null)
            TryAutoReloadAndroidBackup(zip);
    }

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportAndroidBackupZip()
    {
        if (SelectedProject == null)
            return;
        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Android backup ZIP",
            Filter = "CaveAI backup ZIP|*.zip",
            FileName = $"CaveAI_Backup_{safe}.zip",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            PersistProjectBeforeSave?.Invoke(SelectedProject);
            AndroidBackupZipExporter.ExportSingleProject(
                SelectedProject,
                dlg.FileName,
                ActiveSourceZipForExport,
                PersistProjectBeforeSave,
                _knownCaveMaster);
            if (!HasSourceOnDisk())
            {
                AttachPrimarySourceAfterNewZip(dlg.FileName);
                ClearDirty();
            }
            StatusMessage = "Android backup ZIP saved: " + dlg.FileName;
            SnackbarService.ShowFileSaved(dlg.FileName, "Android backup —");
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportCaveReportPdf()
    {
        if (SelectedProject == null)
            return;
        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export cave report PDF",
            Filter = "PDF|*.pdf",
            FileName = $"{safe}_cave_report.pdf",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            CaveSurveyReportPdfExporter.Export(SelectedProject, dlg.FileName, ActiveZipPathForMaps);
            StatusMessage = "Cave report PDF saved.";
            SnackbarService.Show(Wpf.Application.Current.MainWindow, "Cave report PDF saved.");
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

    [RelayCommand(CanExecute = nameof(CanExportAllProjectsToFolder))]
    private void ExportAllMapsToFolder()
    {
        if (Projects.Count == 0)
            return;
        var dlg = new OpenFolderDialog { Title = "Folder for batch map export (SVG/PNG per project)" };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            var result = SurveyBatchMapExporter.ExportAllMapsToFolder(Projects.ToList(), dlg.FolderName);
            StatusMessage = $"Map batch: {result.FilesWritten} file(s) for {result.ProjectCount} project(s).";
            var msg = result.Errors.Count == 0
                ? $"Wrote {result.FilesWritten} map file(s) to:\n{dlg.FolderName}"
                : $"Wrote {result.FilesWritten} file(s); errors:\n" + string.Join("\n", result.Errors.Take(8));
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, msg, "Map batch export",
                Wpf.MessageBoxButton.OK,
                result.Errors.Count > 0 ? Wpf.MessageBoxImage.Warning : Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportLongProfileSvg()
    {
        if (SelectedProject == null)
            return;
        if (PlanSceneBuilder.TryBuild(SelectedProject, SurveyStationGeometry.AndroidViewModePlan, SurveyVisualizationMode.LongProfile) == null)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "No long profile geometry.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
            return;
        }

        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export long profile SVG",
            Filter = "SVG|*.svg",
            FileName = $"{safe}_longprofile.svg",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            using var fs = File.Create(dlg.FileName);
            SurveySvgExporter.WritePlanSvg(
                SelectedProject,
                fs,
                SurveyStationGeometry.AndroidViewModePlan,
                SurveyVisualizationMode.LongProfile,
                PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan, SurveyVisualizationMode.LongProfile));
            StatusMessage = "Long profile SVG saved.";
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportSurveyBooklet()
    {
        if (SelectedProject == null)
            return;
        var safe = string.Join("_", SelectedProject.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export survey booklet PDF",
            Filter = "PDF|*.pdf",
            FileName = $"{safe}_booklet.pdf",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            CaveSurveyBookletExportService.ExportBooklet(SelectedProject, dlg.FileName);
            StatusMessage = "Survey booklet PDF saved.";
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, "Booklet PDF saved.", "Export", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportWithSelectedProject))]
    private void ExportUnifiedQcReport()
    {
        if (SelectedProject == null)
            return;
        var dlg = new OpenFolderDialog
        {
            Title = "Folder for unified QC report packet (TXT + CSV + integrity)",
        };
        if (dlg.ShowDialog(Wpf.Application.Current.MainWindow) != true)
            return;
        try
        {
            string? manifestSummary = null;
            if (!string.IsNullOrEmpty(_zipPath))
                manifestSummary = BackupManifestReader.TryReadSummary(_zipPath);

            var n = UnifiedQcReportExporter.ExportToFolder(
                SelectedProject,
                dlg.FolderName,
                _integrityReport,
                manifestSummary);
            StatusMessage = $"Unified QC report: {n} file(s) saved.";
            Wpf.MessageBox.Show(
                Wpf.Application.Current.MainWindow,
                $"Saved {n} file(s) to:\n{dlg.FolderName}\n\nIncludes summary TXT, anomalies CSV, loops CSV, and integrity/manifest when a ZIP is loaded.",
                "Export",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(Wpf.Application.Current.MainWindow, ex.Message, "Export failed", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
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

        if (TryNotifyCloudBlockedInTestMode("Publish to Cloud"))
            return;

        if (SelectedProject != null &&
            !CloudPublishChecklistDialog.Confirm(GetOwnerWindow?.Invoke(), SelectedProject, LegalTermsAccepted))
            return;

        if (SelectedProject != null &&
            !await TryConfirmPublishReferenceMatchesAsync(SelectedProject).ConfigureAwait(true))
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
                if (SelectedProject != null)
                    _cloudCommands.ClearRetry(SelectedProject.Name);
            }
            else if (!string.IsNullOrWhiteSpace(lastError))
            {
                SnackbarService.Show(owner, lastError, durationMs: 6000);
                if (SelectedProject != null)
                    _cloudCommands.RecordFailure(SelectedProject.Name, PrimarySourceFilePath, lastError);
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

    private async Task<bool> TryConfirmPublishReferenceMatchesAsync(CaveProjectDocument project)
    {
        try
        {
            var fetch = new ReferenceCatalogFetchService();
            var state = await fetch.LoadBrowseIndexAsync().ConfigureAwait(true);
            return ReferencePublishMatchDialog.ConfirmProceed(
                GetOwnerWindow?.Invoke(),
                project,
                state.IndexEntries);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(
                GetOwnerWindow?.Invoke(),
                "Could not load the reference catalog. Publish was cancelled so the OSM/reference match step is not skipped.\n\n" +
                ex.Message,
                "Reference catalog unavailable",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Warning);
            return ReferencePublishMatchDialog.ShouldProceedWhenCatalogLoadFails();
        }
    }

    private async Task TryPromptReferenceLinksAsync(IReadOnlyList<CaveProjectDocument> projects)
    {
        if (projects.Count == 0)
            return;

        try
        {
            var fetch = new ReferenceCatalogFetchService();
            var state = await fetch.LoadBrowseIndexAsync().ConfigureAwait(true);
            if (state.IndexEntries.Count == 0)
                return;

            ReferenceSurveyLinkPrompt.TryPromptForProjects(
                Wpf.Application.Current.MainWindow,
                projects,
                state.IndexEntries);
        }
        catch
        {
            /* best-effort — offline or cache miss */
        }
    }

    /// <summary>Non-Velopack installs (MSI / portable): dismissible banner when GitHub has a newer build.</summary>
    public async Task CheckUpdateAvailableBannerAsync()
    {
#if DEBUG
        return;
#else
        if (DistributionChannel.UpdatesHandledByStore || AppUpdateService.IsVelopackInstalled())
            return;

        var remote = await AppUpdateService.TryFetchRemoteVersionAsync().ConfigureAwait(true);
        if (remote == null)
            return;

        _pendingReleasePageUrl = remote.ReleasePageUrl;
        UpdateAvailableBannerMessage =
            AppStrings.UpdateAvailableBannerMessage(remote.Version, AppMetadata.InformationalVersion);
        UpdateAvailableBannerVisible = true;
#endif
    }

    [RelayCommand]
    private void OpenPendingReleasePage()
    {
        var url = string.IsNullOrWhiteSpace(_pendingReleasePageUrl)
            ? AppUpdateService.GitHubRepoUrl + "/releases"
            : _pendingReleasePageUrl;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [RelayCommand]
    private void DismissUpdateAvailableBanner() => UpdateAvailableBannerVisible = false;

    internal Task PublishToCloudAsyncInternal() => PublishToCloudAsync();

    [RelayCommand]
    private void OpenSurfaceMapTab() => NavigateToSurfaceTab?.Invoke();
}
