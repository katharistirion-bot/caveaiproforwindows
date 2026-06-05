using System;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

public partial class PlanView : System.Windows.Controls.UserControl, IMapSurfaceShortcuts
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(PlanView),
        new PropertyMetadata(null, (d, _) => ((PlanView)d).Redraw()));

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(PlanView),
        new PropertyMetadata(null, (d, _) => ((PlanView)d).Redraw()));

    public static readonly DependencyProperty MapRowsProperty = DependencyProperty.Register(
        nameof(MapRows),
        typeof(IEnumerable),
        typeof(PlanView),
        new PropertyMetadata(null, OnMapRowsChanged));

    public static readonly DependencyProperty MapInventoryProperty = DependencyProperty.Register(
        nameof(MapInventory),
        typeof(IEnumerable),
        typeof(PlanView),
        new PropertyMetadata(null, OnMapInventoryChanged));

    public static readonly DependencyProperty VisualizationModeProperty = DependencyProperty.Register(
        nameof(VisualizationMode),
        typeof(SurveyVisualizationMode),
        typeof(PlanView),
        new PropertyMetadata(SurveyVisualizationMode.Standard, OnVisualizationModeChanged));

    private static void OnVisualizationModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (PlanView)d;
        if (!v.IsLoaded)
            return;
        v.ZoomPan.X = 0;
        v.ZoomPan.Y = 0;
        v.ZoomScale.ScaleX = 1;
        v.ZoomScale.ScaleY = 1;
        v.FitMapSurfaceToHost();
        v.Redraw();
        v.PersistPlanTab();
    }

    private static void OnMapRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (PlanView)d;
        v.UnwireMapRows(e.OldValue);
        v.WireMapRows(e.NewValue);
        v.Redraw();
    }

    private static void OnMapInventoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (PlanView)d;
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

    /// <summary>Open backup .zip path (if any) so map assets inside the archive can be extracted for the plan underlay.</summary>
    public string? ZipPath
    {
        get => (string?)GetValue(ZipPathProperty);
        set => SetValue(ZipPathProperty, value);
    }

    /// <summary>All Maps-tab rows (includes standalone files) for resolving raster underlays.</summary>
    public IEnumerable? MapRows
    {
        get => (IEnumerable?)GetValue(MapRowsProperty);
        set => SetValue(MapRowsProperty, value);
    }

    /// <summary>Rows from Android <c>map_inventory.json</c> when a backup ZIP was opened.</summary>
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
    private PlanScene? _interactivePlanScene;
    private PlanCanvasSurveyLayout _surveyHitLayout;
    private bool _surveyHitLayoutReady;
    private bool _applyingSettings;
    private SurveyMapPickHighlight? _surveyPickHighlight;
    /// <summary>Select mode empty-click hides the analytical column until Esc or another pick.</summary>
    private bool _stationDetailsPaneDismissed;

    /// <summary>PanZoom click-vs-drag detection state: where the left-button went down and if a station was already selected.</summary>
    private Point? _panZoomLeftDownAt;
    private bool _panZoomLeftDownHadSelection;
    private const double PanZoomClickVsDragSlopDip = 4.5;

    private const string NoSelectionStatus = "No item selected";
    private static readonly Brush PropertyNeutralBorderBrush = BrushFromHex("#FF4F5C6F");
    private static readonly Brush PropertyNeutralBadgeBrush = BrushFromHex("#33232C39");
    private static readonly Brush PropertyNeutralTextBrush = BrushFromHex("#FFD2DAE5");
    private static readonly Brush PropertyStationBorderBrush = BrushFromHex("#FF2A8A6E");
    private static readonly Brush PropertyStationBadgeBrush = BrushFromHex("#3331A27D");
    private static readonly Brush PropertyStationTextBrush = BrushFromHex("#FF9EF0D3");
    private static readonly Brush PropertyLegBorderBrush = BrushFromHex("#FF2D79B5");
    private static readonly Brush PropertyLegBadgeBrush = BrushFromHex("#333C8FDC");
    private static readonly Brush PropertyLegTextBrush = BrushFromHex("#FFAEDAFF");

    public PlanView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += PlanView_DataContextChanged;
    }

    private void PlanView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
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

    private MapCanvasEditorTool GetCurrentEditorTool() => _currentTool;

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

        PersistPlanTab();
    }

    private void EditorToolPan_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.PanZoom;
        SyncSymbolPaletteEnabled();
        PersistPlanTab();
    }

    private void EditorToolSelect_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.Select;
        SyncSymbolPaletteEnabled();
        PersistPlanTab();
    }

    private void EditorToolDraw_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.DrawFreehand;
        SyncSymbolPaletteEnabled();
        PersistPlanTab();
    }

    private void EditorToolSymbol_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { IsChecked: true })
        {
            _currentTool = MapCanvasEditorTool.PlaceSymbol;
            EnsureSymbolPaletteHasSelection();
        }

        SyncSymbolPaletteEnabled();
        PersistPlanTab();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SurveyCanvasTheme.Changed += OnSurveyCanvasThemeChanged;
        if (DesignLayer != null && HostScroll != null && ZoomPan != null)
        {
            _mapEditor = new MapCanvasEditorController(
                DesignLayer, HostScroll, ZoomPan, GetCurrentEditorTool, GetSelectedSketchStamp);
        }

        if (ZoomScale != null)
            ZoomScale.Changed += MapTransform_Changed;
        if (ZoomPan != null)
            ZoomPan.Changed += MapTransform_Changed;

        ApplyPlanTabFromSettings();
        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        else if (SymbolPaletteRock != null && SymbolPaletteWater != null && SymbolPaletteSpele != null)
            SymbolPaletteRock.IsChecked = true;
        SyncSymbolPaletteEnabled();

        WireMainViewModel(DataContext as MainViewModel);
        SurveyStationSelectionHub.StationSelected += OnExternalStationSelected;
        ResetPropertiesPanelToSummary();
        Redraw();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitMapSurfaceToHost));
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            ApplyDeferredPlanZoomFromSettings();
            ResetPropertiesPanelToSummary();
        }));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PersistPlanTab();
        SurveyStationSelectionHub.StationSelected -= OnExternalStationSelected;
        SurveyCanvasTheme.Changed -= OnSurveyCanvasThemeChanged;
        WireMainViewModel(null);
        if (MapHostGrid != null)
            MapHostGrid.SizeChanged -= MapHostGrid_SizeChanged;
        if (ZoomScale != null)
            ZoomScale.Changed -= MapTransform_Changed;
        if (ZoomPan != null)
            ZoomPan.Changed -= MapTransform_Changed;
        UnwireMapRows(MapRows);
        UnwireMapInventory(MapInventory);
        if (HostViewport3D != null && HostViewportShell != null)
            CaveViewport3DPresenter.Detach(HostViewport3D, HostViewportShell);
    }

    private void MapTransform_Changed(object? sender, EventArgs e) => UpdateStationFloatingCardPosition();

    private void OnExternalStationSelected(object? sender, SurveyStationSelectionEventArgs e)
    {
        if (string.Equals(e.Source, "Plan", StringComparison.OrdinalIgnoreCase))
            return;
        ApplyExternalStationSelection(e.StationName);
        if (e.RequestZoom)
            ZoomToStation(e.StationName);
    }

    /// <summary>Pan/zoom so <paramref name="stationName"/> is centered in the plan viewport.</summary>
    public void ZoomToStation(string stationName)
    {
        if (ZoomScale == null || ZoomPan == null || HostScroll == null || !_surveyHitLayoutReady)
            return;
        if (!TryFindSceneStation(stationName, out var coord))
            return;

        var canvasPt = _surveyHitLayout.WorldToCanvas(coord.X, coord.Y);
        var targetScale = MapZoomInteractions.ClampScale(Math.Max(ZoomScale.ScaleX, 1.85));
        ZoomScale.CenterX = canvasPt.X;
        ZoomScale.CenterY = canvasPt.Y;
        ZoomScale.ScaleX = targetScale;
        ZoomScale.ScaleY = targetScale;

        var hostW = HostScroll.ViewportWidth > 0 ? HostScroll.ViewportWidth : MapHostGrid?.ActualWidth ?? 800;
        var hostH = HostScroll.ViewportHeight > 0 ? HostScroll.ViewportHeight : MapHostGrid?.ActualHeight ?? 600;
        ZoomPan.X = hostW * 0.5 - canvasPt.X * targetScale;
        ZoomPan.Y = hostH * 0.5 - canvasPt.Y * targetScale;
        UpdateStationFloatingCardPosition();
    }

    public void ApplyExternalStationSelection(string stationName)
    {
        if (Project == null || string.IsNullOrWhiteSpace(stationName))
            return;

        if (_interactivePlanScene != null
            && _surveyHitLayoutReady
            && TryFindSceneStation(stationName, out var coord))
        {
            _stationDetailsPaneDismissed = false;
            UpdatePropertiesPanel(new SurveyPickStation(stationName.Trim(), coord));
        }
        else
            _surveyPickHighlight = new SurveyMapPickHighlight(false, stationName.Trim(), null);

        Redraw();
        UpdateStationFloatingCardPosition();
    }

    private void MapHostGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged && !e.HeightChanged)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(FitMapSurfaceToHost));
    }

    /// <summary>Size the plan/3D surface to the tab’s map host (below toolbars), so fit/reset use that rectangle—not the whole window.</summary>
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
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Redraw));

    private void MaybeResetDesignLayerForProjectChange()
    {
        if (DesignLayer == null)
            return;
        if (ReferenceEquals(Project, _designLayerProjectScope))
            return;
        _designLayerProjectScope = Project;
        DesignLayer.Children.Clear();
        _mapEditor?.OnDesignLayerCleared();
        InvalidateSurveyPickState(true);
    }

    private void InvalidateSurveyPickState(bool clearDetails)
    {
        _interactivePlanScene = null;
        _surveyHitLayoutReady = false;
        _surveyPickHighlight = null;
        _stationDetailsPaneDismissed = false;
        HideStationFloatingCard();
        if (clearDetails)
            ResetPropertiesPanelToSummary();
    }

    private void ApplySurveyDetailsColumnExpanded(bool expanded)
    {
        if (SurveyDetailsPane == null || SurveyDetailsColumn == null)
            return;
        if (expanded)
        {
            SurveyDetailsColumn.MinWidth = 230;
            SurveyDetailsColumn.Width = new GridLength(250);
            SurveyDetailsPane.Visibility = Visibility.Visible;
        }
        else
        {
            SurveyDetailsPane.Visibility = Visibility.Collapsed;
            SurveyDetailsColumn.MinWidth = 0;
            SurveyDetailsColumn.Width = new GridLength(0);
        }
    }

    private void ResetPropertiesPanelToSummary()
    {
        HideStationFloatingCard();
        if (PropertySelectionStatusText == null
            || PropertyStationLegNameText == null
            || PropertyCoordinatesText == null
            || PropertySurveyDataText == null
            || PropertyWallDimensionsText == null)
            return;

        ApplyPropertyCaptionState(PropertyCaptionStyle.Overview);

        var p = Project;
        if (p == null)
        {
            if (PropertyPaneTitleText != null)
                PropertyPaneTitleText.Text = "Survey overview";
            PropertySelectionStatusText.Text = "No item selected";
            ApplyPropertiesVisualState(PropertiesVisualState.None);
            PropertyStationLegNameText.Text = "Open a cave project.";
            PropertyCoordinatesText.Text = "-";
            PropertySurveyDataText.Text = "Survey stats appear after loading a project.";
            PropertyWallDimensionsText.Text = "-";
            SetPropertyAndroidPayloadText("");
            ApplySurveyDetailsColumnExpanded(true);
            return;
        }

        var (stations, totalTape, zSpan, _) = SurveyPlanHudStats.Compute(p);
        var inv = CultureInfo.InvariantCulture;
        var legs = p.Shots.Count(s => s.IsTraverseLeg);
        if (PropertyPaneTitleText != null)
            PropertyPaneTitleText.Text = "Survey overview";
        PropertySelectionStatusText.Text = NoSelectionStatus;
        ApplyPropertiesVisualState(PropertiesVisualState.None);
        PropertyStationLegNameText.Text = "Global cave stats";
        PropertyCoordinatesText.Text = $"Stations: {stations}  |  Traverse legs: {legs}";
        PropertySurveyDataText.Text =
            $"Total length: {totalTape.ToString("0.##", inv)} m  |  Depth span (ΔZ): {zSpan.ToString("0.##", inv)} m";
        PropertyWallDimensionsText.Text = "Use Select mode and click a station or leg.";
        SetPropertyAndroidPayloadText(AndroidSurveyPayloadFormatter.FormatProjectContext(p));

        if (!_stationDetailsPaneDismissed)
            ApplySurveyDetailsColumnExpanded(true);
    }

    private enum PropertyCaptionStyle
    {
        Overview,
        Station,
        Leg,
    }

    private void ApplyPropertyCaptionState(PropertyCaptionStyle style)
    {
        if (PropertyNameFieldCaption == null || PropertyCoordsFieldCaption == null ||
            PropertySurveyFieldCaption == null || PropertyWallFieldCaption == null)
            return;

        switch (style)
        {
            case PropertyCaptionStyle.Station:
                PropertyNameFieldCaption.Text = "Station name / ID";
                PropertyCoordsFieldCaption.Text = "Local plan X · Y · Z elevation (m)";
                PropertySurveyFieldCaption.Text = "Representative survey shot (tape · azimuth · clino)";
                PropertyWallFieldCaption.Text = "LRUD cross-section (m)";
                break;
            case PropertyCaptionStyle.Leg:
                PropertyNameFieldCaption.Text = "Traverse connection";
                PropertyCoordsFieldCaption.Text = "Endpoints (world X,Y,Z)";
                PropertySurveyFieldCaption.Text = "Recorded leg geometry";
                PropertyWallFieldCaption.Text = "LRUD at committing station (from shot)";
                break;
            default:
                PropertyNameFieldCaption.Text = "Name";
                PropertyCoordsFieldCaption.Text = "Plan XY & Z elevation";
                PropertySurveyFieldCaption.Text = "Tape · azimuth · clino";
                PropertyWallFieldCaption.Text = "Cross-section LRUD (Left · Right · Up · Down)";
                break;
        }
    }

    private void SetPropertyAndroidPayloadText(string text)
    {
        if (PropertyAndroidPayloadText == null)
            return;
        PropertyAndroidPayloadText.Text = string.IsNullOrWhiteSpace(text) ? "—" : text;
    }

    private void UpdatePropertiesPanel(SurveyPickResult pick)
    {
        if (PropertySelectionStatusText == null
            || PropertyStationLegNameText == null
            || PropertyCoordinatesText == null
            || PropertySurveyDataText == null
            || PropertyWallDimensionsText == null)
            return;

        var inv = CultureInfo.InvariantCulture;
        if (pick is not SurveyPickStation)
            HideStationFloatingCard();
        switch (pick)
        {
            case SurveyPickStation station:
            {
                if (PropertyPaneTitleText != null)
                    PropertyPaneTitleText.Text = "Station details";
                ApplyPropertyCaptionState(PropertyCaptionStyle.Station);

                _surveyPickHighlight = new SurveyMapPickHighlight(false, station.Name.Trim(), null);
                SurveyStationSelectionHub.Select(station.Name.Trim(), "Plan");
                PropertySelectionStatusText.Text = "Selected station";
                ApplyPropertiesVisualState(PropertiesVisualState.Station);
                PropertyStationLegNameText.Text = station.Name;
                var zTxt = $"Z elevation {station.Coord.Z.ToString("0.###", inv)} m (plan reduction)";
                var xyTxt =
                    $"Local X {station.Coord.X.ToString("0.###", inv)} m  ·  Local Y {station.Coord.Y.ToString("0.###", inv)} m";
                PropertyCoordinatesText.Text = $"{xyTxt}\n{zTxt}";

                var shot = SurveyStationInspector.TryGetRepresentativeShotForStation(Project, station.Name);
                string? lrudCardText = null;
                if (shot != null)
                {
                    var (l, r, u, d) = shot.EffectivePlanLrud();
                    var depthPart = Math.Abs(shot.Depth) > 1e-5f
                        ? $"Logged depth (shot): {shot.Depth.ToString("0.###", inv)} m"
                        : null;
                    var surveyParts = $"Tape {shot.Distance.ToString("0.###", inv)} m  ·  Azimuth {shot.Azimuth.ToString("0.##", inv)}°  ·  Clino {shot.Clino.ToString("0.##", inv)}°";
                    PropertySurveyDataText.Text = depthPart != null ? $"{surveyParts}\n{depthPart}" : surveyParts;
                    PropertyWallDimensionsText.Text =
                        $"Left {l.ToString("0.##", inv)}  ·  Right {r.ToString("0.##", inv)}  ·  Up {u.ToString("0.##", inv)}  ·  Down {d.ToString("0.##", inv)} m";
                    lrudCardText =
                        $"L {l.ToString("0.##", inv)}  ·  R {r.ToString("0.##", inv)}  ·  U {u.ToString("0.##", inv)}  ·  D {d.ToString("0.##", inv)} m";
                }
                else
                {
                    PropertySurveyDataText.Text = "No recorded shot resolves this station (check traverse legs).";
                    PropertyWallDimensionsText.Text = "-";
                    lrudCardText = "LRUD: no recorded shot for this station.";
                }

                ShowStationFloatingCard(station, lrudCardText);

                var stationBlock = AndroidSurveyPayloadFormatter.FormatStationSurveyNotes(Project, station.Name.Trim());
                if (shot != null)
                {
                    var detail = AndroidSurveyPayloadFormatter.FormatShotDetail(Project, shot);
                    stationBlock = string.IsNullOrWhiteSpace(stationBlock)
                        ? detail
                        : $"{stationBlock.TrimEnd()}\n\n— Representative shot —\n{detail}";
                }

                SetPropertyAndroidPayloadText(stationBlock);
                ApplySurveyDetailsColumnExpanded(true);
                break;
            }
            case SurveyPickLeg leg:
            {
                if (PropertyPaneTitleText != null)
                    PropertyPaneTitleText.Text = "Traverse leg details";
                ApplyPropertyCaptionState(PropertyCaptionStyle.Leg);

                var shot = leg.Shot;
                _surveyPickHighlight = new SurveyMapPickHighlight(
                    true,
                    (shot.FromStation ?? "").Trim(),
                    (shot.ToStation ?? "").Trim());
                var (l, r, u, d) = shot.EffectivePlanLrud();
                PropertySelectionStatusText.Text = "Selected traverse leg";
                ApplyPropertiesVisualState(PropertiesVisualState.Leg);
                PropertyStationLegNameText.Text = $"{shot.FromStation} -> {shot.ToStation}";
                PropertyCoordinatesText.Text =
                    $"From ({leg.FromCoord.X.ToString("0.###", inv)}, {leg.FromCoord.Y.ToString("0.###", inv)}, {leg.FromCoord.Z.ToString("0.###", inv)})  to  ({leg.ToCoord.X.ToString("0.###", inv)}, {leg.ToCoord.Y.ToString("0.###", inv)}, {leg.ToCoord.Z.ToString("0.###", inv)})";
                PropertySurveyDataText.Text =
                    $"Length {shot.Distance.ToString("0.###", inv)} m  |  Azimuth {shot.Azimuth.ToString("0.##", inv)}°  |  Clino {shot.Clino.ToString("0.##", inv)}°" +
                    (Math.Abs(shot.Depth) > 1e-5f ? $"  |  Depth (logged) {shot.Depth.ToString("0.###", inv)} m" : "");
                PropertyWallDimensionsText.Text =
                    $"L {l.ToString("0.##", inv)}  |  R {r.ToString("0.##", inv)}  |  U {u.ToString("0.##", inv)}  |  D {d.ToString("0.##", inv)} m";
                SetPropertyAndroidPayloadText(AndroidSurveyPayloadFormatter.FormatShotDetail(Project, shot));
                ApplySurveyDetailsColumnExpanded(true);
                break;
            }
            default:
                _surveyPickHighlight = null;
                ResetPropertiesPanelToSummary();
                break;
        }
    }

    private enum PropertiesVisualState
    {
        None,
        Station,
        Leg,
    }

    private void ApplyPropertiesVisualState(PropertiesVisualState state)
    {
        if (SurveyDetailsPane == null || PropertySelectionStatusBadge == null || PropertySelectionStatusText == null)
            return;

        switch (state)
        {
            case PropertiesVisualState.Station:
                SurveyDetailsPane.BorderBrush = PropertyStationBorderBrush;
                PropertySelectionStatusBadge.BorderBrush = PropertyStationBorderBrush;
                PropertySelectionStatusBadge.Background = PropertyStationBadgeBrush;
                PropertySelectionStatusText.Foreground = PropertyStationTextBrush;
                break;
            case PropertiesVisualState.Leg:
                SurveyDetailsPane.BorderBrush = PropertyLegBorderBrush;
                PropertySelectionStatusBadge.BorderBrush = PropertyLegBorderBrush;
                PropertySelectionStatusBadge.Background = PropertyLegBadgeBrush;
                PropertySelectionStatusText.Foreground = PropertyLegTextBrush;
                break;
            default:
                SurveyDetailsPane.BorderBrush = PropertyNeutralBorderBrush;
                PropertySelectionStatusBadge.BorderBrush = PropertyNeutralBorderBrush;
                PropertySelectionStatusBadge.Background = PropertyNeutralBadgeBrush;
                PropertySelectionStatusText.Foreground = PropertyNeutralTextBrush;
                break;
        }
    }

    private static Brush BrushFromHex(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private void ApplyPlanTabFromSettings()
    {
        _applyingSettings = true;
        try
        {
            var s = AppUiSettingsStore.LoadOrDefault().Plan;
            if (Enum.TryParse(s.Tool, out MapCanvasEditorTool t))
                _currentTool = t;
            else
                _currentTool = MapCanvasEditorTool.PanZoom;

            if (StationNamesCheck != null)
                StationNamesCheck.IsChecked = s.StationNames;
            if (StationZDepthCheck != null)
                StationZDepthCheck.IsChecked = s.StationZ;
            if (CartographyOverlayCheck != null)
                CartographyOverlayCheck.IsChecked = s.Overlay;

            SyncCartographicIntensityCombo(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);

            if (EditorToolPan != null && EditorToolSelect != null && EditorToolDraw != null &&
                EditorToolSymbol != null)
            {
                EditorToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
                EditorToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
                EditorToolDraw.IsChecked = _currentTool == MapCanvasEditorTool.DrawFreehand;
                EditorToolSymbol.IsChecked = _currentTool == MapCanvasEditorTool.PlaceSymbol;
            }
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void ApplyDeferredPlanZoomFromSettings()
    {
        var s = AppUiSettingsStore.LoadOrDefault().Plan;
        if (ZoomScale == null || ZoomPan == null)
            return;
        var zx = Math.Clamp(s.ZoomScale > 0 ? s.ZoomScale : 1, 0.12, 12.0);
        ZoomScale.ScaleX = zx;
        ZoomScale.ScaleY = zx;
        ZoomPan.X = s.PanX;
        ZoomPan.Y = s.PanY;
    }

    private void PersistPlanTab()
    {
        if (_applyingSettings)
            return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.Plan.Tool = _currentTool.ToString();
        all.Plan.StationNames = StationNamesCheck?.IsChecked == true;
        all.Plan.StationZ = StationZDepthCheck?.IsChecked == true;
        all.Plan.Overlay = CartographyOverlayCheck?.IsChecked != false;
        if (CartographicIntensityCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string tag })
            all.CartographicIntensity = tag;
        if (ZoomScale != null)
            all.Plan.ZoomScale = ZoomScale.ScaleX;
        if (ZoomPan != null)
        {
            all.Plan.PanX = ZoomPan.X;
            all.Plan.PanY = ZoomPan.Y;
        }

        AppUiSettingsStore.Save(all);
    }

    private void StaleSurveyHitLayoutOnly()
    {
        _interactivePlanScene = null;
        _surveyHitLayoutReady = false;
    }

    private void RefreshSurveyHud()
    {
        if (HudStationCountText == null || HudLengthText == null || HudZSpanText == null)
            return;
        var inv = CultureInfo.InvariantCulture;
        var p = Project;
        if (p == null)
        {
            HudStationCountText.Text = "Open a cave project.";
            HudLengthText.Text = "Survey stats appear after you load data.";
            HudZSpanText.Text = "";
            return;
        }

        var travLegs = p.Shots?.Count(s => s.IsTraverseLeg) ?? 0;
        if (travLegs == 0)
        {
            HudStationCountText.Text = "No traverse yet — add shots with to ≠ \"-\", export from CaveAI Pro.";
            HudLengthText.Text = "Total surveyed length (Σ tape): —";
            HudZSpanText.Text = "ΔZ (stations): —";
            return;
        }

        var (st, tape, dz, _) = SurveyPlanHudStats.Compute(p);
        HudStationCountText.Text = st <= 0 ? "Stations: —" : $"Stations: {st}";
        HudLengthText.Text = $"Total surveyed length (Σ traverse tape): {tape.ToString("0.##", inv)} m";
        HudZSpanText.Text = $"Maximum depth span (ΔZ of stations): {dz.ToString("0.##", inv)} m";
    }

    private void Redraw()
    {
        // During InitializeComponent(), CheckBox IsChecked can fire before named fields (e.g. SurveyCanvas) exist.
        if (SurveyCanvas == null || DesignLayer == null || MapZoomRoot == null || HostViewport3D == null ||
            HostViewportShell == null)
            return;

        try
        {
            if (VisualizationMode != SurveyVisualizationMode.Pseudo3D)
            {
                CaveViewport3DPresenter.Detach(HostViewport3D, HostViewportShell);
                HostViewportShell.Visibility = Visibility.Collapsed;
                HostScroll.Visibility = Visibility.Visible;
                if (Viewport3DMessage != null)
                    Viewport3DMessage.Visibility = Visibility.Collapsed;
                if (EditorToolsPanel != null)
                    EditorToolsPanel.Visibility = Visibility.Visible;
                if (SurveyDetailsPane != null)
                    SurveyDetailsPane.Visibility = Visibility.Visible;
            }

            if (VisualizationMode == SurveyVisualizationMode.Pseudo3D)
            {
                InvalidateSurveyPickState(true);
                if (SurveyDetailsPane != null)
                    SurveyDetailsPane.Visibility = Visibility.Collapsed;

                HostViewportShell.Visibility = Visibility.Visible;
                HostScroll.Visibility = Visibility.Collapsed;
                if (EditorToolsPanel != null)
                    EditorToolsPanel.Visibility = Visibility.Collapsed;
                SurveyCanvas.Children.Clear();
                DesignLayer.Children.Clear();
                _mapEditor?.OnDesignLayerCleared();

                var p3 = Project;
                if (p3 == null)
                {
                    CaveViewport3DPresenter.Detach(HostViewport3D, HostViewportShell);
                    HostViewport3D.Children.Clear();
                    HostViewport3D.Camera = null;
                    if (Viewport3DMessage != null)
                    {
                        Viewport3DMessage.Text = "Select a project from the list.";
                        Viewport3DMessage.Visibility = Visibility.Visible;
                    }

                    return;
                }

                if (Viewport3DMessage != null)
                    Viewport3DMessage.Visibility = Visibility.Collapsed;

                if (!CaveViewport3DPresenter.TryPopulate(HostViewport3D, p3, HostViewportShell))
                {
                    if (Viewport3DMessage != null)
                    {
                        Viewport3DMessage.Text =
                            "Could not build a 3D cave tube from this project. Add traverse shots with LRUD (left, right, up, down) at stations, then re-export.";
                        Viewport3DMessage.Visibility = Visibility.Visible;
                    }

                    return;
                }

                return;
            }

            MaybeResetDesignLayerForProjectChange();
            SurveyCanvas.Children.Clear();
            StaleSurveyHitLayoutOnly();
            var p = Project;
            if (p == null)
            {
                AddMessage("Select a project from the list.");
                AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                InvalidateSurveyPickState(true);
                return;
            }

            try
            {
                var underlays = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
                var scene = PlanSceneBuilder.TryBuild(p, SurveyStationGeometry.AndroidViewModePlan, VisualizationMode);
                if (scene == null)
                {
                    if (underlays.Count > 0)
                    {
                        ZoomScale.CenterX = SurveyCanvas.Width / 2;
                        ZoomScale.CenterY = SurveyCanvas.Height / 2;
                        PlanCanvasRenderer.DrawRasterUnderlaysOnly(
                            SurveyCanvas,
                            highContrast: false,
                            SurveyCanvas.Width,
                            SurveyCanvas.Height,
                            underlays,
                            "plan");
                        AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                        InvalidateSurveyPickState(true);
                        return;
                    }

                    AddMessage(
                        "No plan data yet — add traverse shots (to ≠ \"-\") and/or wall sketches or vectors in CaveAI Pro (Android), then re-export. "
                        + "If cartography paths in the project resolve to PNG/JPEG/WebP/TIFF (open .zip, or keep the original .zip next to exported data.json), a raster underlay can appear here even without traverse.");
                    AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                    InvalidateSurveyPickState(true);
                    return;
                }

                ZoomScale.CenterX = SurveyCanvas.Width / 2;
                ZoomScale.CenterY = SurveyCanvas.Height / 2;
                PlanCanvasRenderer.Draw(
                    scene,
                    SurveyCanvas,
                    highContrast: false,
                    SurveyCanvas.Width,
                    SurveyCanvas.Height,
                    underlays,
                    p,
                    ZipPath,
                    CurrentDrawOptions());
                _interactivePlanScene = scene;
                _surveyHitLayoutReady = PlanCanvasRenderer.TryComputeSurveyLayout(
                    scene, SurveyCanvas.Width, SurveyCanvas.Height, out _surveyHitLayout);
                if (_surveyHitLayoutReady)
                    AndroidImportedSymbolPresenter.SyncDesignLayer(DesignLayer, scene, _surveyHitLayout, highContrast: false);
                else
                    AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlanView] Redraw failed: {ex}");
                SurveyCanvas.Children.Clear();
                AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                InvalidateSurveyPickState(true);
                MessageBox.Show(
                    $"Plan view could not render this project.\n\n{ex.Message}",
                    "Plan render error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                AddMessage($"Render error: {ex.Message}");
            }
        }
        finally
        {
            RefreshSurveyHud();
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
        // CAD/GIS: wheel zooms toward cursor (Ctrl+wheel still works the same).
        e.Handled = true;
        var focus = e.GetPosition(MapZoomRoot);
        var w = Math.Max(0, MapZoomRoot.ActualWidth);
        var h = Math.Max(0, MapZoomRoot.ActualHeight);
        var clamped = new System.Windows.Point(
            Math.Clamp(focus.X, 0, w),
            Math.Clamp(focus.Y, 0, h));
        if (!MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, e.Delta > 0, clamped))
            return;
        PersistPlanTab();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => MapZoomIn();

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => MapZoomOut();

    /// <inheritdoc />
    public void MapZoomIn() => ApplyMapZoom(zoomIn: true);

    /// <inheritdoc />
    public void MapZoomOut() => ApplyMapZoom(zoomIn: false);

    private void ApplyMapZoom(bool zoomIn)
    {
        if (ZoomScale == null || ZoomPan == null)
            return;
        var focus = new System.Windows.Point(ZoomScale.CenterX, ZoomScale.CenterY);
        if (!MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, zoomIn, focus))
            return;
        PersistPlanTab();
    }

    private void DesignLayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _mapEditor?.OnMouseDownForMapPan(sender, e);
        DesignLayer_MapMouseDown(sender, e);
    }

    private void DesignLayer_MouseUp(object sender, MouseButtonEventArgs e) =>
        _mapEditor?.OnMouseUpForMapPan(sender, e);

    private void DesignLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == MapCanvasEditorTool.Select
            && _surveyHitLayoutReady
            && _interactivePlanScene != null
            && Project != null
            && SurveyCanvas != null)
        {
            _mapEditor?.ClearTransientSelectHighlight();
            var pt = e.GetPosition(SurveyCanvas);
            var pick = SurveyPlanPickService.TryPick(pt, _surveyHitLayout, _interactivePlanScene, Project);
            if (pick is SurveyPickNone)
            {
                ClearActiveSurveySelection(hideDetailsPane: true);
                e.Handled = true;
                return;
            }

            _stationDetailsPaneDismissed = false;
            UpdatePropertiesPanel(pick);
            Redraw();
            UpdateStationFloatingCardPosition();
            e.Handled = true;
            return;
        }

        // Default tool (PanZoom) is "always interactive": a left-click that lands on a station marker
        // preempts pan and selects the station. Empty-space clicks fall through to the normal pan logic.
        if (_currentTool == MapCanvasEditorTool.PanZoom
            && e.ClickCount == 1
            && _surveyHitLayoutReady
            && _interactivePlanScene != null
            && Project != null
            && SurveyCanvas != null)
        {
            var pt = e.GetPosition(SurveyCanvas);
            var stationPick = SurveyPlanPickService.TryPickStation(pt, _surveyHitLayout, _interactivePlanScene);
            if (stationPick != null)
            {
                _panZoomLeftDownAt = null;
                _panZoomLeftDownHadSelection = false;
                _stationDetailsPaneDismissed = false;
                UpdatePropertiesPanel(stationPick);
                Redraw();
                UpdateStationFloatingCardPosition();
                e.Handled = true;
                return;
            }

            _panZoomLeftDownAt = pt;
            _panZoomLeftDownHadSelection = _surveyPickHighlight != null;
        }

        _mapEditor?.OnMouseLeftButtonDown(sender, e);
    }

    private void DesignLayer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _mapEditor?.OnMouseMove(sender, e);
        if (_panZoomLeftDownAt is { } start && SurveyCanvas != null)
        {
            var now = e.GetPosition(SurveyCanvas);
            if (Math.Abs(now.X - start.X) > PanZoomClickVsDragSlopDip ||
                Math.Abs(now.Y - start.Y) > PanZoomClickVsDragSlopDip)
            {
                // Drag-pan started — cancel the "click on empty space" deselect intent and reposition the floating card.
                _panZoomLeftDownAt = null;
                _panZoomLeftDownHadSelection = false;
            }
        }

        if (_surveyPickHighlight != null)
            UpdateStationFloatingCardPosition();
    }

    private void DesignLayer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _mapEditor?.OnMouseLeftButtonUp(sender, e);

        if (_panZoomLeftDownAt is { } _ && _panZoomLeftDownHadSelection
                                        && _currentTool == MapCanvasEditorTool.PanZoom)
        {
            // Click (no drag) on empty space while a station was selected → deselect and hide the floating card.
            ClearActiveSurveySelection(hideDetailsPane: false);
        }

        _panZoomLeftDownAt = null;
        _panZoomLeftDownHadSelection = false;
    }

    private void ClearActiveSurveySelection(bool hideDetailsPane)
    {
        _surveyPickHighlight = null;
        if (hideDetailsPane)
        {
            _stationDetailsPaneDismissed = true;
            ApplySurveyDetailsColumnExpanded(false);
        }
        else
        {
            _stationDetailsPaneDismissed = false;
            ResetPropertiesPanelToSummary();
        }

        HideStationFloatingCard();
        Redraw();
    }

    private void ShowStationFloatingCard(SurveyPickStation station, string? lrudText)
    {
        if (StationFloatingCard == null
            || StationCardNameText == null
            || StationCardCoordsText == null
            || StationCardDepthText == null
            || StationCardLrudText == null)
            return;

        var inv = CultureInfo.InvariantCulture;
        StationCardNameText.Text = string.IsNullOrWhiteSpace(station.Name) ? "(unnamed)" : station.Name;
        StationCardCoordsText.Text =
            $"X {station.Coord.X.ToString("0.###", inv)}  ·  Y {station.Coord.Y.ToString("0.###", inv)} m";
        StationCardDepthText.Text = $"Z {station.Coord.Z.ToString("0.###", inv)} m elevation";
        StationCardLrudText.Text = lrudText ?? "-";
        StationFloatingCard.Visibility = Visibility.Visible;
        UpdateStationFloatingCardPosition();
    }

    private void HideStationFloatingCard()
    {
        if (StationFloatingCard != null)
            StationFloatingCard.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Re-anchors the floating card next to the currently selected station, accounting for the active
    /// <see cref="ZoomScale"/> / <see cref="ZoomPan"/> transform applied to <see cref="MapZoomRoot"/>.
    /// Card stays inside the visible map host and is offset above-right of the marker so it doesn't cover it.
    /// </summary>
    private void UpdateStationFloatingCardPosition()
    {
        if (StationFloatingCard == null || StationFloatingCard.Visibility != Visibility.Visible)
            return;
        if (_surveyPickHighlight == null || _surveyPickHighlight.IsLeg)
            return;
        if (!_surveyHitLayoutReady || _interactivePlanScene == null)
            return;
        if (SurveyCanvas == null || MapHostGrid == null)
            return;

        var name = _surveyPickHighlight.StationOrFrom?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        if (!TryFindSceneStation(name, out var coord))
            return;

        var canvasPt = _surveyHitLayout.WorldToCanvas(coord.X, coord.Y);
        Point hostPt;
        try
        {
            hostPt = SurveyCanvas.TransformToVisual(MapHostGrid).Transform(canvasPt);
        }
        catch
        {
            return;
        }

        StationFloatingCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardW = StationFloatingCard.DesiredSize.Width;
        var cardH = StationFloatingCard.DesiredSize.Height;

        const double offset = 18;
        var hostW = MapHostGrid.ActualWidth;
        var hostH = MapHostGrid.ActualHeight;

        var left = hostPt.X + offset;
        if (left + cardW > hostW - 8)
            left = hostPt.X - cardW - offset;
        if (left < 8)
            left = Math.Max(8, hostW - cardW - 8);

        var top = hostPt.Y - cardH - offset;
        if (top < 8)
            top = hostPt.Y + offset;
        if (top + cardH > hostH - 8)
            top = Math.Max(8, hostH - cardH - 8);

        StationFloatingCard.Margin = new Thickness(left, top, 0, 0);
    }

    private bool TryFindSceneStation(string name, out SurveyStationGeometry.StationPlanCoords coord)
    {
        coord = new SurveyStationGeometry.StationPlanCoords("?", float.NaN, float.NaN, float.NaN);
        if (_interactivePlanScene == null)
            return false;
        if (_interactivePlanScene.Stations.TryGetValue(name, out var c))
        {
            coord = c;
            return true;
        }

        var match = _interactivePlanScene.Stations.Keys.FirstOrDefault(
            k => string.Equals(k.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (match != null && _interactivePlanScene.Stations.TryGetValue(match, out var c2))
        {
            coord = c2;
            return true;
        }

        return false;
    }

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
        PersistPlanTab();
    }

    private void SyncCartographicIntensityCombo(string? persisted)
    {
        if (CartographicIntensityCombo == null)
            return;
        var key = CartographicIntensityParser.Parse(persisted).ToString();
        foreach (ComboBoxItem item in CartographicIntensityCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, key, StringComparison.OrdinalIgnoreCase))
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

    private PlanCanvasDrawOptions CurrentDrawOptions()
    {
        var intensity = CartographicIntensityParser.Parse(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);
        return PlanCanvasDrawOptions.ForPlan(
            StationNamesCheck?.IsChecked == true,
            CartographyOverlayCheck?.IsChecked != false,
            VisualizationMode,
            StationZDepthCheck?.IsChecked == true,
            intensity,
            _surveyPickHighlight);
    }

    private void ResetView_Click(object sender, RoutedEventArgs e) => ResetMapView();

    public void ResetMapView()
    {
        if (ZoomPan == null || ZoomScale == null)
            return;
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = 1;
        ZoomScale.ScaleY = 1;
        Redraw();
        PersistPlanTab();
    }

    public void ClearMapSelectionAndRedraw()
    {
        _surveyPickHighlight = null;
        _stationDetailsPaneDismissed = false;
        ResetPropertiesPanelToSummary();
        ApplySurveyDetailsColumnExpanded(true);
        Redraw();
    }

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
        }
        finally
        {
            _applyingSettings = false;
        }

        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        SyncSymbolPaletteEnabled();
        PersistPlanTab();
    }

    private void ExportPlanPng_Click(object sender, RoutedEventArgs e)
    {
        if (VisualizationMode == SurveyVisualizationMode.Pseudo3D && Project == null)
        {
            MessageBox.Show("Select a project first.", "Export PNG", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var bytes = CapturePlanPngBytes();
        if (bytes == null || bytes.Length == 0)
        {
            MessageBox.Show(
                "Nothing to export — open a project and use the 2D plan (or 3D tab for the tube view).",
                "Export PNG",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            FileName = SanitizeFileName(Project?.Name) + "-plan.png",
        };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            File.WriteAllBytes(dlg.FileName, bytes);
            PostExportStatus(dlg.FileName, "High-res plan PNG");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export PNG", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportPlanSvg_Click(object sender, RoutedEventArgs e)
    {
        var p = Project;
        if (p == null)
        {
            MessageBox.Show("Select a project first.", "Export SVG", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (VisualizationMode == SurveyVisualizationMode.Pseudo3D)
        {
            MessageBox.Show(
                "Vector SVG export is available from 2D plan modes. Switch to PLAN, LONG PROFILE, X-RAY, or PLAN 2-TONE.",
                "Export SVG",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (PlanSceneBuilder.TryBuild(p, SurveyStationGeometry.AndroidViewModePlan, VisualizationMode) == null)
        {
            MessageBox.Show(
                "No drawable plan geometry for this project (and no raster-only fallback for SVG).",
                "Export SVG",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "SVG (*.svg)|*.svg",
            DefaultExt = ".svg",
            FileName = SanitizeFileName(p.Name) + "-plan.svg",
        };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            using var fs = File.Create(dlg.FileName);
            SurveySvgExporter.WritePlanSvg(p, fs, SurveyStationGeometry.AndroidViewModePlan, VisualizationMode);
            PostExportStatus(dlg.FileName, "Plan SVG (vector)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export SVG", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PostExportStatus(string fullPath, string kind)
    {
        var name = Path.GetFileName(fullPath);
        if (DataContext is MainViewModel vm)
            vm.StatusMessage = $"{kind} saved: {name}";
    }

    private static string SanitizeFileName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "plan" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            n = n.Replace(c, '_');
        return n.Length > 80 ? n[..80] : n;
    }

    /// <summary>PNG export: 3D captures the viewport upscaled to ~300 DPI; 2D modes export full survey bounds at high resolution.</summary>
    public byte[]? CapturePlanPngBytes()
    {
        if (VisualizationMode == SurveyVisualizationMode.Pseudo3D
            && HostViewportShell != null
            && HostViewportShell.Visibility == Visibility.Visible)
        {
            var w = Math.Max(320, HostViewportShell.ActualWidth);
            var h = Math.Max(240, HostViewportShell.ActualHeight);
            if (w < 8 || h < 8)
            {
                var gw = MapHostGrid != null ? MapHostGrid.ActualWidth : 0;
                var gh = MapHostGrid != null ? MapHostGrid.ActualHeight : 0;
                w = gw > 8 ? Math.Max(320, gw) : 960;
                h = gh > 8 ? Math.Max(240, gh) : 640;
            }

            HostViewportShell.Measure(new Size(w, h));
            HostViewportShell.Arrange(new Rect(0, 0, w, h));
            HostViewportShell.UpdateLayout();

            var dpi = PlanMapRasterExporter.ExportDpiScale;
            var pxW = (int)Math.Max(1, Math.Ceiling(w * dpi));
            var pxH = (int)Math.Max(1, Math.Ceiling(h * dpi));
            var maxDim = Math.Max(pxW, pxH);
            if (maxDim > PlanMapRasterExporter.MaxExportEdgePixels)
            {
                var f = PlanMapRasterExporter.MaxExportEdgePixels / (double)maxDim;
                pxW = Math.Max(PlanMapRasterExporter.MinExportEdgePixels, (int)(pxW * f));
                pxH = Math.Max(PlanMapRasterExporter.MinExportEdgePixels, (int)(pxH * f));
            }

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var vb = new VisualBrush(HostViewportShell)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Viewbox = new Rect(0, 0, 1, 1),
                };
                dc.DrawRectangle(vb, null, new Rect(0, 0, pxW, pxH));
            }

            var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        var p = Project;
        if (p == null)
            return null;

        var underlays = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
        return PlanMapRasterExporter.TryCapturePlanPngHighRes(
            p,
            VisualizationMode,
            PrintHiContrastCheck?.IsChecked == true,
            CurrentDrawOptions(),
            underlays,
            ZipPath);
    }

    private void PrintPlan_Click(object sender, RoutedEventArgs e)
    {
        var p = Project;
        if (p == null)
        {
            MessageBox.Show(
                "Select a project from the list on the left.",
                "Print plan",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (VisualizationMode == SurveyVisualizationMode.Pseudo3D && HostViewportShell != null)
        {
            try
            {
                var w = Math.Max(320, HostViewportShell.ActualWidth);
                var h = Math.Max(240, HostViewportShell.ActualHeight);
                if (w < 8 || h < 8)
                {
                    var gw = MapHostGrid != null ? MapHostGrid.ActualWidth : 0;
                    var gh = MapHostGrid != null ? MapHostGrid.ActualHeight : 0;
                    w = gw > 8 ? Math.Max(320, gw) : 960;
                    h = gh > 8 ? Math.Max(240, gh) : 640;
                }

                HostViewportShell.Measure(new Size(w, h));
                HostViewportShell.Arrange(new Rect(0, 0, w, h));
                HostViewportShell.UpdateLayout();

                var pxW3d = (int)Math.Max(1, Math.Ceiling(w));
                var pxH3d = (int)Math.Max(1, Math.Ceiling(h));
                var rtb3d = new RenderTargetBitmap(pxW3d, pxH3d, 96, 96, PixelFormats.Pbgra32);
                rtb3d.Render(HostViewportShell);

                var pd3d = new PrintDialog();
                try
                {
                    pd3d.PrintTicket.PageOrientation = PageOrientation.Landscape;
                }
                catch
                {
                    /* ignore */
                }

                if (pd3d.ShowDialog() != true)
                    return;

                var pw3d = pd3d.PrintableAreaWidth;
                var ph3d = pd3d.PrintableAreaHeight;
                if (pw3d <= 0 || ph3d <= 0)
                {
                    MessageBox.Show("Invalid printable area.", "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var stack3d = new StackPanel { Background = Brushes.White, Width = pw3d };
                var title3d = new TextBlock
                {
                    Text = $"{p.Name}  ·  {p.Date}  ·  3D MODEL (LRUD tube, CAVE AI PRO)",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black,
                    Margin = new Thickness(24, 18, 24, 10),
                    TextWrapping = TextWrapping.Wrap,
                };
                stack3d.Children.Add(title3d);

                var vb3d = new Viewbox
                {
                    Width = pw3d - 48,
                    Height = Math.Max(120, ph3d - 72),
                    Margin = new Thickness(24, 0, 24, 24),
                    Stretch = Stretch.Uniform,
                };
                vb3d.Child = new Image { Source = rtb3d, SnapsToDevicePixels = true };
                stack3d.Children.Add(vb3d);

                stack3d.Measure(new Size(pw3d, ph3d));
                stack3d.Arrange(new Rect(0, 0, pw3d, ph3d));
                stack3d.UpdateLayout();

                pd3d.PrintVisual(stack3d, $"CAVE AI PRO — {p.Name} 3D");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlanView] PrintPlan 3D failed: {ex}");
                MessageBox.Show(ex.Message, "Print error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return;
        }

        PlanScene? scene;
        try
        {
            var underlaysPrint = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
            scene = PlanSceneBuilder.TryBuild(p, SurveyStationGeometry.AndroidViewModePlan, VisualizationMode);
            var hi = PrintHiContrastCheck.IsChecked == true;
            if (scene == null)
            {
                if (underlaysPrint.Count == 0)
                {
                    MessageBox.Show(
                        "No plan data to print (add traverse shots and/or sketches or vectors), and no resolvable map image for a raster-only preview.",
                        "Print plan",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                PlanCanvasRenderer.DrawRasterUnderlaysOnly(
                    SurveyCanvas,
                    hi,
                    SurveyCanvas.Width,
                    SurveyCanvas.Height,
                    underlaysPrint,
                    "plan");
            }
            else
            {
                PlanCanvasRenderer.Draw(
                    scene,
                    SurveyCanvas,
                    hi,
                    SurveyCanvas.Width,
                    SurveyCanvas.Height,
                    underlaysPrint,
                    p,
                    ZipPath,
                    CurrentDrawOptions());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlanView] PrintPlan render failed: {ex}");
            MessageBox.Show(
                $"Could not prepare the plan for printing.\n\n{ex.Message}",
                "Print plan",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Redraw();
            return;
        }

        var pd = new PrintDialog();
        try
        {
            pd.PrintTicket.PageOrientation = PageOrientation.Landscape;
        }
        catch
        {
            /* ignore */
        }

        if (pd.ShowDialog() != true)
        {
            Redraw();
            return;
        }

        var pw = pd.PrintableAreaWidth;
        var ph = pd.PrintableAreaHeight;
        if (pw <= 0 || ph <= 0)
        {
            MessageBox.Show("Invalid printable area.", "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
            Redraw();
            return;
        }

        if (MapZoomRoot == null)
        {
            Redraw();
            return;
        }

        MapZoomRoot.Measure(new Size(MapZoomRoot.Width, MapZoomRoot.Height));
        MapZoomRoot.Arrange(new Rect(0, 0, MapZoomRoot.Width, MapZoomRoot.Height));
        MapZoomRoot.UpdateLayout();

        var pxW = (int)Math.Max(1, Math.Ceiling(MapZoomRoot.Width));
        var pxH = (int)Math.Max(1, Math.Ceiling(MapZoomRoot.Height));
        var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(MapZoomRoot);

        var stack = new StackPanel { Background = Brushes.White, Width = pw };
        var titleSuffix = scene == null ? " — map preview (no survey geometry)" : "";
        var title = new TextBlock
        {
            Text = $"{p.Name}  ·  {p.Date}  ·  Plan (survey m, CAVE AI PRO){titleSuffix}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(24, 18, 24, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(title);

        var vb = new Viewbox
        {
            Width = pw - 48,
            Height = Math.Max(120, ph - 72),
            Margin = new Thickness(24, 0, 24, 24),
            Stretch = Stretch.Uniform,
        };
        vb.Child = new Image { Source = rtb, SnapsToDevicePixels = true };
        stack.Children.Add(vb);

        stack.Measure(new Size(pw, ph));
        stack.Arrange(new Rect(0, 0, pw, ph));
        stack.UpdateLayout();

        try
        {
            pd.PrintVisual(stack, $"CAVE AI PRO — {p.Name} plan");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Print error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ZoomPan.X = 0;
            ZoomPan.Y = 0;
            ZoomScale.ScaleX = ZoomScale.ScaleY = 1;
            Redraw();
        }
    }
}
