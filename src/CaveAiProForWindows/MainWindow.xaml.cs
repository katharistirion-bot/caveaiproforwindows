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

        AllowDrop = true;
        AddHandler(System.Windows.DragDrop.PreviewDragOverEvent, new System.Windows.DragEventHandler(OnPreviewDragOver), handledEventsToo: true);
        AddHandler(System.Windows.DragDrop.DropEvent, new System.Windows.DragEventHandler(OnDrop), handledEventsToo: true);

        Loaded += (_, _) =>
        {
            WindowPlacementStore.ApplyTo(this);
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
            IntroVideoWindow.ShowIfFirstRun(this);
            WelcomeOnboardingWindow.ShowIfFirstRun(this);
            PostSignInWizardWindow.ShowIfNeeded(this);
            if (DataContext is MainViewModel vmBanner)
            {
                vmBanner.RefreshAccountBannerFromSession();
                vmBanner.RefreshFooterStatus();
            }

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
            EnsureSurfaceTab();
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
            PublicLibraryCatalog.RememberExploreMapViewportUrl(url);
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
        if (LegalSettingsTabItem != null)
            LegalSettingsTabItem.IsSelected = true;
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
}
