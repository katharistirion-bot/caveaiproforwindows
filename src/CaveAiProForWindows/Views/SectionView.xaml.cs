using System;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

public partial class SectionView : System.Windows.Controls.UserControl, IMapSurfaceShortcuts, ISurveyMapPrintSurface
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(SectionView),
        new PropertyMetadata(null, (d, _) => ((SectionView)d).Redraw()));

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(SectionView),
        new PropertyMetadata(null, (d, _) => ((SectionView)d).Redraw()));

    public static readonly DependencyProperty MapRowsProperty = DependencyProperty.Register(
        nameof(MapRows),
        typeof(IEnumerable),
        typeof(SectionView),
        new PropertyMetadata(null, OnMapRowsChanged));

    public static readonly DependencyProperty MapInventoryProperty = DependencyProperty.Register(
        nameof(MapInventory),
        typeof(IEnumerable),
        typeof(SectionView),
        new PropertyMetadata(null, OnMapInventoryChanged));

    public static readonly DependencyProperty VisualizationModeProperty = DependencyProperty.Register(
        nameof(VisualizationMode),
        typeof(SurveyVisualizationMode),
        typeof(SectionView),
        new PropertyMetadata(SurveyVisualizationMode.Standard, OnVisualizationModeChanged));

    private static void OnVisualizationModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SectionView)d;
        if (!v.IsLoaded)
            return;
        v.ZoomPan.X = 0;
        v.ZoomPan.Y = 0;
        v.ZoomScale.ScaleX = 1;
        v.ZoomScale.ScaleY = 1;
        v.FitMapSurfaceToHost();
        v.Redraw();
        v.PersistSectionTab();
    }

    private static void OnMapRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SectionView)d;
        v.UnwireMapRows(e.OldValue);
        v.WireMapRows(e.NewValue);
        v.Redraw();
    }

    private static void OnMapInventoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SectionView)d;
        v.UnwireMapInventory(e.OldValue);
        v.WireMapInventory(e.NewValue);
        v.Redraw();
    }

    private INotifyCollectionChanged? _wiredMapRows;
    private INotifyCollectionChanged? _wiredMapInventory;

    private void WireMapRows(object? value)
    {
        if (value is INotifyCollectionChanged n)
        {
            _wiredMapRows = n;
            n.CollectionChanged += MapRows_CollectionChanged;
        }
    }

    private void UnwireMapRows(object? value)
    {
        if (value is INotifyCollectionChanged n)
        {
            n.CollectionChanged -= MapRows_CollectionChanged;
            if (ReferenceEquals(n, _wiredMapRows))
                _wiredMapRows = null;
        }
    }

    private void MapRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void WireMapInventory(object? value)
    {
        if (value is INotifyCollectionChanged n)
        {
            _wiredMapInventory = n;
            n.CollectionChanged += MapInventory_CollectionChanged;
        }
    }

    private void UnwireMapInventory(object? value)
    {
        if (value is INotifyCollectionChanged n)
        {
            n.CollectionChanged -= MapInventory_CollectionChanged;
            if (ReferenceEquals(n, _wiredMapInventory))
                _wiredMapInventory = null;
        }
    }

    private void MapInventory_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    public CaveProjectDocument? Project
    {
        get => (CaveProjectDocument?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public string? ZipPath
    {
        get => (string?)GetValue(ZipPathProperty);
        set => SetValue(ZipPathProperty, value);
    }

    public IEnumerable? MapRows
    {
        get => (IEnumerable?)GetValue(MapRowsProperty);
        set => SetValue(MapRowsProperty, value);
    }

    public IEnumerable? MapInventory
    {
        get => (IEnumerable?)GetValue(MapInventoryProperty);
        set => SetValue(MapInventoryProperty, value);
    }

    /// <summary>Native vector style for this tab (set from MainWindow tab headers).</summary>
    public SurveyVisualizationMode VisualizationMode
    {
        get => (SurveyVisualizationMode)GetValue(VisualizationModeProperty);
        set => SetValue(VisualizationModeProperty, value);
    }

    private MapCanvasEditorController? _mapEditor;
    private MapCanvasEditorTool _currentTool = MapCanvasEditorTool.PanZoom;
    private SketchEditorSymbolKind _stampKind = SketchEditorSymbolKind.RockBlock;
    private MainViewModel? _wiredMainVm;
    private CaveProjectDocument? _designLayerProjectScope;
    private bool _applyingSettings;
    private SurveyMapPickHighlight? _surveyPickHighlight;

    public SectionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += SectionView_DataContextChanged;
    }

    private void SectionView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        WireMainViewModel(e.NewValue as MainViewModel);

    private void WireMainViewModel(MainViewModel? vm)
    {
        if (_wiredMainVm != null)
            _wiredMainVm.SurveyDataChanged -= MainViewModel_SurveyDataChanged;
        _wiredMainVm = vm;
        if (_wiredMainVm != null)
            _wiredMainVm.SurveyDataChanged += MainViewModel_SurveyDataChanged;
    }

    private void MainViewModel_SurveyDataChanged(object? sender, EventArgs e) => Redraw();

    private MapCanvasEditorTool GetCurrentEditorTool() => MapCanvasEditorTool.PanZoom;

    private SketchEditorSymbolKind GetSelectedSketchStamp() => _stampKind;

    private void SyncSymbolPaletteEnabled()
    {
        if (SymbolPaletteRoot != null)
            SymbolPaletteRoot.IsEnabled = _currentTool == MapCanvasEditorTool.PlaceSymbol;
    }

    private void EnsureSymbolPaletteHasSelection()
    {
        if (SymbolPaletteRock is { IsChecked: true })
            return;
        if (SymbolPaletteWater is { IsChecked: true })
            return;
        if (SymbolPaletteSpele is { IsChecked: true })
            return;
        if (SymbolPaletteRock != null)
            SymbolPaletteRock.IsChecked = true;
    }

    private void SymbolPalette_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { IsChecked: true } t)
            return;
        _stampKind = t switch
        {
            _ when ReferenceEquals(t, SymbolPaletteRock) => SketchEditorSymbolKind.RockBlock,
            _ when ReferenceEquals(t, SymbolPaletteWater) => SketchEditorSymbolKind.WaterPool,
            _ => SketchEditorSymbolKind.StalactiteSpeleothem,
        };
        foreach (ToggleButton sibling in new ToggleButton?[]
                 {
                     SymbolPaletteRock, SymbolPaletteWater, SymbolPaletteSpele,
                 }.OfType<ToggleButton>())
        {
            if (!ReferenceEquals(sibling, t))
                sibling.IsChecked = false;
        }

        PersistSectionTab();
    }

    private void ApplySectionTabFromSettings()
    {
        _applyingSettings = true;
        try
        {
            var s = AppUiSettingsStore.LoadOrDefault().Section;
            if (Enum.TryParse(s.Tool, out MapCanvasEditorTool t))
                _currentTool = t;
            else
                _currentTool = MapCanvasEditorTool.PanZoom;
            if (StationNamesCheck != null)
                StationNamesCheck.IsChecked = s.StationNames;
            if (LegSurveyDetailsCheck != null)
                LegSurveyDetailsCheck.IsChecked = s.LegSurveyDetails;
            if (StationEnvironmentCheck != null)
                StationEnvironmentCheck.IsChecked = s.StationEnvironment;
            if (DepthSpanAnnotationsCheck != null)
                DepthSpanAnnotationsCheck.IsChecked = s.DepthSpanAnnotations;
            if (BracketMarkersCheck != null)
                BracketMarkersCheck.IsChecked = s.BracketMarkers;
            if (LoopClosureHighlightsCheck != null)
                LoopClosureHighlightsCheck.IsChecked = s.LoopClosureHighlights;
            if (LrudRibbonQcCheck != null)
                LrudRibbonQcCheck.IsChecked = s.LrudRibbonQcHighlights;
            if (WallHatchingCheck != null)
                WallHatchingCheck.IsChecked = s.ShowWallHatching;
            if (CoordinateGridCheck != null)
                CoordinateGridCheck.IsChecked = s.ShowCoordinateGrid;
            if (CartographyOverlayCheck != null)
                CartographyOverlayCheck.IsChecked = s.Overlay;
            SyncCartographicIntensityCombo(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);
            SyncSurveyDetailDensityCombo(AppUiSettingsStore.LoadOrDefault().SurveyDetailDensity);
            SyncMapExportQualityCombo(AppUiSettingsStore.LoadOrDefault().MapExportQuality);
            if (EditorToolPan != null && EditorToolSelect != null && EditorToolDraw != null &&
                EditorToolSymbol != null)
            {
                EditorToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
                EditorToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
                EditorToolDraw.IsChecked = _currentTool == MapCanvasEditorTool.DrawFreehand;
                EditorToolSymbol.IsChecked = _currentTool == MapCanvasEditorTool.PlaceSymbol;
                if (EditorToolErase != null)
                    EditorToolErase.IsChecked = _currentTool == MapCanvasEditorTool.Erase;
            }
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void ApplyDeferredSectionZoomFromSettings()
    {
        var s = AppUiSettingsStore.LoadOrDefault().Section;
        if (ZoomScale == null || ZoomPan == null)
            return;
        var zx = Math.Clamp(s.ZoomScale > 0 ? s.ZoomScale : 1, 0.12, 12.0);
        ZoomScale.ScaleX = zx;
        ZoomScale.ScaleY = zx;
        ZoomPan.X = s.PanX;
        ZoomPan.Y = s.PanY;
    }

    private void PersistSectionTab()
    {
        if (_applyingSettings)
            return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.Section.Tool = _currentTool.ToString();
        all.Section.StationNames = StationNamesCheck?.IsChecked == true;
        all.Section.LegSurveyDetails = LegSurveyDetailsCheck?.IsChecked != false;
        all.Section.StationEnvironment = StationEnvironmentCheck?.IsChecked != false;
        all.Section.DepthSpanAnnotations = DepthSpanAnnotationsCheck?.IsChecked != false;
        all.Section.BracketMarkers = BracketMarkersCheck?.IsChecked != false;
        all.Section.LoopClosureHighlights = LoopClosureHighlightsCheck?.IsChecked != false;
        all.Section.LrudRibbonQcHighlights = LrudRibbonQcCheck?.IsChecked != false;
        all.Section.ShowWallHatching = WallHatchingCheck?.IsChecked == true;
        all.Section.ShowCoordinateGrid = CoordinateGridCheck?.IsChecked == true;
        all.Section.Overlay = CartographyOverlayCheck?.IsChecked != false;
        if (ZoomScale != null)
            all.Section.ZoomScale = ZoomScale.ScaleX;
        if (ZoomPan != null)
        {
            all.Section.PanX = ZoomPan.X;
            all.Section.PanY = ZoomPan.Y;
        }

        AppUiSettingsStore.Save(all);
    }

    private void EditorToolPan_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.PanZoom;
        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void EditorToolSelect_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.Select;
        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void EditorToolDraw_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.DrawFreehand;
        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void EditorToolSymbol_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
        {
            _currentTool = MapCanvasEditorTool.PlaceSymbol;
            EnsureSymbolPaletteHasSelection();
        }

        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void EditorToolErase_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.Erase;
        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SurveyCanvasTheme.Changed += OnSurveyCanvasThemeChanged;
        if (DesignLayer != null && HostScroll != null && ZoomPan != null)
        {
            _mapEditor = new MapCanvasEditorController(
                DesignLayer, HostScroll, ZoomPan, GetCurrentEditorTool, GetSelectedSketchStamp);
        }

        ApplySectionTabFromSettings();
        _currentTool = MapCanvasEditorTool.PanZoom;
        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        else if (SymbolPaletteRock != null && SymbolPaletteWater != null && SymbolPaletteSpele != null)
            SymbolPaletteRock.IsChecked = true;
        SyncSymbolPaletteEnabled();

        WireMainViewModel(DataContext as MainViewModel);
        SurveyStationSelectionHub.StationSelected += OnExternalStationSelected;
        SurveyStationSelectionHub.SelectionCleared += OnExternalSelectionCleared;
        Redraw();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitMapSurfaceToHost));
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyDeferredSectionZoomFromSettings));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PersistSectionTab();
        SurveyStationSelectionHub.StationSelected -= OnExternalStationSelected;
        SurveyStationSelectionHub.SelectionCleared -= OnExternalSelectionCleared;
        SurveyCanvasTheme.Changed -= OnSurveyCanvasThemeChanged;
        WireMainViewModel(null);
        if (MapHostGrid != null)
            MapHostGrid.SizeChanged -= MapHostGrid_SizeChanged;
        UnwireMapRows(MapRows);
        UnwireMapInventory(MapInventory);
    }

    private void MapHostGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged && !e.HeightChanged)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(FitMapSurfaceToHost));
    }

    private void FitMapSurfaceToHost()
    {
        if (MapHostGrid == null || SurveyCanvas == null || DesignLayer == null || MapZoomRoot == null ||
            HostScroll == null || ZoomScale == null)
            return;
        var w = Math.Max(320, MapHostGrid.ActualWidth);
        var h = Math.Max(240, MapHostGrid.ActualHeight);
        if (w < 8 || h < 8)
            return;
        if (Math.Abs(SurveyCanvas.Width - w) < 0.5 && Math.Abs(SurveyCanvas.Height - h) < 0.5)
            return;
        MapZoomRoot.Width = w;
        MapZoomRoot.Height = h;
        SurveyCanvas.Width = w;
        SurveyCanvas.Height = h;
        DesignLayer.Width = w;
        DesignLayer.Height = h;
        ZoomScale.CenterX = w * 0.5;
        ZoomScale.CenterY = h * 0.5;
        Redraw();
    }

    private void OnSurveyCanvasThemeChanged() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(Redraw));

    private void MaybeResetDesignLayerForProjectChange()
    {
        if (DesignLayer == null)
            return;
        if (ReferenceEquals(Project, _designLayerProjectScope))
            return;
        _designLayerProjectScope = Project;
        DesignLayer.Children.Clear();
        _mapEditor?.OnDesignLayerCleared();
    }

    private void RefreshCaveNameBanner()
    {
        if (CaveNameBanner == null || CaveNameBannerText == null || CaveNameBannerSubText == null)
            return;

        var p = Project;
        if (p == null)
        {
            CaveNameBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var name = CaveProjectDisplayNames.GetDisplayName(p);
        if (string.IsNullOrWhiteSpace(name))
        {
            CaveNameBanner.Visibility = Visibility.Collapsed;
            return;
        }

        CaveNameBannerText.Text = name;
        var sub = "Section";
        if (!string.IsNullOrWhiteSpace(p.Date))
            sub += " · " + p.Date.Trim();
        CaveNameBannerSubText.Text = sub;
        CaveNameBanner.Visibility = Visibility.Visible;
    }

    private void Redraw()
    {
        if (SurveyCanvas == null || DesignLayer == null || MapZoomRoot == null)
            return;

        MaybeResetDesignLayerForProjectChange();
        SurveyCanvas.Children.Clear();
        RefreshCaveNameBanner();
        var p = Project;
        if (p == null)
        {
            AddMessage("Select a project from the list.");
            AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
            return;
        }

        try
        {
            var underlays = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
            var scene = SectionSceneBuilder.TryBuild(p, VisualizationMode);
            if (scene == null)
            {
                if (underlays.Count > 0)
                {
                    ZoomScale.CenterX = SurveyCanvas.Width / 2;
                    ZoomScale.CenterY = SurveyCanvas.Height / 2;
                    SectionCanvasRenderer.DrawRasterUnderlaysOnly(
                        SurveyCanvas,
                        highContrast: false,
                        SurveyCanvas.Width,
                        SurveyCanvas.Height,
                        underlays,
                        "section");
                    AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                    return;
                }

                AddMessage(
                    "No section vectors/sketches (viewMode 1) or traverse data for this project. "
                    + "If cartography paths resolve to PNG/JPEG/WebP/TIFF (ZIP open, or .zip next to exported data.json), a raster underlay can still appear here.");
                AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                return;
            }

            ZoomScale.CenterX = SurveyCanvas.Width / 2;
            ZoomScale.CenterY = SurveyCanvas.Height / 2;
            SectionCanvasRenderer.Draw(
                scene,
                SurveyCanvas,
                highContrast: false,
                SurveyCanvas.Width,
                SurveyCanvas.Height,
                underlays,
                p,
                ZipPath,
                CurrentDrawOptions());
            AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SectionView] Redraw failed: {ex}");
            SurveyCanvas.Children.Clear();
            AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
            MessageBox.Show(
                $"Section view could not render this project.\n\n{ex.Message}",
                "Section render error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            AddMessage($"Render error: {ex.Message}");
        }
    }

    private void AddMessage(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 14,
            MaxWidth = 520,
            TextWrapping = TextWrapping.Wrap,
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "Cave.TextMuted");
        Canvas.SetLeft(tb, 16);
        Canvas.SetTop(tb, 16);
        SurveyCanvas.Children.Add(tb);
    }

    private void HostScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ZoomScale == null || ZoomPan == null || MapZoomRoot == null)
            return;
        e.Handled = true;
        var focus = e.GetPosition(MapZoomRoot);
        var w = Math.Max(0, MapZoomRoot.ActualWidth);
        var h = Math.Max(0, MapZoomRoot.ActualHeight);
        var clamped = new Point(Math.Clamp(focus.X, 0, w), Math.Clamp(focus.Y, 0, h));
        if (!MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, e.Delta > 0, clamped))
            return;
        PersistSectionTab();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => MapZoomIn();

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => MapZoomOut();

    /// <inheritdoc />
    public void MapZoomIn() => ApplyMapZoom(zoomIn: true);

    /// <inheritdoc />
    public void MapZoomOut() => ApplyMapZoom(zoomIn: false);

    /// <inheritdoc />
    public void UndoSketchEdit() { }

    /// <inheritdoc />
    public void RedoSketchEdit() { }

    /// <inheritdoc />
    public bool TryDeleteSelectedInk() => false;

    public bool TryDuplicateSelectedInk() => false;

    public void FitMapToSurveyBounds() { }

    private void ApplyMapZoom(bool zoomIn)
    {
        if (ZoomScale == null || ZoomPan == null)
            return;
        var focus = new Point(ZoomScale.CenterX, ZoomScale.CenterY);
        if (!MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, zoomIn, focus))
            return;
        PersistSectionTab();
    }

    private void DesignLayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _mapEditor?.OnMouseDownForMapPan(sender, e);
        DesignLayer_MapMouseDown(sender, e);
    }

    private void DesignLayer_MouseUp(object sender, MouseButtonEventArgs e) =>
        _mapEditor?.OnMouseUpForMapPan(sender, e);

    private void DesignLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _mapEditor?.OnMouseLeftButtonDown(sender, e);

    private void DesignLayer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) =>
        _mapEditor?.OnMouseMove(sender, e);

    private void DesignLayer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _mapEditor?.OnMouseLeftButtonUp(sender, e);

    private void DesignLayer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        _mapEditor?.OnMouseLeave(sender, e);

    private void DesignLayer_MapMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || e.ChangedButton != MouseButton.Left || _currentTool != MapCanvasEditorTool.PanZoom)
            return;
        ResetMapView();
        e.Handled = true;
    }

    private void CartographyOptions_Changed(object sender, RoutedEventArgs e)
    {
        Redraw();
        PersistSectionTab();
    }

    private PlanCanvasDrawOptions CurrentDrawOptions()
    {
        var intensity = CartographicIntensityParser.Parse(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);
        return PlanCanvasDrawOptions.ForSection(
            StationNamesCheck?.IsChecked == true,
            CartographyOverlayCheck?.IsChecked != false,
            VisualizationMode,
            intensity,
            _surveyPickHighlight,
            LegSurveyDetailsCheck?.IsChecked != false,
            StationEnvironmentCheck?.IsChecked != false,
            DepthSpanAnnotationsCheck?.IsChecked != false,
            BracketMarkersCheck?.IsChecked != false,
            LoopClosureHighlightsCheck?.IsChecked != false,
            LrudRibbonQcCheck?.IsChecked != false,
            CoordinateGridCheck?.IsChecked == true,
            WallHatchingCheck?.IsChecked == true);
    }

    private void OnExternalStationSelected(object? sender, SurveyStationSelectionEventArgs e)
    {
        if (string.Equals(e.Source, "Section", StringComparison.OrdinalIgnoreCase))
            return;
        _surveyPickHighlight = new SurveyMapPickHighlight(false, e.StationName.Trim(), null);
        Redraw();
    }

    private void OnExternalSelectionCleared(object? sender, SurveyStationSelectionEventArgs e)
    {
        if (string.Equals(e.Source, "Section", StringComparison.OrdinalIgnoreCase))
            return;
        _surveyPickHighlight = null;
        Redraw();
    }

    private void ResetView_Click(object sender, RoutedEventArgs e) => ResetMapView();

    /// <inheritdoc />
    public void ResetMapView()
    {
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = 1;
        ZoomScale.ScaleY = 1;
        Redraw();
        PersistSectionTab();
    }

    /// <inheritdoc />
    public void ClearMapSelectionAndRedraw() => Redraw();

    /// <inheritdoc />
    public void ApplyMapEditorTool(MapCanvasEditorTool tool)
    {
        _applyingSettings = true;
        try
        {
            _currentTool = tool;
            if (EditorToolPan != null)
                EditorToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
            if (EditorToolSelect != null)
                EditorToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
            if (EditorToolDraw != null)
                EditorToolDraw.IsChecked = _currentTool == MapCanvasEditorTool.DrawFreehand;
            if (EditorToolSymbol != null)
                EditorToolSymbol.IsChecked = _currentTool == MapCanvasEditorTool.PlaceSymbol;
            if (EditorToolErase != null)
                EditorToolErase.IsChecked = _currentTool == MapCanvasEditorTool.Erase;
        }
        finally
        {
            _applyingSettings = false;
        }

        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    /// <inheritdoc />
    public void ShowPrintPreview()
    {
        var p = Project;
        if (p == null)
        {
            MessageBox.Show("Select a project.", "Print", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var underlaysPrint = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
        var scene = SectionSceneBuilder.TryBuild(p, VisualizationMode);
        var hi = PrintHiContrastCheck.IsChecked == true;
        if (scene == null)
        {
            if (underlaysPrint.Count == 0)
            {
                MessageBox.Show(
                    "No section data to print and no resolvable map image for a raster-only preview.",
                    "Print",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SectionCanvasRenderer.DrawRasterUnderlaysOnly(
                SurveyCanvas,
                hi,
                SurveyCanvas.Width,
                SurveyCanvas.Height,
                underlaysPrint,
                "section");
        }
        else
            SectionCanvasRenderer.Draw(
                scene,
                SurveyCanvas,
                hi,
                SurveyCanvas.Width,
                SurveyCanvas.Height,
                underlaysPrint,
                p,
                ZipPath,
                CurrentDrawOptions());
        if (MapZoomRoot == null)
        {
            Redraw();
            return;
        }

        var titleSuffix = scene == null ? "  ·  map preview (no survey geometry)" : "";
        var hiContrast = PrintHiContrastCheck.IsChecked == true;
        try
        {
            SurveyMapPrintWorkflow.ShowPreview(
                Window.GetWindow(this),
                p,
                SurveyMapPrintKind.Section,
                MapZoomRoot,
                hiContrast,
                titleSuffix,
                BuildPrintCartographyContext(),
                ResetMapViewAfterPrint);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Print", MessageBoxButton.OK, MessageBoxImage.Error);
            ResetMapViewAfterPrint();
        }
    }

    private void ResetMapViewAfterPrint()
    {
        if (ZoomPan == null || ZoomScale == null)
            return;
        ZoomPan.X = ZoomPan.Y = 0;
        ZoomScale.ScaleX = ZoomScale.ScaleY = 1;
        Redraw();
    }

    private SurveyMapPrintContext BuildPrintCartographyContext()
    {
        if (CartographyOverlayCheck?.IsChecked == false)
            return new SurveyMapPrintContext { ShowCartographyOverlay = false };

        var project = Project;
        if (project == null)
            return new SurveyMapPrintContext { ShowCartographyOverlay = false };

        var scene = SectionSceneBuilder.TryBuild(project, VisualizationMode);
        if (scene == null)
            return new SurveyMapPrintContext { ShowCartographyOverlay = false };

        var layout = PlanCanvasSurveyLayout.FromAxisAlignedBounds(
            scene.MinX, scene.MaxX, scene.MinY, scene.MaxY,
            SurveyCanvas?.Width ?? 960,
            SurveyCanvas?.Height ?? 640);
        return new SurveyMapPrintContext
        {
            SourcePxPerMetre = layout.PxPerMetre,
            CanvasKind = SurveyCanvasKind.Section,
            ShowCartographyOverlay = true,
        };
    }

    private void SyncCartographicIntensityCombo(string? persisted)
    {
        if (CartographicIntensityCombo == null)
            return;
        var key = CartographicIntensityParser.Parse(persisted).ToString();
        foreach (ComboBoxItem item in CartographicIntensityCombo.Items)
        {
            if (item.Tag is string t && string.Equals(t, key, StringComparison.OrdinalIgnoreCase))
            {
                CartographicIntensityCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void CartographicIntensityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || CartographicIntensityCombo?.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.CartographicIntensity = tag;
        AppUiSettingsStore.Save(all);
        Redraw();
    }

    private void SyncSurveyDetailDensityCombo(string? persisted)
    {
        if (SurveyDetailDensityCombo == null)
            return;
        var tag = SurveyDetailDensityParser.ToPersistedString(SurveyDetailDensityParser.Parse(persisted));
        foreach (ComboBoxItem item in SurveyDetailDensityCombo.Items)
        {
            if (item.Tag is string t && string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
            {
                SurveyDetailDensityCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SurveyDetailDensityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || SurveyDetailDensityCombo?.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;

        var density = SurveyDetailDensityParser.Parse(tag);
        var all = AppUiSettingsStore.LoadOrDefault();
        all.SurveyDetailDensity = SurveyDetailDensityParser.ToPersistedString(density);
        SurveyDetailDensityMapper.ApplyToMapTab(all.Plan, density);
        SurveyDetailDensityMapper.ApplyToMapTab(all.Section, density);
        SurveyDetailDensityMapper.ApplyToMapTab(all.Sketch, density);
        AppUiSettingsStore.Save(all);

        _applyingSettings = true;
        try
        {
            var s = all.Section;
            if (StationNamesCheck != null)
                StationNamesCheck.IsChecked = s.StationNames;
            if (LegSurveyDetailsCheck != null)
                LegSurveyDetailsCheck.IsChecked = s.LegSurveyDetails;
            if (StationEnvironmentCheck != null)
                StationEnvironmentCheck.IsChecked = s.StationEnvironment;
            if (DepthSpanAnnotationsCheck != null)
                DepthSpanAnnotationsCheck.IsChecked = s.DepthSpanAnnotations;
            if (BracketMarkersCheck != null)
                BracketMarkersCheck.IsChecked = s.BracketMarkers;
            if (LoopClosureHighlightsCheck != null)
                LoopClosureHighlightsCheck.IsChecked = s.LoopClosureHighlights;
            if (LrudRibbonQcCheck != null)
                LrudRibbonQcCheck.IsChecked = s.LrudRibbonQcHighlights;
        }
        finally
        {
            _applyingSettings = false;
        }

        Redraw();
    }

    private void SyncMapExportQualityCombo(string? persisted)
    {
        if (MapExportQualityCombo == null)
            return;
        var tag = MapExportQualityParser.ToPersistedString(MapExportQualityParser.Parse(persisted));
        foreach (ComboBoxItem item in MapExportQualityCombo.Items)
        {
            if (item.Tag is string t && string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
            {
                MapExportQualityCombo.SelectedItem = item;
                return;
            }
        }
    }

    private MapExportQuality SelectedMapExportQuality()
    {
        if (MapExportQualityCombo?.SelectedItem is ComboBoxItem { Tag: string tag })
            return MapExportQualityParser.Parse(tag);
        return MapExportQualityParser.Parse(AppUiSettingsStore.LoadOrDefault().MapExportQuality);
    }

    private void MapExportQualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || MapExportQualityCombo?.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.MapExportQuality = tag;
        AppUiSettingsStore.Save(all);
    }

    private void ExportSectionPng_Click(object sender, RoutedEventArgs e)
    {
        var bytes = CaptureSectionPngBytes();
        if (bytes == null || bytes.Length == 0)
        {
            MessageBox.Show(
                "Nothing to export — open a project with section vectors or traverse data.",
                "Export PNG",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            FileName = SanitizeFileName(Project?.Name) + "-section.png",
        };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            File.WriteAllBytes(dlg.FileName, bytes);
            if (DataContext is MainViewModel vm)
                vm.StatusMessage = $"High-res section PNG saved: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export PNG", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public byte[]? CaptureSectionPngBytes()
    {
        var p = Project;
        if (p == null)
            return null;

        var quality = SelectedMapExportQuality();
        var underlays = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
        return PlanMapRasterExporter.TryCapturePlanPngHighRes(
            p,
            VisualizationMode,
            PrintHiContrastCheck?.IsChecked == true,
            PlanCanvasDrawOptionsFactory.ForRasterExport(
                SurveyCanvasKind.Section,
                quality,
                VisualizationMode,
                p,
                showWallHatching: WallHatchingCheck?.IsChecked == true),
            underlays,
            ZipPath,
            vectorViewMode: SurveyStationGeometry.AndroidViewModeSection,
            quality: quality);
    }

    private static string SanitizeFileName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "section" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            n = n.Replace(c, '_');
        return n.Length > 80 ? n[..80] : n;
    }
}
