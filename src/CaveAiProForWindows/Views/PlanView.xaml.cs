using System;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
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
using CaveAiProForWindows.Services.ReferenceCatalog;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

public partial class PlanView : System.Windows.Controls.UserControl, IMapSurfaceShortcuts, ISurveyMapPrintSurface
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
        v.UpdateCartographySidebarVisibility();
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

    /// <summary>Flushes sketch-editor session and merges design-layer ink before raster export.</summary>
    public Action<CaveProjectDocument>? BeforePlanExport { get; set; }

    /// <summary>Sketch editor (or other) design layer to composite onto plan PNG exports.</summary>
    public Func<PlanDesignLayerExportContext?>? ResolveDesignLayerForExport { get; set; }

    private MapCanvasEditorController? _mapEditor;
    private MapCanvasEditorTool _currentTool = MapCanvasEditorTool.PanZoom;
    private MainViewModel? _wiredMainVm;
    private PlanScene? _interactivePlanScene;
    private PlanCanvasSurveyLayout _surveyHitLayout;
    private bool _surveyHitLayoutReady;
    private bool _applyingSettings;
    private bool _showReferencePins = true;
    private SurveyMapPickHighlight? _surveyPickHighlight;
    private CaveViewport3DFlyThrough? _flyThrough;
    private CaveViewport3DFlyThroughRecorder? _flyThroughRecorder;
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
        IsVisibleChanged += PlanView_IsVisibleChanged;
    }

    private void PlanView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && IsLoaded)
            Redraw();
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

    private static MapCanvasEditorTool NormalizeNavigationTool(MapCanvasEditorTool tool) =>
        tool == MapCanvasEditorTool.Select ? MapCanvasEditorTool.Select : MapCanvasEditorTool.PanZoom;

    private static bool IsPlanDesignTool(MapCanvasEditorTool tool) =>
        tool is MapCanvasEditorTool.DrawFreehand
            or MapCanvasEditorTool.PlaceSymbol
            or MapCanvasEditorTool.Erase
            or MapCanvasEditorTool.DrawLine;

    private void EditorToolPan_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.PanZoom;
        PersistPlanTab();
    }

    private void EditorToolSelect_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.Select;
        PersistPlanTab();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SurveyCanvasTheme.Changed += OnSurveyCanvasThemeChanged;
        if (DesignLayer != null && HostScroll != null && ZoomPan != null)
        {
            _mapEditor = new MapCanvasEditorController(
                DesignLayer,
                HostScroll,
                ZoomPan,
                GetCurrentEditorTool,
                () => SketchEditorSymbolKind.RockBlock);
        }

        if (ZoomScale != null)
            ZoomScale.Changed += MapTransform_Changed;
        if (ZoomPan != null)
            ZoomPan.Changed += MapTransform_Changed;

        ApplyPlanTabFromSettings();
        UpdateCartographySidebarVisibility();

        WireMainViewModel(DataContext as MainViewModel);
        SurveyStationSelectionHub.StationSelected += OnExternalStationSelected;
        SurveyStationSelectionHub.SelectionCleared += OnExternalSelectionCleared;
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
        SurveyStationSelectionHub.SelectionCleared -= OnExternalSelectionCleared;
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
            CaveViewport3DPresenter.Detach(HostViewport3D, HostViewportShell, Viewport3DLabelCanvas);
        _flyThrough?.Dispose();
        _flyThrough = null;
    }

    private void MapTransform_Changed(object? sender, EventArgs e) => UpdateStationFloatingCardPosition();

    private void OnExternalStationSelected(object? sender, SurveyStationSelectionEventArgs e)
    {
        if (string.Equals(e.Source, "Plan", StringComparison.OrdinalIgnoreCase))
            return;

        if (VisualizationMode == SurveyVisualizationMode.Pseudo3D)
        {
            if (!string.Equals(e.Source, "3D", StringComparison.OrdinalIgnoreCase))
                ApplyExternal3DStationSelection(e.StationName);
            return;
        }

        ApplyExternalStationSelection(e.StationName);
        if (e.RequestZoom)
            ZoomToStation(e.StationName);
    }

    private void OnExternalSelectionCleared(object? sender, SurveyStationSelectionEventArgs e)
    {
        if (string.Equals(e.Source, "Plan", StringComparison.OrdinalIgnoreCase))
            return;
        _surveyPickHighlight = null;
        ResetPropertiesPanelToSummary();
        Redraw();
        UpdateStationFloatingCardPosition();
    }

    private void ApplyExternal3DStationSelection(string stationName)
    {
        if (Project == null || string.IsNullOrWhiteSpace(stationName))
            return;

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(Project);
        var match = coords.Keys.FirstOrDefault(k =>
            string.Equals(k.Trim(), stationName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match == null || !coords.TryGetValue(match, out var c))
            return;

        _stationDetailsPaneDismissed = false;
        _surveyPickHighlight = new SurveyMapPickHighlight(false, match.Trim(), null);
        UpdatePropertiesPanel(new SurveyPickStation(match.Trim(), c));
        CaveViewport3DPresenter.SetHighlightStation(HostViewport3D, match.Trim());
        ApplySurveyDetailsColumnExpanded(true);
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
        if (ReferencePinsLayer != null)
        {
            ReferencePinsLayer.Width = w;
            ReferencePinsLayer.Height = h;
        }
        DesignLayer.Width = w;
        DesignLayer.Height = h;
        ZoomScale.CenterX = w * 0.5;
        ZoomScale.CenterY = h * 0.5;
        Redraw();
    }

    private void OnSurveyCanvasThemeChanged() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Redraw));

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
            PropertyStationLegNameText.Text = "Open a survey project.";
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
        PropertyStationLegNameText.Text = "Global survey stats";
        var siteLabel = SurveySiteTypeResolver.GetMapLabel(p);
        var siteLine = string.IsNullOrWhiteSpace(siteLabel) ? "" : $"Site type: {siteLabel}  |  ";
        PropertyCoordinatesText.Text = $"{siteLine}Stations: {stations}  |  Traverse legs: {legs}";
        PropertySurveyDataText.Text =
            $"Total length: {totalTape.ToString("0.##", inv)} m  |  Vertical span (Z): {zSpan.ToString("0.##", inv)} m";
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
                if (VisualizationMode != SurveyVisualizationMode.Pseudo3D)
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
                _currentTool = NormalizeNavigationTool(t);
            else
                _currentTool = MapCanvasEditorTool.PanZoom;

            if (StationNamesCheck != null)
                StationNamesCheck.IsChecked = s.StationNames;
            if (StationZDepthCheck != null)
                StationZDepthCheck.IsChecked = s.StationZ;
            if (LegSurveyDetailsCheck != null)
                LegSurveyDetailsCheck.IsChecked = s.LegSurveyDetails;
            if (StationEnvironmentCheck != null)
                StationEnvironmentCheck.IsChecked = s.StationEnvironment;
            if (DepthSpanAnnotationsCheck != null)
                DepthSpanAnnotationsCheck.IsChecked = s.DepthSpanAnnotations;
            if (BracketMarkersCheck != null)
                BracketMarkersCheck.IsChecked = s.BracketMarkers;
            if (Viewport3DMapSymbolsCheck != null)
                Viewport3DMapSymbolsCheck.IsChecked = s.Viewport3DMapSymbols;
            if (Viewport3DFieldCatalogCheck != null)
                Viewport3DFieldCatalogCheck.IsChecked = s.Viewport3DFieldCatalog;
            if (Viewport3DStationSnapshotsCheck != null)
                Viewport3DStationSnapshotsCheck.IsChecked = s.Viewport3DStationSnapshots;
            if (Viewport3DAiTagsCheck != null)
                Viewport3DAiTagsCheck.IsChecked = s.Viewport3DAiTags;
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
            if (Viewport3DShowLabelsCheck != null)
                Viewport3DShowLabelsCheck.IsChecked = s.Viewport3DShowLabels;
            SyncViewport3DLabelSizeCombo(s.Viewport3DLabelSize);
            SyncViewport3DTubeQualityCombo(s.Viewport3DTubeQuality);
            if (Viewport3DShowSplinesCheck != null)
                Viewport3DShowSplinesCheck.IsChecked = s.Viewport3DShowSplines;
            if (Viewport3DTopographyCheck != null)
                Viewport3DTopographyCheck.IsChecked = s.Viewport3DShowTopography;
            if (Viewport3DDemCheck != null)
                Viewport3DDemCheck.IsChecked = s.Viewport3DShowDem;
            if (Viewport3DSectionCutCheck != null)
                Viewport3DSectionCutCheck.IsChecked = s.Viewport3DSectionCutEnabled;
            if (Viewport3DSectionCutSlider != null)
                Viewport3DSectionCutSlider.Value = Math.Clamp(s.Viewport3DSectionCutPosition, 0.05, 0.95);
            SyncSectionCutAxisCombo(s.Viewport3DSectionCutAxis);

            SyncCartographicIntensityCombo(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);
            SyncSurveyDetailDensityCombo(AppUiSettingsStore.LoadOrDefault().SurveyDetailDensity);
            SyncMapExportQualityCombo(AppUiSettingsStore.LoadOrDefault().MapExportQuality);

            _showReferencePins = AppUiSettingsStore.LoadOrDefault().ShowReferencePinsOnPlan;
            if (ReferencePinsCheck != null)
                ReferencePinsCheck.IsChecked = _showReferencePins;

            if (EditorToolPan != null && EditorToolSelect != null)
            {
                EditorToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
                EditorToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
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

    private IReadOnlyList<PlanRasterUnderlay> LoadPlanUnderlays(CaveProjectDocument project) =>
        PlanMapUnderlayLoader.TryLoadRasterUnderlays(project, ZipPath, MapRows, MapInventory);

    private void ApplyReferencePinsOverlay()
    {
        if (ReferencePinsLayer == null)
            return;

        ReferencePinsLayer.Children.Clear();
        if (!_showReferencePins || Project == null || !_surveyHitLayoutReady || VisualizationMode == SurveyVisualizationMode.Pseudo3D)
            return;

        var pins = ReferenceCatalogPlanPins.CollectPins(Project);
        if (pins.Count == 0)
            return;

        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.PlanReferencePinsShown);

        foreach (var pin in pins)
        {
            var pt = ReferenceCatalogPlanPins.TryPinToCanvas(pin, Project, _surveyHitLayout);
            if (pt == null)
                continue;

            var size = pin.IsLinkedReference ? 12.0 : 8.0;
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = size,
                Height = size,
                Fill = pin.IsLinkedReference
                    ? Brushes.DeepSkyBlue
                    : ReferenceCatalogMapDepthColors.ReferenceFillBrush(pin.DepthM),
                Stroke = Brushes.White,
                StrokeThickness = pin.IsLinkedReference ? 1.5 : 0.75,
                Opacity = 0.9,
                ToolTip = pin.Name + (pin.IsLinkedReference ? " (linked reference)" : pin.Rich ? " (Surveyed)" : " (Sparse)"),
            };
            Canvas.SetLeft(dot, pt.Value.X - size * 0.5);
            Canvas.SetTop(dot, pt.Value.Y - size * 0.5);
            ReferencePinsLayer.Children.Add(dot);
        }
    }

    private void ReferencePinsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
            return;

        _showReferencePins = ReferencePinsCheck?.IsChecked == true;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.ShowReferencePinsOnPlan = _showReferencePins;
        AppUiSettingsStore.Save(all);
        ApplyReferencePinsOverlay();
    }

    private void PersistPlanTab()
    {
        if (_applyingSettings)
            return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.Plan.Tool = NormalizeNavigationTool(_currentTool).ToString();
        all.Plan.StationNames = StationNamesCheck?.IsChecked == true;
        all.Plan.StationZ = StationZDepthCheck?.IsChecked == true;
        all.Plan.LegSurveyDetails = LegSurveyDetailsCheck?.IsChecked != false;
        all.Plan.StationEnvironment = StationEnvironmentCheck?.IsChecked != false;
        all.Plan.DepthSpanAnnotations = DepthSpanAnnotationsCheck?.IsChecked != false;
        all.Plan.BracketMarkers = BracketMarkersCheck?.IsChecked != false;
        all.Plan.Viewport3DMapSymbols = Viewport3DMapSymbolsCheck?.IsChecked != false;
        all.Plan.Viewport3DFieldCatalog = Viewport3DFieldCatalogCheck?.IsChecked != false;
        all.Plan.Viewport3DStationSnapshots = Viewport3DStationSnapshotsCheck?.IsChecked != false;
        all.Plan.Viewport3DAiTags = Viewport3DAiTagsCheck?.IsChecked != false;
        all.Plan.LoopClosureHighlights = LoopClosureHighlightsCheck?.IsChecked != false;
        all.Plan.LrudRibbonQcHighlights = LrudRibbonQcCheck?.IsChecked != false;
        all.Plan.ShowWallHatching = WallHatchingCheck?.IsChecked == true;
        all.Plan.ShowCoordinateGrid = CoordinateGridCheck?.IsChecked == true;
        all.Plan.Overlay = CartographyOverlayCheck?.IsChecked != false;
        all.Plan.Viewport3DShowLabels = Viewport3DShowLabelsCheck?.IsChecked != false;
        if (Viewport3DLabelSizeCombo?.SelectedItem is ComboBoxItem { Tag: string labelSize })
            all.Plan.Viewport3DLabelSize = labelSize;
        if (Viewport3DTubeQualityCombo?.SelectedItem is ComboBoxItem { Tag: string tubeQuality })
            all.Plan.Viewport3DTubeQuality = tubeQuality;
        all.Plan.Viewport3DShowSplines = Viewport3DShowSplinesCheck?.IsChecked != false;
        all.Plan.Viewport3DShowTopography = Viewport3DTopographyCheck?.IsChecked == true;
        all.Plan.Viewport3DShowDem = Viewport3DDemCheck?.IsChecked == true;
        all.Plan.Viewport3DSectionCutEnabled = Viewport3DSectionCutCheck?.IsChecked == true;
        all.Plan.Viewport3DSectionCutPosition = Viewport3DSectionCutSlider?.Value ?? 0.5;
        if (Viewport3DSectionCutAxisCombo?.SelectedItem is ComboBoxItem { Tag: string axis })
            all.Plan.Viewport3DSectionCutAxis = axis;
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
            if (HudCaveNameText != null)
                HudCaveNameText.Text = "—";
            HudStationCountText.Text = "Open a cave project.";
            HudLengthText.Text = "Survey stats appear after you load data.";
            HudZSpanText.Text = "";
            return;
        }

        if (HudCaveNameText != null)
        {
            var caveName = CaveProjectDisplayNames.GetDisplayName(p);
            HudCaveNameText.Text = string.IsNullOrWhiteSpace(caveName) ? "Unnamed cave" : caveName;
        }

        var travLegs = p.Shots?.Count(s => s.IsTraverseLeg) ?? 0;
        if (travLegs == 0)
        {
            HudStationCountText.Text = "No traverse yet — add shots with to ≠ \"-\", export from CaveAI Pro.";
            HudLengthText.Text = "Total traverse length: —";
            HudZSpanText.Text = "Vertical span (station Z): —";
            return;
        }

        var (st, tape, dz, _) = SurveyPlanHudStats.Compute(p);
        HudStationCountText.Text = st <= 0 ? "Stations: —" : $"Stations: {st}";
        HudLengthText.Text = $"Total traverse length: {tape.ToString("0.##", inv)} m";
        HudZSpanText.Text = $"Vertical span (station Z): {dz.ToString("0.##", inv)} m";
    }

    private void RefreshViewport3DCaveBanner(CaveProjectDocument? project)
    {
        if (Viewport3DCaveBanner == null || Viewport3DCaveNameText == null || Viewport3DCaveSubText == null)
            return;

        if (project == null)
        {
            Viewport3DCaveBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var name = CaveProjectDisplayNames.GetDisplayName(project);
        if (string.IsNullOrWhiteSpace(name))
        {
            Viewport3DCaveBanner.Visibility = Visibility.Collapsed;
            return;
        }

        Viewport3DCaveNameText.Text = name;
        var sub = "3D survey";
        if (!string.IsNullOrWhiteSpace(project.Date))
            sub += " · " + project.Date.Trim();
        Viewport3DCaveSubText.Text = sub;
        Viewport3DCaveBanner.Visibility = Visibility.Visible;
    }

    private void Redraw()
    {
        // During InitializeComponent(), CheckBox IsChecked can fire before named fields (e.g. SurveyCanvas) exist.
        if (SurveyCanvas == null || DesignLayer == null || MapZoomRoot == null || HostViewport3D == null ||
            HostViewportShell == null)
            return;

        try
        {
            UpdateCartographySidebarVisibility();

            if (VisualizationMode != SurveyVisualizationMode.Pseudo3D)
            {
                CaveViewport3DPresenter.Detach(HostViewport3D, HostViewportShell, Viewport3DLabelCanvas);
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
                if (_flyThrough?.IsRunning == true || _flyThroughRecorder?.IsRunning == true)
                    return;

                if (SurveyDetailsPane != null)
                    SurveyDetailsPane.Visibility = Visibility.Visible;

                HostViewportShell.Visibility = Visibility.Visible;
                HostScroll.Visibility = Visibility.Collapsed;
                SurveyCanvas.Children.Clear();
                DesignLayer.Children.Clear();
                _mapEditor?.OnDesignLayerCleared();

                var p3 = Project;
                if (p3 == null)
                {
                    CaveViewport3DPresenter.Detach(HostViewport3D, HostViewportShell, Viewport3DLabelCanvas);
                    HostViewport3D.Children.Clear();
                    HostViewport3D.Camera = null;
                    if (Viewport3DMessage != null)
                    {
                        Viewport3DMessage.Text = "Select a project from the list.";
                        Viewport3DMessage.Visibility = Visibility.Visible;
                    }

                    RefreshViewport3DCaveBanner(null);
                    return;
                }

                if (Viewport3DMessage != null)
                    Viewport3DMessage.Visibility = Visibility.Collapsed;

                if (!CaveViewport3DPresenter.TryPopulate(
                        HostViewport3D,
                        p3,
                        HostViewportShell,
                        Viewport3DLabelCanvas,
                        CurrentViewport3DDisplayOptions(),
                        OnViewport3DPick,
                        _surveyPickHighlight is { IsLeg: false } hl ? hl.StationOrFrom : null,
                        OnViewport3DLabelClick,
                        CurrentViewport3DLabelScale()))
                {
                    if (Viewport3DMessage != null)
                    {
                        Viewport3DMessage.Text =
                            "Could not build a 3D cave tube from this project. Add traverse shots with LRUD (left, right, up, down) at stations, then re-export.";
                        Viewport3DMessage.Visibility = Visibility.Visible;
                    }

                    RefreshViewport3DCaveBanner(null);
                    return;
                }

                RefreshViewport3DCaveBanner(p3);
                RefreshSurveyHud();
                return;
            }

            DesignLayer?.Children.Clear();
            _mapEditor?.OnDesignLayerCleared();
            SurveyCanvas.Children.Clear();
            ReferencePinsLayer?.Children.Clear();
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
                var underlays = LoadPlanUnderlays(p);
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
                AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                ApplyReferencePinsOverlay();
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
        if (!_applyingSettings && sender is System.Windows.Controls.CheckBox cb)
            ApplyViewport3DOverlayExclusivity(cb);

        UpdateCartographySidebarVisibility();
        Redraw();
        PersistPlanTab();
    }

    /// <summary>Only one ground/section overlay at a time — stacked planes clutter the 3D view.</summary>
    private void ApplyViewport3DOverlayExclusivity(System.Windows.Controls.CheckBox changed)
    {
        if (changed != Viewport3DTopographyCheck && changed != Viewport3DDemCheck && changed != Viewport3DSectionCutCheck)
            return;
        if (changed.IsChecked != true)
            return;

        _applyingSettings = true;
        try
        {
            if (changed == Viewport3DTopographyCheck)
            {
                if (Viewport3DDemCheck != null)
                    Viewport3DDemCheck.IsChecked = false;
                if (Viewport3DSectionCutCheck != null)
                    Viewport3DSectionCutCheck.IsChecked = false;
            }
            else if (changed == Viewport3DDemCheck)
            {
                if (Viewport3DTopographyCheck != null)
                    Viewport3DTopographyCheck.IsChecked = false;
                if (Viewport3DSectionCutCheck != null)
                    Viewport3DSectionCutCheck.IsChecked = false;
            }
            else if (changed == Viewport3DSectionCutCheck)
            {
                if (Viewport3DTopographyCheck != null)
                    Viewport3DTopographyCheck.IsChecked = false;
                if (Viewport3DDemCheck != null)
                    Viewport3DDemCheck.IsChecked = false;
            }
        }
        finally
        {
            _applyingSettings = false;
        }
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

    private void MapToolbarPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || MapToolbarPresetCombo?.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;
        if (string.IsNullOrWhiteSpace(tag))
            return;
        var preset = tag switch
        {
            "Publication" => MapToolbarPresetApplier.Preset.Publication,
            "Field QC" => MapToolbarPresetApplier.Preset.FieldQc,
            "Minimal" => MapToolbarPresetApplier.Preset.Minimal,
            _ => MapToolbarPresetApplier.Preset.Minimal,
        };
        MapToolbarPresetApplier.Apply(preset);
        ApplyPlanTabFromSettings();
        Redraw();
        _applyingSettings = true;
        try
        {
            MapToolbarPresetCombo.SelectedIndex = 0;
        }
        finally
        {
            _applyingSettings = false;
        }
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
            var s = all.Plan;
            if (StationNamesCheck != null)
                StationNamesCheck.IsChecked = s.StationNames;
            if (StationZDepthCheck != null)
                StationZDepthCheck.IsChecked = s.StationZ;
            if (LegSurveyDetailsCheck != null)
                LegSurveyDetailsCheck.IsChecked = s.LegSurveyDetails;
            if (StationEnvironmentCheck != null)
                StationEnvironmentCheck.IsChecked = s.StationEnvironment;
            if (DepthSpanAnnotationsCheck != null)
                DepthSpanAnnotationsCheck.IsChecked = s.DepthSpanAnnotations;
            if (BracketMarkersCheck != null)
                BracketMarkersCheck.IsChecked = s.BracketMarkers;
            if (Viewport3DMapSymbolsCheck != null)
                Viewport3DMapSymbolsCheck.IsChecked = s.Viewport3DMapSymbols;
            if (Viewport3DFieldCatalogCheck != null)
                Viewport3DFieldCatalogCheck.IsChecked = s.Viewport3DFieldCatalog;
            if (Viewport3DStationSnapshotsCheck != null)
                Viewport3DStationSnapshotsCheck.IsChecked = s.Viewport3DStationSnapshots;
            if (Viewport3DAiTagsCheck != null)
                Viewport3DAiTagsCheck.IsChecked = s.Viewport3DAiTags;
            if (LoopClosureHighlightsCheck != null)
                LoopClosureHighlightsCheck.IsChecked = s.LoopClosureHighlights;
            if (LrudRibbonQcCheck != null)
                LrudRibbonQcCheck.IsChecked = s.LrudRibbonQcHighlights;
            if (Viewport3DShowLabelsCheck != null)
                Viewport3DShowLabelsCheck.IsChecked = s.Viewport3DShowLabels;
            SyncViewport3DLabelSizeCombo(s.Viewport3DLabelSize);
        }
        finally
        {
            _applyingSettings = false;
        }

        Redraw();
    }

    private Viewport3DDisplayOptions CurrentViewport3DDisplayOptions()
    {
        var annotations = CurrentViewport3DAnnotationOptions();
        Viewport3DSectionCutOptions? cut = null;
        if (Viewport3DSectionCutCheck?.IsChecked == true)
        {
            var axis = Viewport3DSectionCutAxis.HorizontalZ;
            if (Viewport3DSectionCutAxisCombo?.SelectedItem is ComboBoxItem { Tag: string tag })
                Enum.TryParse(tag, out axis);
            cut = new Viewport3DSectionCutOptions(
                axis,
                Viewport3DSectionCutSlider?.Value ?? 0.5,
                Enabled: true);
        }

        var settings = AppUiSettingsStore.LoadOrDefault();
        var tubeQuality = Viewport3DTubeQualityCombo?.SelectedItem is ComboBoxItem { Tag: string tqTag }
                          && Enum.TryParse<TubeMeshQuality>(tqTag, true, out var tqParsed)
            ? tqParsed
            : TubeMeshQualityResolver.Resolve(
                settings.Plan.Viewport3DTubeQuality,
                settings.CartographicIntensity);

        return new Viewport3DDisplayOptions(
            annotations,
            Viewport3DShowSplinesCheck?.IsChecked != false,
            Viewport3DTopographyCheck?.IsChecked == true,
            Viewport3DDemCheck?.IsChecked == true,
            cut,
            tubeQuality);
    }

    private Viewport3DAnnotationOptions CurrentViewport3DAnnotationOptions()
    {
        var show = Viewport3DShowLabelsCheck?.IsChecked != false;
        if (!show)
            return Viewport3DAnnotationOptions.AllOff;

        return new Viewport3DAnnotationOptions(
            ShowLabels: true,
            ShowStationNames: StationNamesCheck?.IsChecked == true,
            ShowStationZ: StationZDepthCheck?.IsChecked == true,
            ShowLegDetails: LegSurveyDetailsCheck?.IsChecked != false,
            ShowEnvironment: StationEnvironmentCheck?.IsChecked != false,
            ShowDepthSpans: DepthSpanAnnotationsCheck?.IsChecked != false,
            ShowBrackets: BracketMarkersCheck?.IsChecked != false,
            ShowMapSymbols: Viewport3DMapSymbolsCheck?.IsChecked != false,
            ShowFieldCatalog: Viewport3DFieldCatalogCheck?.IsChecked != false,
            ShowStationSnapshots: Viewport3DStationSnapshotsCheck?.IsChecked != false,
            ShowAiTags: Viewport3DAiTagsCheck?.IsChecked != false);
    }

    private void UpdateCartographySidebarVisibility()
    {
        var is3d = VisualizationMode == SurveyVisualizationMode.Pseudo3D;
        var show3dLabels = Viewport3DShowLabelsCheck?.IsChecked == true;
        var planOnly = is3d ? Visibility.Collapsed : Visibility.Visible;

        if (PlanNavigationToolsSection != null)
            PlanNavigationToolsSection.Visibility = planOnly;
        if (PlanCartographySeparator != null)
            PlanCartographySeparator.Visibility = planOnly;
        if (PlanCartographySection != null)
            PlanCartographySection.Visibility = planOnly;
        if (PlanMapToolbarPanel != null)
            PlanMapToolbarPanel.Visibility = planOnly;

        if (PlanDisplaySectionTitle != null)
            PlanDisplaySectionTitle.Visibility = is3d ? Visibility.Collapsed : Visibility.Visible;

        var annotationVisibility = is3d
            ? (show3dLabels ? Visibility.Visible : Visibility.Collapsed)
            : Visibility.Visible;

        if (PlanDisplayOptionsSection != null)
            PlanDisplayOptionsSection.Visibility = is3d ? annotationVisibility : Visibility.Visible;

        if (StationNamesCheck != null)
            StationNamesCheck.Visibility = annotationVisibility;
        if (StationZDepthCheck != null)
            StationZDepthCheck.Visibility = annotationVisibility;
        if (LegSurveyDetailsCheck != null)
            LegSurveyDetailsCheck.Visibility = annotationVisibility;
        if (StationEnvironmentCheck != null)
            StationEnvironmentCheck.Visibility = annotationVisibility;
        if (DepthSpanAnnotationsCheck != null)
            DepthSpanAnnotationsCheck.Visibility = annotationVisibility;
        if (BracketMarkersCheck != null)
            BracketMarkersCheck.Visibility = annotationVisibility;
        if (Viewport3DMapSymbolsCheck != null)
            Viewport3DMapSymbolsCheck.Visibility = annotationVisibility;
        if (Viewport3DFieldCatalogCheck != null)
            Viewport3DFieldCatalogCheck.Visibility = annotationVisibility;
        if (Viewport3DStationSnapshotsCheck != null)
            Viewport3DStationSnapshotsCheck.Visibility = annotationVisibility;
        if (Viewport3DAiTagsCheck != null)
            Viewport3DAiTagsCheck.Visibility = annotationVisibility;

        if (Viewport3DToolsPanel != null)
            Viewport3DToolsPanel.Visibility = is3d ? Visibility.Visible : Visibility.Collapsed;
        if (Viewport3DFloatingToolbar != null)
            Viewport3DFloatingToolbar.Visibility = is3d ? Visibility.Visible : Visibility.Collapsed;
        if (Viewport3DFloatShowLabelsCheck != null && Viewport3DShowLabelsCheck != null && !_applyingSettings)
        {
            _applyingSettings = true;
            try
            {
                Viewport3DFloatShowLabelsCheck.IsChecked = Viewport3DShowLabelsCheck.IsChecked;
            }
            finally
            {
                _applyingSettings = false;
            }
        }
        if (LoopClosureHighlightsCheck != null)
            LoopClosureHighlightsCheck.Visibility = is3d ? Visibility.Collapsed : Visibility.Visible;
        if (LrudRibbonQcCheck != null)
            LrudRibbonQcCheck.Visibility = is3d ? Visibility.Collapsed : Visibility.Visible;
        if (CoordinateGridCheck != null)
            CoordinateGridCheck.Visibility = is3d ? Visibility.Collapsed : Visibility.Visible;
        if (CartographyOverlayCheck != null)
            CartographyOverlayCheck.Visibility = is3d ? Visibility.Collapsed : Visibility.Visible;

        if (PlanViewFooterHint != null)
        {
            PlanViewFooterHint.Text = is3d
                ? "3D MODEL — drag to orbit the cave, mouse wheel to zoom, click a station label to select. Sidebar: 3D tools (labels, fly-through, export OBJ/glTF)."
                : "Plan — survey metres (CaveAI Pro reduction). View and QC only; edit walls and symbols in SKETCH EDITOR. Zoom: wheel (toward cursor) or toolbar. Pan: left or middle drag. Double-click map (Pan mode): reset view.";
        }
    }

    private void SyncSectionCutAxisCombo(string? persisted)
    {
        if (Viewport3DSectionCutAxisCombo == null)
            return;
        var key = string.IsNullOrWhiteSpace(persisted) ? "HorizontalZ" : persisted.Trim();
        foreach (ComboBoxItem item in Viewport3DSectionCutAxisCombo.Items)
        {
            if (item.Tag is string t && string.Equals(t, key, StringComparison.OrdinalIgnoreCase))
            {
                Viewport3DSectionCutAxisCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void OnViewport3DPick(SurveyPickResult? pick)
    {
        if (pick == null)
        {
            _surveyPickHighlight = null;
            ResetPropertiesPanelToSummary();
            CaveViewport3DPresenter.SetHighlightStation(HostViewport3D, null);
            return;
        }

        UpdatePropertiesPanel(pick);
        if (pick is SurveyPickStation st)
        {
            SurveyStationSelectionHub.Select(st.Name.Trim(), "3D");
            CaveViewport3DPresenter.SetHighlightStation(HostViewport3D, st.Name.Trim());
        }
        else
        {
            CaveViewport3DPresenter.SetHighlightStation(HostViewport3D, null);
        }
    }

    private double CurrentViewport3DLabelScale()
    {
        var size = Viewport3DLabelSizeCombo?.SelectedItem is ComboBoxItem { Tag: string tag }
            ? tag
            : AppUiSettingsStore.LoadOrDefault().Plan.Viewport3DLabelSize;
        return Viewport3DLabelSizeScale.Factor(size);
    }

    private void SyncViewport3DLabelSizeCombo(string? persisted)
    {
        if (Viewport3DLabelSizeCombo == null)
            return;
        var key = string.IsNullOrWhiteSpace(persisted) ? Viewport3DLabelSizeScale.Medium : persisted.Trim();
        foreach (ComboBoxItem item in Viewport3DLabelSizeCombo.Items)
        {
            if (item.Tag is string t && string.Equals(t, key, StringComparison.OrdinalIgnoreCase))
            {
                Viewport3DLabelSizeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SyncViewport3DTubeQualityCombo(string? persisted)
    {
        if (Viewport3DTubeQualityCombo == null)
            return;
        var key = string.IsNullOrWhiteSpace(persisted) ? nameof(TubeMeshQuality.Standard) : persisted.Trim();
        foreach (ComboBoxItem item in Viewport3DTubeQualityCombo.Items)
        {
            if (item.Tag is string t && string.Equals(t, key, StringComparison.OrdinalIgnoreCase))
            {
                Viewport3DTubeQualityCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void OnViewport3DLabelClick(Viewport3DLabelEntry entry)
    {
        switch (entry.Kind)
        {
            case Viewport3DLabelKind.Station when !string.IsNullOrWhiteSpace(entry.TargetStation):
            {
                var station = entry.TargetStation.Trim();
                SurveyStationSelectionHub.Select(station, "3D");
                CaveViewport3DPresenter.SetHighlightStation(HostViewport3D, station);
                if (Project != null)
                {
                    var coords = SurveyStationGeometry.CalculatePlanCoordinates(Project);
                    if (coords.TryGetValue(station, out var c))
                        UpdatePropertiesPanel(new SurveyPickStation(station, c));
                }

                SurveyWorkspaceNavigator.JumpToStation(station, "3D");
                break;
            }
            case Viewport3DLabelKind.Symbol when !string.IsNullOrWhiteSpace(entry.TargetStation):
            {
                var station = entry.TargetStation.Trim();
                SurveyStationSelectionHub.Select(station, "3D");
                CaveViewport3DPresenter.SetHighlightStation(HostViewport3D, station);
                SurveyWorkspaceNavigator.JumpToStation(station, "3D");
                break;
            }
            case Viewport3DLabelKind.FieldCatalog:
                SurveyWorkspaceNavigator.OpenGeoBioTab();
                break;
        }
    }

    private void Viewport3DSurveyLabelsPreset_Click(object sender, RoutedEventArgs e)
    {
        _applyingSettings = true;
        try
        {
            if (Viewport3DShowLabelsCheck != null)
                Viewport3DShowLabelsCheck.IsChecked = true;
            if (StationNamesCheck != null)
                StationNamesCheck.IsChecked = true;
            if (StationZDepthCheck != null)
                StationZDepthCheck.IsChecked = false;
            if (LegSurveyDetailsCheck != null)
                LegSurveyDetailsCheck.IsChecked = false;
            if (StationEnvironmentCheck != null)
                StationEnvironmentCheck.IsChecked = false;
            if (DepthSpanAnnotationsCheck != null)
                DepthSpanAnnotationsCheck.IsChecked = false;
            if (BracketMarkersCheck != null)
                BracketMarkersCheck.IsChecked = false;
            if (Viewport3DMapSymbolsCheck != null)
                Viewport3DMapSymbolsCheck.IsChecked = true;
            if (Viewport3DFieldCatalogCheck != null)
                Viewport3DFieldCatalogCheck.IsChecked = true;
            if (Viewport3DStationSnapshotsCheck != null)
                Viewport3DStationSnapshotsCheck.IsChecked = false;
            if (Viewport3DAiTagsCheck != null)
                Viewport3DAiTagsCheck.IsChecked = false;
        }
        finally
        {
            _applyingSettings = false;
        }

        UpdateCartographySidebarVisibility();
        Redraw();
        PersistPlanTab();
    }

    private void Viewport3DResetLabels_Click(object sender, RoutedEventArgs e)
    {
        AppUiSettingsStore.ResetViewport3DLabels();
        ApplyPlanTabFromSettings();
        UpdateCartographySidebarVisibility();
        Redraw();
    }

    private void Viewport3DFloatShowLabels_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
            return;

        _applyingSettings = true;
        try
        {
            if (Viewport3DShowLabelsCheck != null && Viewport3DFloatShowLabelsCheck != null)
                Viewport3DShowLabelsCheck.IsChecked = Viewport3DFloatShowLabelsCheck.IsChecked;
        }
        finally
        {
            _applyingSettings = false;
        }

        CartographyOptions_Changed(sender, e);
    }

    private void Viewport3DCleanPreset_Click(object sender, RoutedEventArgs e)
    {
        _applyingSettings = true;
        try
        {
            if (Viewport3DShowLabelsCheck != null)
                Viewport3DShowLabelsCheck.IsChecked = false;
            if (StationNamesCheck != null)
                StationNamesCheck.IsChecked = true;
            if (StationZDepthCheck != null)
                StationZDepthCheck.IsChecked = false;
            if (LegSurveyDetailsCheck != null)
                LegSurveyDetailsCheck.IsChecked = false;
            if (StationEnvironmentCheck != null)
                StationEnvironmentCheck.IsChecked = false;
            if (DepthSpanAnnotationsCheck != null)
                DepthSpanAnnotationsCheck.IsChecked = false;
            if (BracketMarkersCheck != null)
                BracketMarkersCheck.IsChecked = false;
            if (Viewport3DMapSymbolsCheck != null)
                Viewport3DMapSymbolsCheck.IsChecked = false;
            if (Viewport3DFieldCatalogCheck != null)
                Viewport3DFieldCatalogCheck.IsChecked = false;
            if (Viewport3DStationSnapshotsCheck != null)
                Viewport3DStationSnapshotsCheck.IsChecked = false;
            if (Viewport3DAiTagsCheck != null)
                Viewport3DAiTagsCheck.IsChecked = false;
            if (Viewport3DShowSplinesCheck != null)
                Viewport3DShowSplinesCheck.IsChecked = false;
            if (Viewport3DTopographyCheck != null)
                Viewport3DTopographyCheck.IsChecked = false;
            if (Viewport3DDemCheck != null)
                Viewport3DDemCheck.IsChecked = false;
            if (Viewport3DSectionCutCheck != null)
                Viewport3DSectionCutCheck.IsChecked = false;
        }
        finally
        {
            _applyingSettings = false;
        }

        UpdateCartographySidebarVisibility();
        Redraw();
        PersistPlanTab();
    }

    private void Viewport3DCompetitivePreset_Click(object sender, RoutedEventArgs e)
    {
        _applyingSettings = true;
        try
        {
            var stationCount = Project?.Shots
                .SelectMany(s => new[] { s.FromStation, s.ToStation })
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() ?? 0;

            SyncViewport3DLabelSizeCombo(Viewport3DLabelSizeScale.Medium);
            SyncViewport3DTubeQualityCombo(nameof(TubeMeshQuality.High));

            if (Viewport3DShowLabelsCheck != null)
                Viewport3DShowLabelsCheck.IsChecked = stationCount <= 48;
            if (StationNamesCheck != null)
                StationNamesCheck.IsChecked = stationCount <= 48;
            if (StationZDepthCheck != null)
                StationZDepthCheck.IsChecked = false;
            if (LegSurveyDetailsCheck != null)
                LegSurveyDetailsCheck.IsChecked = false;
            if (StationEnvironmentCheck != null)
                StationEnvironmentCheck.IsChecked = false;
            if (DepthSpanAnnotationsCheck != null)
                DepthSpanAnnotationsCheck.IsChecked = false;
            if (BracketMarkersCheck != null)
                BracketMarkersCheck.IsChecked = false;
            if (Viewport3DMapSymbolsCheck != null)
                Viewport3DMapSymbolsCheck.IsChecked = false;
            if (Viewport3DFieldCatalogCheck != null)
                Viewport3DFieldCatalogCheck.IsChecked = false;
            if (Viewport3DStationSnapshotsCheck != null)
                Viewport3DStationSnapshotsCheck.IsChecked = false;
            if (Viewport3DAiTagsCheck != null)
                Viewport3DAiTagsCheck.IsChecked = false;
            if (Viewport3DShowSplinesCheck != null)
                Viewport3DShowSplinesCheck.IsChecked = true;
            if (Viewport3DTopographyCheck != null)
                Viewport3DTopographyCheck.IsChecked = false;
            if (Viewport3DDemCheck != null)
                Viewport3DDemCheck.IsChecked = false;
            if (Viewport3DSectionCutCheck != null)
                Viewport3DSectionCutCheck.IsChecked = false;

            var all = AppUiSettingsStore.LoadOrDefault();
            all.Plan.Viewport3DTubeQuality = nameof(TubeMeshQuality.High);
            all.Plan.Viewport3DLabelSize = Viewport3DLabelSizeScale.Medium;
            AppUiSettingsStore.Save(all);
        }
        finally
        {
            _applyingSettings = false;
        }

        UpdateCartographySidebarVisibility();
        Redraw();
        PersistPlanTab();
    }

    private void Viewport3DFlyThrough_Click(object sender, RoutedEventArgs e)
    {
        if (Project == null || HostViewport3D == null || HostViewportShell == null)
            return;

        if (_flyThrough?.IsRunning == true)
        {
            _flyThrough.Stop();
            _flyThrough.Dispose();
            _flyThrough = null;
            Viewport3DFlyThroughButton!.Content = "Fly-through";
            return;
        }

        _flyThroughRecorder?.Stop();
        _flyThroughRecorder?.Dispose();
        _flyThroughRecorder = null;

        var path = CaveViewport3DFlyThroughPathBuilder.BuildPath(Project);
        if (path.Count < 2)
        {
            MessageBox.Show(
                "Fly-through needs at least two centerline samples from traverse legs with station coordinates.\n\n" +
                "Add connected traverse shots, then reopen the 3D MODEL tab.",
                "Fly-through",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _flyThrough?.Dispose();
        _flyThrough = new CaveViewport3DFlyThrough(
            HostViewport3D,
            HostViewportShell,
            Project,
            Viewport3DLabelCanvas,
            SurveyHudPanel);
        _flyThrough.Completed += () =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (Viewport3DFlyThroughButton != null)
                    Viewport3DFlyThroughButton.Content = "Fly-through";
            });
        };
        Viewport3DFlyThroughButton!.Content = "Stop fly-through";
        _flyThrough.Start();
    }

    private void Export3DPng_Click(object sender, RoutedEventArgs e)
    {
        if (Project == null)
            return;

        if (VisualizationMode != SurveyVisualizationMode.Pseudo3D || HostViewportShell == null)
        {
            MessageBox.Show("Switch to the 3D MODEL tab to export the current viewport.", "Export 3D PNG",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export 3D PNG",
            Filter = "PNG|*.png",
            FileName = SanitizeFileName(Project.Name) + "_3d.png",
        };
        if (dlg.ShowDialog() != true)
            return;

        var png = CaptureViewport3DPngBytes(MapExportQuality.Print);
        if (png == null)
        {
            MessageBox.Show("Could not render 3D PNG.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        File.WriteAllBytes(dlg.FileName, png);
        PostExportStatus(dlg.FileName, "3D viewport PNG");
    }

    private void Export3DObj_Click(object sender, RoutedEventArgs e) =>
        Export3DMesh(Viewport3DMeshExporter.ExportFormat.Obj, "OBJ|*.obj");

    private void Export3DGltf_Click(object sender, RoutedEventArgs e) =>
        Export3DMesh(Viewport3DMeshExporter.ExportFormat.Gltf, "glTF|*.gltf");

    private void Export3DMesh(Viewport3DMeshExporter.ExportFormat format, string filter)
    {
        if (Project == null)
            return;

        var ext = format == Viewport3DMeshExporter.ExportFormat.Obj ? ".obj" : ".gltf";
        var dlg = new SaveFileDialog
        {
            Title = "Export 3D mesh",
            Filter = filter,
            FileName = SanitizeFileName(Project.Name) + "_3d" + ext,
        };
        if (dlg.ShowDialog() != true)
            return;

        if (!Viewport3DMeshExporter.TryExport(Project, dlg.FileName, format, CurrentViewport3DDisplayOptions()))
            MessageBox.Show("Could not export 3D mesh.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void RecordFlyThrough_Click(object sender, RoutedEventArgs e)
    {
        if (Project == null || HostViewport3D == null || HostViewportShell == null)
            return;

        var path = CaveViewport3DFlyThroughPathBuilder.BuildPath(Project);
        if (path.Count < 2)
        {
            MessageBox.Show(
                "Cannot record — no fly-through path. Add connected traverse legs first.",
                "Record fly-through",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _flyThrough?.Stop();
        _flyThrough?.Dispose();
        _flyThrough = null;
        if (Viewport3DFlyThroughButton != null)
            Viewport3DFlyThroughButton.Content = "Fly-through";

        _flyThroughRecorder?.Dispose();
        _flyThroughRecorder = new CaveViewport3DFlyThroughRecorder(
            HostViewport3D,
            HostViewportShell,
            Project,
            120,
            Viewport3DLabelCanvas);

        var finished = new TaskCompletionSource<bool>();
        _flyThroughRecorder.RecordingCompleted += () => finished.TrySetResult(true);
        _flyThroughRecorder.StartRecording();

        await finished.Task.ConfigureAwait(true);

        var mp4Dlg = new SaveFileDialog
        {
            Title = "Save fly-through video (optional)",
            Filter = "MP4|*.mp4|Skip|*.*",
            FileName = SanitizeFileName(Project!.Name) + "_flythrough.mp4",
        };
        string? mp4Path = mp4Dlg.ShowDialog() == true ? mp4Dlg.FileName : null;
        var result = await _flyThroughRecorder.FinishAsync(mp4Path).ConfigureAwait(true);
        _flyThroughRecorder.Dispose();
        _flyThroughRecorder = null;

        if (result.UsedFfmpeg && !string.IsNullOrEmpty(result.Mp4Path))
        {
            MessageBox.Show($"Saved MP4:\n{result.Mp4Path}", "Fly-through", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                $"Saved {result.FrameCount} PNG frame(s) to:\n{result.FrameDirectory}\n\n" +
                (result.FrameCount > 0
                    ? "Install ffmpeg on PATH to encode MP4 automatically next time."
                    : "No frames captured."),
                "Fly-through",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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
        if (IsPlanDesignTool(tool))
            return;

        tool = NormalizeNavigationTool(tool);
        _applyingSettings = true;
        try
        {
            _currentTool = tool;
            if (EditorToolPan != null)
                EditorToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
            if (EditorToolSelect != null)
                EditorToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
        }
        finally
        {
            _applyingSettings = false;
        }

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
            FileName = SanitizeFileName(Project?.Name) +
                       (VisualizationMode == SurveyVisualizationMode.LongProfile ? "-long-profile.png" : "-plan.png"),
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
            SurveySvgExporter.WritePlanSvg(
                p,
                fs,
                SurveyStationGeometry.AndroidViewModePlan,
                VisualizationMode,
                PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan, VisualizationMode));
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
    public byte[]? CapturePlanPngBytes() =>
        VisualizationMode == SurveyVisualizationMode.Pseudo3D
            && HostViewportShell != null
            && HostViewportShell.Visibility == Visibility.Visible
            ? CaptureViewport3DPngBytes(MapExportQuality.Standard)
            : CapturePlan2DPngBytes();

    /// <summary>WYSIWYG 3D viewport PNG (current camera, labels, overlays).</summary>
    public byte[]? CaptureViewport3DPngBytes(MapExportQuality quality = MapExportQuality.Print)
    {
        if (HostViewportShell == null || HostViewportShell.Visibility != Visibility.Visible)
            return null;

        if (HostViewportShell.ActualWidth < 8 || HostViewportShell.ActualHeight < 8)
        {
            var gw = MapHostGrid != null ? MapHostGrid.ActualWidth : 0;
            var gh = MapHostGrid != null ? MapHostGrid.ActualHeight : 0;
            var w = gw > 8 ? Math.Max(320, gw) : 960;
            var h = gh > 8 ? Math.Max(240, gh) : 640;
            HostViewportShell.Measure(new Size(w, h));
            HostViewportShell.Arrange(new Rect(0, 0, w, h));
            HostViewportShell.UpdateLayout();
        }

        return PlanMapRasterExporter.TryCaptureViewport3DPngWysiwyg(HostViewportShell, quality);
    }

    private byte[]? CapturePlan2DPngBytes()
    {
        var p = Project;
        if (p == null)
            return null;

        BeforePlanExport?.Invoke(p);

        var quality = SelectedMapExportQuality();
        var underlays = LoadPlanUnderlays(p);
        var designOverlay = ResolveDesignLayerForExport?.Invoke();

        return PlanMapRasterExporter.TryCapturePlanPngHighRes(
            p,
            VisualizationMode,
            PrintHiContrastCheck?.IsChecked == true,
            PlanCanvasDrawOptionsFactory.ForRasterExport(
                SurveyCanvasKind.Plan,
                quality,
                VisualizationMode,
                p,
                showWallHatching: WallHatchingCheck?.IsChecked == true),
            underlays,
            ZipPath,
            quality: quality,
            designOverlay: designOverlay);
    }

    /// <inheritdoc />
    public void ShowPrintPreview()
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

                SurveyMapPrintWorkflow.ShowPreview(
                    Window.GetWindow(this),
                    p,
                    SurveyMapPrintKind.Plan3D,
                    HostViewportShell,
                    cartography: null);
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
            var underlaysPrint = LoadPlanUnderlays(p);
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
                SurveyMapPrintKind.Plan,
                MapZoomRoot,
                hiContrast,
                titleSuffix,
                BuildPrintCartographyContext(),
                ResetMapViewAfterPrint);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Print error", MessageBoxButton.OK, MessageBoxImage.Error);
            ResetMapViewAfterPrint();
        }
    }

    private void ResetMapViewAfterPrint()
    {
        if (ZoomPan == null || ZoomScale == null)
            return;
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = ZoomScale.ScaleY = 1;
        Redraw();
    }

    private SurveyMapPrintContext BuildPrintCartographyContext()
    {
        if (!_surveyHitLayoutReady || CartographyOverlayCheck?.IsChecked == false)
        {
            return new SurveyMapPrintContext { ShowCartographyOverlay = false };
        }

        return new SurveyMapPrintContext
        {
            SourcePxPerMetre = _surveyHitLayout.PxPerMetre,
            CanvasKind = SurveyCanvasKind.Plan,
            ShowCartographyOverlay = true,
        };
    }
}
