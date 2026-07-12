using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.Legal;
using CaveAiProForWindows.Services.Localization;
using CaveAiProForWindows.Services.Collaboration;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.ViewModels;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows;

public partial class MainWindow : Window
{
    private AndroidBackupZipWatcher? _androidBackupZipWatcher;
    private ProjectAutosaveService? _autosave;
    private PlanView? _planView;
    private PlanView? _model3DView;
    private OfflineXRayView? _xRayView;
    private SurfaceMapView? _surfaceMapView;
    private bool _planTabInitialized;
    private bool _model3DTabInitialized;
    private bool _xRayTabInitialized;
    private bool _surfaceTabInitialized;
    private System.Windows.Threading.DispatcherTimer? _entitlementTimer;
    private DateTime _lastEntitlementCheckUtc = DateTime.MinValue;

    private PlanView? PlanViewControl => _planView;
    private OfflineXRayView? OfflineXRayViewControl => _xRayView;

    public MainWindow()
    {
        InitializeComponent();
        MainSurveyTabControl.SelectionChanged += OnSurveyTabSelectionChanged;
        var vm = new MainViewModel();
        DataContext = vm;
        InputBindings.Add(new KeyBinding(vm.OpenFileCommand, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.SaveProjectCommand, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.CloseWorkspaceCommand, Key.W, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.AboutCommand, new KeyGesture(Key.F1)));
        InputBindings.Add(new KeyBinding(vm.ShowCommandPaletteCommand, Key.K, ModifierKeys.Control));

        AllowDrop = true;
        AddHandler(System.Windows.DragDrop.PreviewDragOverEvent, new System.Windows.DragEventHandler(OnPreviewDragOver), handledEventsToo: true);
        AddHandler(System.Windows.DragDrop.DropEvent, new System.Windows.DragEventHandler(OnDrop), handledEventsToo: true);

        Loaded += (_, _) =>
        {
            WindowPlacementStore.ApplyTo(this);
            ShellLayoutStore.ApplyTo(
                RecentFilesColumn,
                ProjectsColumn,
                MainSurveyTabControl,
                null,
                name =>
                {
                    if (DataContext is MainViewModel vmPick &&
                        vmPick.Projects.FirstOrDefault(p =>
                            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { } match)
                    {
                        vmPick.SelectedProject = match;
                    }
                });
            UiLocalizationService.LoadLanguageFromSettings();
            if (DataContext is MainViewModel vmLoad)
            {
                UiLocalizationService.ApplyToMainWindow(this, vmLoad);
            }
            ApplyLegalDisclaimerDocument();
            SurveyWorkspaceNavigator.Register(this);
            vm.PersistProjectBeforeSave = project =>
            {
                if (ReferenceEquals(vm.SelectedProject, project))
                    SketchEditorControl.TryPersistSessionToProject(project);
            };
            WirePlanViewExportCallbacks(vm);
            vm.CaptureCloudPublishArtifacts = () => SketchEditorControl.TryCaptureCloudPublishArtifacts();
            vm.NavigateToDesignFromSurvey = runProceduralAssist => OpenSketchEditorForDesign(runProceduralAssist);
            vm.NavigateToSurfaceTab = SelectSurfaceTab;
            vm.ResetSurveyViewSurfaces = ResetSurveyViewSurfacesForProjectUnload;
            IntroVideoWindow.ShowIfFirstRun(this);
            WelcomeOnboardingWindow.ShowIfFirstRun(this);
            PostSignInWizardWindow.ShowIfNeeded(this);
            if (DataContext is MainViewModel vmBanner)
            {
                vmBanner.RefreshAccountBannerFromSession();
                vmBanner.RefreshFooterStatus();
            }

            ApplyPreferencesSettingsUi();
            TryOpenPendingExploreMap();

            _entitlementTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30),
            };
            _entitlementTimer.Tick += (_, _) => RefreshEntitlementIfStale(force: true);
            _entitlementTimer.Start();
            Activated += (_, _) => RefreshEntitlementIfStale();
            _autosave = new ProjectAutosaveService();
            _autosave.Configure(
                () => vm.SelectedProject,
                () => vm.PrimarySourceFilePath,
                p => vm.PersistProjectBeforeSave?.Invoke(p));
            StartAndroidBackupSyncWatcher(vm);
            StartCollaborationNotifications(vm);
            ApplyPlanViewLocalization();
            ApplyReferencePinsSettingUi();
            ApplySurveyTabGroup(SurveyTabGroup.Survey);
            AndroidDesktopSyncHub.SyncSettingsChanged += OnAndroidSyncSettingsChanged;
            AndroidDesktopSyncHub.CollaborationProjectChanged += OnCollaborationProjectChanged;
            AndroidDesktopSyncHub.SyncFilesChanged += OnAndroidSyncFilesChanged;
        };
        Closing += (_, _) =>
        {
            AndroidDesktopSyncHub.SyncSettingsChanged -= OnAndroidSyncSettingsChanged;
            AndroidDesktopSyncHub.SyncFilesChanged -= OnAndroidSyncFilesChanged;
            AndroidDesktopSyncHub.CollaborationProjectChanged -= OnCollaborationProjectChanged;
            _androidBackupZipWatcher?.Dispose();
            _androidBackupZipWatcher = null;
            _autosave?.Dispose();
            _autosave = null;
            _collaborationNotifications?.Dispose();
            _collaborationNotifications = null;
            _entitlementTimer?.Stop();
            _entitlementTimer = null;
            if (DataContext is MainViewModel vmClose)
            {
                var layout = ShellLayoutStore.CaptureFrom(
                    RecentFilesColumn,
                    ProjectsColumn,
                    MainSurveyTabControl,
                    vmClose.SelectedProject?.Name);
                ShellLayoutStore.Save(layout);
            }

            WindowPlacementStore.SaveFrom(this);
        };
        PreviewKeyDown += OnMainWindowPreviewKeyDown;
        App.WriteStartupLog("MainWindow constructed and Loaded wiring attached");
    }

    private void OnSurveyTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainSurveyTabControl?.SelectedItem is not TabItem tab)
            return;

        if (ReferenceEquals(tab, PlanTab))
            EnsurePlanTab();
        else if (ReferenceEquals(tab, Model3DTab))
            EnsureModel3DTab();
        else if (ReferenceEquals(tab, XRayTab))
            EnsureXRayTab();
        else if (ReferenceEquals(tab, SurfaceTab))
        {
            EnsureSurfaceTab();
            _surfaceMapView?.ReloadLayersFromSettings();
        }
    }

    private void EnsurePlanTab()
    {
        if (_planTabInitialized)
            return;

        _planView = CreateBoundPlanView(SurveyVisualizationMode.Standard);
        PlanTabHost.Content = _planView;
        _planTabInitialized = true;
        if (DataContext is MainViewModel vm)
            WirePlanViewExportCallbacks(vm);
        UiLocalizationService.ApplyToPlanView3DTools(_planView);
    }

    private void EnsureModel3DTab()
    {
        if (_model3DTabInitialized)
            return;

        _model3DView = CreateBoundPlanView(SurveyVisualizationMode.Pseudo3D);
        Model3DTabHost.Content = _model3DView;
        _model3DTabInitialized = true;
        UiLocalizationService.ApplyToPlanView3DTools(_model3DView);
        MaybeShowFirstOpen3DTooltip();
    }

    private void MaybeShowFirstOpen3DTooltip()
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        if (settings.HasSeen3DCompetitiveTooltip)
            return;
        settings.HasSeen3DCompetitiveTooltip = true;
        AppUiSettingsStore.Save(settings);
        MessageBox.Show(
            this,
            "Tip: Use the 3D toolbar preset «Competitive 3D» for a clean labeled overview, then export PNG or open Tools → Publication sheet for print layouts.",
            "3D MODEL tab",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void EnsureXRayTab()
    {
        if (_xRayTabInitialized)
            return;

        _xRayView = new OfflineXRayView
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _xRayView.SetBinding(OfflineXRayView.ProjectProperty,
            new System.Windows.Data.Binding("DataContext.SelectedProject") { ElementName = "Shell" });
        _xRayView.SetBinding(OfflineXRayView.ZipPathProperty,
            new System.Windows.Data.Binding("DataContext.ActiveZipPathForMaps") { ElementName = "Shell" });
        _xRayView.SetBinding(OfflineXRayView.MapInventoryProperty,
            new System.Windows.Data.Binding("DataContext.MapInventoryRows") { ElementName = "Shell" });
        XRayTabHost.Content = _xRayView;
        _xRayTabInitialized = true;
    }

    private void EnsureSurfaceTab()
    {
        if (_surfaceTabInitialized)
            return;

        _surfaceMapView = new SurfaceMapView
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _surfaceMapView.SetBinding(SurfaceMapView.ProjectProperty,
            new System.Windows.Data.Binding("DataContext.SelectedProject") { ElementName = "Shell" });
        _surfaceMapView.SetBinding(SurfaceMapView.ZipPathProperty,
            new System.Windows.Data.Binding("DataContext.ActiveZipPathForMaps") { ElementName = "Shell" });
        _surfaceMapView.SetBinding(SurfaceMapView.CloudAssetCacheDirProperty,
            new System.Windows.Data.Binding("DataContext.CloudAssetCacheDir") { ElementName = "Shell" });
        SurfaceTabHost.Content = _surfaceMapView;
        _surfaceTabInitialized = true;
    }

    private PlanView CreateBoundPlanView(SurveyVisualizationMode mode)
    {
        var view = new PlanView
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VisualizationMode = mode,
        };
        view.SetBinding(PlanView.ProjectProperty,
            new System.Windows.Data.Binding("DataContext.SelectedProject") { ElementName = "Shell" });
        view.SetBinding(PlanView.ZipPathProperty,
            new System.Windows.Data.Binding("DataContext.ActiveZipPathForMaps") { ElementName = "Shell" });
        view.SetBinding(PlanView.MapRowsProperty,
            new System.Windows.Data.Binding("DataContext.MapAssetRows") { ElementName = "Shell" });
        view.SetBinding(PlanView.MapInventoryProperty,
            new System.Windows.Data.Binding("DataContext.MapInventoryRows") { ElementName = "Shell" });
        return view;
    }

    private void WirePlanViewExportCallbacks(MainViewModel vm)
    {
        if (_planView == null)
            return;
        _planView.BeforePlanExport = project =>
        {
            if (ReferenceEquals(vm.SelectedProject, project))
                SketchEditorControl.TryPersistSessionToProject(project);
        };
        _planView.ResolveDesignLayerForExport = () =>
        {
            if (vm.SelectedProject == null)
                return null;
            return SketchEditorControl.TryGetDesignLayerExportContext();
        };
    }

    private void ApplyReferencePinsSettingUi()
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        if (ShowReferencePinsSetting != null)
            ShowReferencePinsSetting.IsChecked = settings.ShowReferencePinsOnXRay;
        if (ShowReferencePinsOnPlanSetting != null)
            ShowReferencePinsOnPlanSetting.IsChecked = settings.ShowReferencePinsOnPlan;
    }

    private void ShowReferencePinsSetting_Changed(object sender, RoutedEventArgs e)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        settings.ShowReferencePinsOnXRay = ShowReferencePinsSetting?.IsChecked == true;
        AppUiSettingsStore.Save(settings);
    }

    private void ShowReferencePinsOnPlanSetting_Changed(object sender, RoutedEventArgs e)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        settings.ShowReferencePinsOnPlan = ShowReferencePinsOnPlanSetting?.IsChecked == true;
        AppUiSettingsStore.Save(settings);
    }

    private void ApplyPreferencesSettingsUi()
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        if (PreferencesSyncFolderBox != null)
            SetPreferencesPathBox(PreferencesSyncFolderBox, settings.AndroidSync.SyncFolderPath, optional: false);
        if (PreferencesExportFolderBox != null)
            SetPreferencesPathBox(PreferencesExportFolderBox, settings.DefaultExportFolderPath, optional: true);
        if (PreferencesDarkThemeCheck != null)
            PreferencesDarkThemeCheck.IsChecked = settings.UseDarkTheme;
        if (PreferencesTelemetryCheck != null)
            PreferencesTelemetryCheck.IsChecked = settings.SendAnonymizedErrorReports;
        if (PreferencesMapQualityCombo != null)
        {
            foreach (ComboBoxItem item in PreferencesMapQualityCombo.Items)
            {
                if (item.Tag is string tag &&
                    string.Equals(tag, settings.MapExportQuality, StringComparison.OrdinalIgnoreCase))
                {
                    PreferencesMapQualityCombo.SelectedItem = item;
                    break;
                }
            }
        }

        RefreshPreferencesLastErrorLine();
        RefreshPreferencesPublishHistoryLine();
    }

    private void RefreshPreferencesLastErrorLine()
    {
        if (PreferencesLastErrorText == null)
            return;
        var summary = ClientErrorTelemetryService.LastErrorSummary;
        PreferencesLastErrorText.Text = string.IsNullOrWhiteSpace(summary)
            ? "No recent in-app error recorded this session."
            : summary;
    }

    private void RefreshPreferencesPublishHistoryLine()
    {
        if (PreferencesPublishHistoryText == null || DataContext is not MainViewModel vm || vm.SelectedProject == null)
        {
            if (PreferencesPublishHistoryText != null)
                PreferencesPublishHistoryText.Text = "Publish history: select a project to see local publish log.";
            return;
        }

        var entries = vm.CloudCommands.HistoryForProject(vm.SelectedProject.Name);
        if (entries.Count == 0)
        {
            PreferencesPublishHistoryText.Text = "Publish history: none recorded for this project on this PC.";
            return;
        }

        var latest = entries[0];
        PreferencesPublishHistoryText.Text =
            $"Publish history: last {latest.PublishedAtUtc:yyyy-MM-dd HH:mm} UTC · doc {latest.PublishedDocId}" +
            (entries.Count > 1 ? $" (+{entries.Count - 1} earlier)" : "");
    }

    private void PreferencesBrowseSyncFolder_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        var dlg = new OpenFolderDialog
        {
            Title = "Android Desktop Sync folder",
            FolderName = Directory.Exists(settings.AndroidSync.SyncFolderPath ?? "")
                ? settings.AndroidSync.SyncFolderPath
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dlg.ShowDialog(this) != true)
            return;
        if (!Directory.Exists(dlg.FolderName))
        {
            MessageBox.Show(this, "The selected folder does not exist.", "Android sync folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        settings.AndroidSync.SyncFolderPath = dlg.FolderName;
        AppUiSettingsStore.Save(settings);
        if (PreferencesSyncFolderBox != null)
            SetPreferencesPathBox(PreferencesSyncFolderBox, dlg.FolderName, optional: false);
        if (DataContext is MainViewModel vm)
            vm.RefreshFooterStatus();
    }

    private void PreferencesBrowseExportFolder_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        var dlg = new OpenFolderDialog
        {
            Title = "Default export folder",
            FolderName = Directory.Exists(settings.DefaultExportFolderPath ?? "")
                ? settings.DefaultExportFolderPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != true)
            return;
        if (!Directory.Exists(dlg.FolderName))
        {
            MessageBox.Show(this, "The selected folder does not exist.", "Export folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        settings.DefaultExportFolderPath = dlg.FolderName;
        AppUiSettingsStore.Save(settings);
        if (PreferencesExportFolderBox != null)
            SetPreferencesPathBox(PreferencesExportFolderBox, dlg.FolderName, optional: true);
    }

    private static void SetPreferencesPathBox(System.Windows.Controls.TextBox box, string? path, bool optional)
    {
        var display = string.IsNullOrWhiteSpace(path)
            ? optional ? "(same as backup file)" : "(not set)"
            : path;
        box.Text = display;
        var valid = optional && string.IsNullOrWhiteSpace(path) || (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
        box.Foreground = valid
            ? (System.Windows.Media.Brush)box.FindResource("Cave.Text")
            : (System.Windows.Media.Brush)box.FindResource("Cave.Accent");
        box.ToolTip = valid
            ? display
            : $"{display} — folder not found on this PC";
    }

    private void PreferencesMapQualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreferencesMapQualityCombo?.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;
        var settings = AppUiSettingsStore.LoadOrDefault();
        settings.MapExportQuality = tag;
        AppUiSettingsStore.Save(settings);
    }

    private void PreferencesDarkThemeCheck_Changed(object sender, RoutedEventArgs e)
    {
        ThemePaletteSwitcher.SetDarkTheme(PreferencesDarkThemeCheck?.IsChecked == true);
    }

    private void PreferencesTelemetryCheck_Changed(object sender, RoutedEventArgs e)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        settings.SendAnonymizedErrorReports = PreferencesTelemetryCheck?.IsChecked == true;
        AppUiSettingsStore.Save(settings);
    }

    private void PreferencesCopyLastError_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        var text = ClientErrorTelemetryService.LastErrorSummary ?? "(no error this session)";
        text += Environment.NewLine + $"Crash-free sessions: {settings.CrashFreeSessionCount}";
        text += Environment.NewLine + $"App: {AppMetadata.InformationalVersion}";
        try
        {
            Clipboard.SetText(text);
            SnackbarService.Show(this, "Copied error summary for support.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TryOpenPendingExploreMap()
    {
        var url = App.PendingExploreMapUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;
        App.PendingExploreMapUrl = null;
        try
        {
            PublicLibraryCatalog.RememberExploreMapViewportUrl(url);
            PublicLibraryCatalog.ShowInAppWindow(this, url);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Explore map", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject? ancestor)
    {
        while (child != null)
        {
            if (ReferenceEquals(child, ancestor))
                return true;
            child = VisualTreeHelper.GetParent(child);
        }

        return false;
    }

    private bool IsKeyboardFocusWithinSurveyTabs() =>
        MainSurveyTabControl != null &&
        Keyboard.FocusedElement is DependencyObject dep &&
        IsDescendantOf(dep, MainSurveyTabControl);

    private static bool IsTextInputFocused() =>
        Keyboard.FocusedElement is TextBoxBase;

    private IMapSurfaceShortcuts? TryResolveFocusedMapSurface()
    {
        if (Keyboard.FocusedElement is not DependencyObject dep)
            return null;
        for (var o = dep; o != null; o = VisualTreeHelper.GetParent(o))
        {
            if (o is PlanView pv && pv.VisualizationMode != SurveyVisualizationMode.Pseudo3D)
                return pv;
            if (o is SketchEditorView sk)
                return sk;
            if (o is OfflineXRayView xray)
                return xray;
            if (o is SectionView sec)
                return sec;
        }

        return null;
    }

    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        {
            if (DataContext is MainViewModel vmPalette && vmPalette.ShowCommandPaletteCommand.CanExecute(null))
                vmPalette.ShowCommandPaletteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!IsKeyboardFocusWithinSurveyTabs())
            return;

        if (e.Key == Key.Escape)
        {
            if (IsTextInputFocused())
                return;
            var surfaceEsc = TryResolveFocusedMapSurface();
            if (surfaceEsc == null)
                return;
            surfaceEsc.ClearMapSelectionAndRedraw();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && !IsTextInputFocused())
        {
            var surfaceDel = TryResolveFocusedMapSurface();
            if (surfaceDel?.TryDeleteSelectedInk() == true)
            {
                e.Handled = true;
                return;
            }
        }

        if (Keyboard.Modifiers == ModifierKeys.None && !IsTextInputFocused())
        {
            var surfacePlain = TryResolveFocusedMapSurface();
            if (surfacePlain != null && e.Key == Key.W)
            {
                surfacePlain.ApplyMapEditorTool(MapCanvasEditorTool.DrawLine);
                e.Handled = true;
                return;
            }
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        if (IsTextInputFocused())
            return;

        var surface = TryResolveFocusedMapSurface();
        if (surface == null)
            return;

        switch (e.Key)
        {
            case Key.Z:
                surface.UndoSketchEdit();
                e.Handled = true;
                break;
            case Key.Y:
                surface.RedoSketchEdit();
                e.Handled = true;
                break;
            case Key.D:
                if (surface.TryDuplicateSelectedInk())
                    e.Handled = true;
                break;
            case Key.F:
                surface.FitMapToSurveyBounds();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.PanZoom);
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.Select);
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.DrawFreehand);
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.PlaceSymbol);
                e.Handled = true;
                break;
            case Key.D5:
            case Key.NumPad5:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.Erase);
                e.Handled = true;
                break;
            case Key.D6:
            case Key.NumPad6:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.DrawLine);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                surface.ResetMapView();
                e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                surface.MapZoomIn();
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                surface.MapZoomOut();
                e.Handled = true;
                break;
        }
    }

    private void IntegrityBanner_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IntegrityTabItem != null)
            IntegrityTabItem.IsSelected = true;
    }

    private async void SwitchGoogleAccount_Click(object sender, RoutedEventArgs e)
    {
        if (MicrosoftTestMode.IsActive)
        {
            MessageBox.Show(
                this,
                MicrosoftTestMode.CloudFeatureBlockedMessage,
                "Switch Google account",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "Sign out of the current Google account and open the sign-in screen again?\n\n" +
            "An active CaveAI Pro subscription or trial is still required.",
            "Switch Google account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        await FirebaseAuthSession.SignOutAsync().ConfigureAwait(true);
        var ok = AppLockBootstrapper.TryEnsureUnlocked();
        if (!ok)
        {
            Application.Current.Shutdown();
            return;
        }

        if (DataContext is MainViewModel vm)
            vm.RefreshAccountBannerFromSession();
    }

    private void OpenPublicLibraryToolbar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.OpenPublicLibraryCatalogCommand.CanExecute(null))
            vm.OpenPublicLibraryCatalogCommand.Execute(null);
        else
            OpenPublicLibraryInAppFallback();
    }

    private void OpenPublicLibraryInAppFallback()
    {
        try
        {
            PublicLibraryCatalog.ShowMapInAppWindow(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Public Cave Library", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ReplayIntroVideo_Click(object sender, RoutedEventArgs e) =>
        IntroVideoWindow.ShowReplay(this);

    private void OpenPublicLibraryWebView_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PublicLibraryCatalog.ShowMapInAppWindow(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Public Cave Library", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenExploreMapWebView_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = PublicLibraryCatalog.ResolveExploreMapOpenUrl();
            PublicLibraryCatalog.ShowInAppWindow(this, url);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Explore map", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenPublicLibraryExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PublicLibraryCatalog.OpenMap();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Public Cave Library", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenFollowingPublishers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new FollowingPublishersWindow { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Following", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenSharedFieldTripLink_Click(object sender, RoutedEventArgs e)
    {
        var pasted = ShareUrlPrompt.Show(this, "Field trip share link", "Paste a caveaipro.com field trip URL:");
        if (string.IsNullOrWhiteSpace(pasted))
            return;

        var payload = Services.FieldTrip.FieldTripShareCodec.TryParseFromUrl(pasted);
        if (payload == null)
        {
            System.Windows.MessageBox.Show(this, "Could not parse field trip link.", "Field trip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var store = Services.FieldTrip.FieldTripStore.Load();
        var trip = store.Trips.FirstOrDefault() ?? new Models.FieldTripDocument { Name = "Imported trip" };
        if (!store.Trips.Contains(trip))
            store.Trips.Add(trip);
        trip.Stops.Clear();
        trip.Stops.AddRange(Services.FieldTrip.FieldTripShareCodec.ToFieldTripStops(payload));
        Services.FieldTrip.FieldTripStore.Upsert(trip);
        FieldTripPlannerWindow.Show(this);
    }

    private void OpenDataInspector_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.LegalTermsAccepted)
        {
            MessageBox.Show(
                this,
                "Please open the LEGAL & SETTINGS tab and accept the terms and conditions before using the Data & backup inspector.",
                "Terms required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SelectLegalSettingsTab();
            return;
        }

        var w = new DataInspectorWindow
        {
            Owner = this,
            DataContext = DataContext,
        };
        w.Show();
    }

    public void SelectLegalSettingsTab()
    {
        ApplySurveyTabGroup(SurveyTabGroup.Settings);
        if (LegalSettingsTabItem != null)
            LegalSettingsTabItem.IsSelected = true;
    }

    public void SelectSurfaceTab()
    {
        ApplySurveyTabGroup(SurveyTabGroup.Survey);
        if (SurfaceTab != null)
            MainSurveyTabControl.SelectedItem = SurfaceTab;
        EnsureSurfaceTab();
        _surfaceMapView?.ReloadLayersFromSettings();
    }

    private async void AuthStatusChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var chip = vm.AuthStatusChip ?? "";
        if (chip.Contains("Legal pending", StringComparison.OrdinalIgnoreCase))
        {
            SelectLegalSettingsTab();
            return;
        }

        if (chip.Contains("Not signed in", StringComparison.OrdinalIgnoreCase) ||
            chip.Contains("Subscription required", StringComparison.OrdinalIgnoreCase))
        {
            if (MicrosoftTestMode.IsActive)
            {
                MessageBox.Show(
                    this,
                    MicrosoftTestMode.CloudFeatureBlockedMessage,
                    "Sign in",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var ok = AppLockBootstrapper.TryEnsureUnlocked();
            if (!ok)
            {
                Application.Current.Shutdown();
                return;
            }

            vm.RefreshAccountBannerFromSession();
            vm.RefreshFooterStatus();
        }
    }

    private void LegalRequiredOverlay_GoToLegal_Click(object sender, RoutedEventArgs e) => SelectLegalSettingsTab();

    private static bool IsSurveyBackupFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".json" or ".zip";
    }

    private void OnPreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files &&
            files.Any(static f =>
                !string.IsNullOrEmpty(f) &&
                (IsSurveyBackupFile(f) || StandaloneMapFileSupport.IsStandaloneMapFile(f))))
            e.Effects = System.Windows.DragDropEffects.Copy;
        else
            e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files || DataContext is not MainViewModel vm)
        {
            e.Handled = true;
            return;
        }

        var existing = files
            .Where(static f => !string.IsNullOrEmpty(f) && File.Exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var surveys = existing.Where(IsSurveyBackupFile).ToList();
        var maps = existing.Where(StandaloneMapFileSupport.IsStandaloneMapFile).ToList();

        if (surveys.Count > 0)
            vm.LoadFromPaths(surveys);

        if (maps.Count > 0)
        {
            var n = vm.AddStandaloneMapPaths(maps);
            if (n > 0 && surveys.Count == 0)
                vm.StatusMessage = n == 1
                    ? "Added 1 standalone map — use File → Export → Maps report (CSV) to list paths."
                    : $"Added {n} standalone maps — use File → Export → Maps report (CSV) to list paths.";
        }

        e.Handled = true;
    }

    private void ExportProjectWithPlanZip_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (!vm.LegalTermsAccepted)
        {
            MessageBox.Show(
                this,
                "Please open the LEGAL & SETTINGS tab and accept the terms and conditions before exporting.",
                "Terms required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SelectLegalSettingsTab();
            return;
        }

        var project = vm.SelectedProject;
        if (project == null)
        {
            MessageBox.Show(
                "Select a project in the list first.",
                "Export ZIP",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var seg = string.Join("_", (project.Name ?? "cave").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim('_');
        if (seg.Length == 0)
            seg = "cave";
        if (seg.Length > 60)
            seg = seg[..60].TrimEnd('_');

        var dlg = new SaveFileDialog
        {
            Title = "Export project to ZIP",
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = $"{seg}_CaveAiPro_export.zip",
            AddExtension = true,
            DefaultExt = ".zip",
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            SketchEditorControl.TryPersistSessionToProject(project);
            EnsurePlanTab();
            var png = PlanViewControl?.CapturePlanPngBytes() ?? Array.Empty<byte>();
            SurveyPortableZipExporter.WriteZip(project, png, dlg.FileName);
            vm.StatusMessage = $"Exported ZIP: {dlg.FileName}";
            MessageBox.Show(
                "Saved:\r\n• data.json — one project (Android-compatible Gson shape)\r\n• plan_view.png — PLAN tab snapshot\r\n• README.txt",
                "Export ZIP",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export ZIP failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyLegalDisclaimerDocument()
    {
        if (LegalPublisherMetaText != null)
        {
            LegalPublisherMetaText.Text =
                $"Publisher: {LegalTexts.PublisherName} · {LegalTexts.ContactEmail} · " +
                $"Document v{LegalTexts.DocumentVersion} ({LegalTexts.LastUpdated}). " +
                "Android and web editions use the same Developer and core safety terms.";
        }

        if (LegalDisclaimerRichText == null)
            return;
        LegalRichTextFormatter.ApplyPlainText(LegalDisclaimerRichText, LegalTexts.FullDisclaimerAndEula);
    }

    /// <summary>Focus PLAN tab and zoom X-Ray / Plan to the given station (called from navigator hub).</summary>
    public void FocusStationOnWorkspace(string stationName)
    {
        if (string.IsNullOrWhiteSpace(stationName))
            return;

        if (MainSurveyTabControl != null)
        {
            MainSurveyTabControl.SelectedIndex = 0;
            EnsurePlanTab();
        }

        PlanViewControl?.ApplyExternalStationSelection(stationName);
        PlanViewControl?.ZoomToStation(stationName);
        OfflineXRayViewControl?.ZoomToStation(stationName);
    }

    public void FocusGeoBioTab()
    {
        if (MainSurveyTabControl != null)
            MainSurveyTabControl.SelectedIndex = 4;
    }

    /// <summary>Select SKETCH EDITOR and optionally run procedural LRUD assist.</summary>
    public void OpenSketchEditorForDesign(bool runProceduralAssist = false)
    {
        if (MainSurveyTabControl != null && SketchEditorTabItem != null)
            MainSurveyTabControl.SelectedItem = SketchEditorTabItem;

        SketchEditorControl.BeginDesignFromSurvey(runProceduralAssist);
    }

    private void ResetSurveyViewSurfacesForProjectUnload()
    {
        _surfaceMapView?.ResetForProjectUnload();

        SketchEditorControl.ResetForProjectUnload();

        foreach (var plan in FindVisualChildren<PlanView>(this))
        {
            plan.ResetMapView();
            plan.ClearMapSelectionAndRedraw();
        }

        foreach (var section in FindVisualChildren<SectionView>(this))
            section.ResetMapView();

        _xRayView?.ResetMapView();
        _xRayView?.ClearMapSelectionAndRedraw();
    }

    public void ApplyPlanViewLocalization()
    {
        foreach (var plan in FindVisualChildren<PlanView>(this))
            UiLocalizationService.ApplyToPlanView3DTools(plan);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null)
            yield break;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private void OnCollaborationProjectChanged(object? sender, string projectId)
    {
        if (DataContext is MainViewModel vm)
            Dispatcher.BeginInvoke(() => StartCollaborationNotifications(vm));
    }

    private void StartAndroidBackupSyncWatcher(MainViewModel vm)
    {
        _androidBackupZipWatcher?.Dispose();
        var syncFolder = AndroidSurveySyncService.ResolveSyncFolder(
            AppUiSettingsStore.LoadOrDefault().AndroidSync.SyncFolderPath,
            null);
        if (string.IsNullOrWhiteSpace(syncFolder))
            syncFolder = AndroidSurveySyncService.DefaultSyncFolderCandidates().FirstOrDefault(Directory.Exists);

        if (string.IsNullOrWhiteSpace(syncFolder))
            return;

        _androidBackupZipWatcher = new AndroidBackupZipWatcher();
        _androidBackupZipWatcher.BackupZipChanged += (_, e) =>
        {
            Dispatcher.BeginInvoke(() => vm.TryAutoReloadAndroidBackup(e.ZipPath));
        };
        _androidBackupZipWatcher.Watch(syncFolder);
    }

    private void OnAndroidSyncSettingsChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            Dispatcher.BeginInvoke(() =>
            {
                StartAndroidBackupSyncWatcher(vm);
                vm.RefreshFooterStatus();
            });
    }

    private void OnAndroidSyncFilesChanged(object? sender, AndroidSyncFilesChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        Dispatcher.BeginInvoke(() => vm.TryAutoReloadFromSyncFolder(e.SyncFolder));
    }

    private CollaborationNotificationService? _collaborationNotifications;

    private void StartCollaborationNotifications(MainViewModel vm)
    {
        _collaborationNotifications?.Dispose();
        _collaborationNotifications = new CollaborationNotificationService();
        _collaborationNotifications.UnreadCountChanged += (_, count) =>
        {
            vm.CollaborationUnreadCount = count;
            DesktopTrayBadgeService.SetBadge(this, count);
        };
        var settings = AppUiSettingsStore.LoadOrDefault();
        _collaborationNotifications.TrackProject(settings.CollaborationSharedProjectId);
    }

    private async void RefreshEntitlementIfStale(bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastEntitlementCheckUtc < TimeSpan.FromMinutes(5))
            return;

        _lastEntitlementCheckUtc = DateTime.UtcNow;
        if (MicrosoftTestMode.IsActive)
            return;

        try
        {
            var result = await AccountSessionState.RefreshEntitlementAsync().ConfigureAwait(true);
            if (result == null || DataContext is not MainViewModel vm)
                return;

            vm.RefreshAccountBannerFromSession();
            vm.RefreshFooterStatus();
        }
        catch
        {
            /* offline — keep last known entitlement */
        }
    }

    private enum SurveyTabGroup { Survey, Library, Publish, Settings }

    private void TabGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender == TabGroupSurvey)
            ApplySurveyTabGroup(SurveyTabGroup.Survey);
        else if (sender == TabGroupLibrary)
            ApplySurveyTabGroup(SurveyTabGroup.Library);
        else if (sender == TabGroupPublish)
            ApplySurveyTabGroup(SurveyTabGroup.Publish);
        else if (sender == TabGroupSettings)
            ApplySurveyTabGroup(SurveyTabGroup.Settings);
    }

    private void ApplySurveyTabGroup(SurveyTabGroup group)
    {
        TabGroupSurvey.IsChecked = group == SurveyTabGroup.Survey;
        TabGroupLibrary.IsChecked = group == SurveyTabGroup.Library;
        TabGroupPublish.IsChecked = group == SurveyTabGroup.Publish;
        TabGroupSettings.IsChecked = group == SurveyTabGroup.Settings;

        var surveyTabs = new HashSet<TabItem>();
        foreach (var item in MainSurveyTabControl.Items.OfType<TabItem>())
        {
            if (item == IntegrityTabItem || item.Header?.ToString() == "SURVEY QC")
                continue;
            if (item == LegalSettingsTabItem)
                continue;
            surveyTabs.Add(item);
        }

        var libraryTabs = new HashSet<TabItem> { IntegrityTabItem };
        foreach (var item in MainSurveyTabControl.Items.OfType<TabItem>())
        {
            if (item.Header?.ToString() == "SURVEY QC")
                libraryTabs.Add(item);
        }

        foreach (var item in MainSurveyTabControl.Items.OfType<TabItem>())
        {
            item.Visibility = group switch
            {
                SurveyTabGroup.Survey => surveyTabs.Contains(item) ? Visibility.Visible : Visibility.Collapsed,
                SurveyTabGroup.Library => libraryTabs.Contains(item) ? Visibility.Visible : Visibility.Collapsed,
                SurveyTabGroup.Publish => Visibility.Collapsed,
                SurveyTabGroup.Settings => item == LegalSettingsTabItem ? Visibility.Visible : Visibility.Collapsed,
                _ => Visibility.Visible,
            };
        }

        if (group == SurveyTabGroup.Publish)
        {
            SnackbarService.Show(this,
                "Cloud publish: Tools → Push to Cloud, or Ctrl+K → Push to Cloud.",
                durationMs: 5000);
            TabGroupSurvey.IsChecked = true;
            ApplySurveyTabGroup(SurveyTabGroup.Survey);
            return;
        }

        var visible = MainSurveyTabControl.Items.OfType<TabItem>()
            .FirstOrDefault(t => t.Visibility == Visibility.Visible);
        if (visible != null && MainSurveyTabControl.SelectedItem is TabItem sel && sel.Visibility != Visibility.Visible)
            MainSurveyTabControl.SelectedItem = visible;
    }
}
