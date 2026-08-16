using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Services.SketchAssist;
using CaveAiProForWindows.ViewModels;
using Microsoft.Win32;

namespace CaveAiProForWindows.Views;

/// <summary>Plan survey as base map + interactive design layer; shared zoom/pan on <see cref="MapZoomRoot"/>.</summary>
public partial class SketchEditorView : UserControl, IMapSurfaceShortcuts
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(SketchEditorView),
        new PropertyMetadata(null, OnProjectChanged));

    private static void OnProjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SketchEditorView)d;
        v.ScheduleRedraw(SurveyRedrawMode.Full);
        v._cloudPublish?.NotifyProjectChanged(v.Project);
    }

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(SketchEditorView),
        new PropertyMetadata(null, (d, _) => ((SketchEditorView)d).ScheduleRedraw(SurveyRedrawMode.Full)));

    public static readonly DependencyProperty MapRowsProperty = DependencyProperty.Register(
        nameof(MapRows),
        typeof(IEnumerable),
        typeof(SketchEditorView),
        new PropertyMetadata(null, OnMapRowsChanged));

    public static readonly DependencyProperty MapInventoryProperty = DependencyProperty.Register(
        nameof(MapInventory),
        typeof(IEnumerable),
        typeof(SketchEditorView),
        new PropertyMetadata(null, OnMapInventoryChanged));

    public static readonly DependencyProperty VisualizationModeProperty = DependencyProperty.Register(
        nameof(VisualizationMode),
        typeof(SurveyVisualizationMode),
        typeof(SketchEditorView),
        new PropertyMetadata(SurveyVisualizationMode.Standard, OnVisualizationModeChanged));

    private static void OnVisualizationModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SketchEditorView)d;
        if (!v.IsLoaded)
            return;
        v.ZoomPan.X = 0;
        v.ZoomPan.Y = 0;
        v.ZoomScale.ScaleX = 1;
        v.ZoomScale.ScaleY = 1;
        v.ScheduleRedraw(SurveyRedrawMode.Full);
        v.PersistSketchTab();
    }

    private static void OnMapRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SketchEditorView)d;
        v.UnwireMapRows(e.OldValue);
        v.WireMapRows(e.NewValue);
        v.ScheduleRedraw(SurveyRedrawMode.Full);
    }

    private static void OnMapInventoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SketchEditorView)d;
        v.UnwireMapInventory(e.OldValue);
        v.WireMapInventory(e.NewValue);
        v.ScheduleRedraw(SurveyRedrawMode.Full);
    }

    private INotifyCollectionChanged? _wiredMapRows;
    private INotifyCollectionChanged? _wiredMapInventory;

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

    public SurveyVisualizationMode VisualizationMode
    {
        get => (SurveyVisualizationMode)GetValue(VisualizationModeProperty);
        set => SetValue(VisualizationModeProperty, value);
    }

    private MapCanvasEditorController? _editor;
    private MapCanvasEditorTool _currentTool = MapCanvasEditorTool.PanZoom;
    private SketchEditorSymbolKind _stampKind = SketchEditorSymbolKind.RockBlock;
    private MainViewModel? _wiredMainVm;
    private CloudPublishViewModel? _cloudPublish;
    private ProceduralSketchViewModel? _proceduralSketch;
    private SketchEditorViewModel? _sketchPersistence;
    private CaveProjectDocument? _designLayerProjectScope;
    private bool _designLayerHydrated;
    private PlanScene? _interactivePlanScene;
    private PlanCanvasSurveyLayout _surveyHitLayout;
    private bool _surveyHitLayoutReady;
    private bool _applyingSettings;
    private SurveyMapPickHighlight? _surveyPickHighlight;
    private bool _stationDetailsPaneDismissed;

    private enum SurveyRedrawMode
    {
        Full,
        OverlayOnly,
    }

    private bool _redrawQueued;
    private bool _fullRedrawRequired = true;
    private IReadOnlyList<PlanRasterUnderlay>? _cachedUnderlays;
    private DispatcherTimer? _cartographyDebounceTimer;
    private DispatcherTimer? _sizeDebounceTimer;
    private double _inkStrokeWidthPx = SketchStrokeStyleDefaults.DefaultStrokeWidthPx;
    private bool _inkDashedStrokes;
    private string _inkWallProfile = SketchWallInkProfiles.Wall;
    private string? _cursorLrudHint;

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

    public SketchEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += SketchEditorView_DataContextChanged;
        IsVisibleChanged += SketchEditorView_IsVisibleChanged;
    }

    private void SketchEditorView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ScheduleRedraw(SurveyRedrawMode.Full)));
    }

    private void SketchEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        WireMainViewModel(e.NewValue as MainViewModel);

    private void WireMainViewModel(MainViewModel? vm)
    {
        if (_wiredMainVm != null)
        {
            _wiredMainVm.SurveyDataChanged -= MainViewModel_SurveyDataChanged;
            _wiredMainVm.PropertyChanged -= MainViewModel_PropertyChanged;
        }

        _wiredMainVm = vm;
        if (_wiredMainVm != null)
        {
            _wiredMainVm.SurveyDataChanged += MainViewModel_SurveyDataChanged;
            _wiredMainVm.PropertyChanged += MainViewModel_PropertyChanged;
        }

        _cloudPublish?.NotifyLegalTermsChanged();
    }

    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LegalTermsAccepted))
            _cloudPublish?.NotifyLegalTermsChanged();
    }

    private void MainViewModel_SurveyDataChanged(object? sender, EventArgs e) =>
        ScheduleRedraw(SurveyRedrawMode.Full);

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

    private void MapRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        ScheduleRedraw(SurveyRedrawMode.Full);

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

    private void MapInventory_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        ScheduleRedraw(SurveyRedrawMode.Full);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SurveyCanvasTheme.Changed += OnSurveyCanvasThemeChanged;
        WireMapRows(MapRows);
        WireMapInventory(MapInventory);
        if (DesignLayer != null && HostScroll != null && ZoomPan != null)
        {
            _editor = new MapCanvasEditorController(
                DesignLayer,
                HostScroll,
                ZoomPan,
                () => _currentTool,
                () => _stampKind,
                () => SketchSnapToStationsCheck?.IsChecked == true,
                SnapCanvasPoint,
                () => _inkStrokeWidthPx,
                () => _inkDashedStrokes,
                () => _inkWallProfile);
        }

        PopulateSymbolLegend();
        ApplySketchTabFromSettings();
        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        else if (SymbolPaletteRock != null)
            SymbolPaletteRock.IsChecked = true;
        SyncSymbolPaletteEnabled();
        WireMainViewModel(DataContext as MainViewModel);
        EnsureCloudPublishViewModel();
        EnsureProceduralSketchViewModel();
        EnsureSketchPersistenceViewModel();
        if (_editor != null)
        {
            _editor.DesignLayerModified = SyncDesignLayerToInMemoryProject;
            _editor.InkAdded = OnInkAdded;
            _editor.InkRemoved = OnInkRemoved;
        }

        if (SketchEditToolsPanel != null)
            SketchEditToolsPanel.DataContext = _sketchPersistence;
        ResetPropertiesPanelToSummary();
        RedrawImmediate();
        UpdateEditorStatusBar();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            ApplyDeferredSketchZoomFromSettings();
            ResetPropertiesPanelToSummary();
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PersistSketchTab();
        _cartographyDebounceTimer?.Stop();
        _sizeDebounceTimer?.Stop();
        SurveyCanvasTheme.Changed -= OnSurveyCanvasThemeChanged;
        WireMainViewModel(null);
        UnwireMapRows(MapRows);
        UnwireMapInventory(MapInventory);
    }

    private void OnSurveyCanvasThemeChanged() =>
        ScheduleRedraw(SurveyRedrawMode.Full);

    private void MaybeResetDesignLayerForProjectChange()
    {
        if (DesignLayer == null)
            return;
        if (ReferenceEquals(Project, _designLayerProjectScope))
            return;
        _designLayerProjectScope = Project;
        _designLayerHydrated = false;
        _cachedUnderlays = null;
        _interactivePlanScene = null;
        _surveyHitLayoutReady = false;
        DesignLayer.Children.Clear();
        _editor?.OnDesignLayerCleared();
        _sketchPersistence?.ClearHistory();
        InvalidateSurveyPickState(true);
    }

    private void InvalidateSurveyPickState(bool clearDetails)
    {
        _interactivePlanScene = null;
        _surveyHitLayoutReady = false;
        _surveyPickHighlight = null;
        _stationDetailsPaneDismissed = false;
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
        switch (pick)
        {
            case SurveyPickStation station:
            {
                if (PropertyPaneTitleText != null)
                    PropertyPaneTitleText.Text = "Station details";
                ApplyPropertyCaptionState(PropertyCaptionStyle.Station);

                _surveyPickHighlight = new SurveyMapPickHighlight(false, station.Name.Trim(), null);
                PropertySelectionStatusText.Text = "Selected station";
                ApplyPropertiesVisualState(PropertiesVisualState.Station);
                PropertyStationLegNameText.Text = station.Name;
                var zTxt = $"Z elevation {station.Coord.Z.ToString("0.###", inv)} m (plan reduction)";
                var xyTxt =
                    $"Local X {station.Coord.X.ToString("0.###", inv)} m  ·  Local Y {station.Coord.Y.ToString("0.###", inv)} m";
                PropertyCoordinatesText.Text = $"{xyTxt}\n{zTxt}";

                var shot = SurveyStationInspector.TryGetRepresentativeShotForStation(Project, station.Name);
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
                }
                else
                {
                    PropertySurveyDataText.Text = "No recorded shot resolves this station (check traverse legs).";
                    PropertyWallDimensionsText.Text = "-";
                }

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

    private void ApplySketchTabFromSettings()
    {
        _applyingSettings = true;
        try
        {
            var s = AppUiSettingsStore.LoadOrDefault().Sketch;
            if (Enum.TryParse(s.Tool, out MapCanvasEditorTool t))
                _currentTool = t;
            else
                _currentTool = MapCanvasEditorTool.PanZoom;
            if (SketchStationNamesCheck != null)
                SketchStationNamesCheck.IsChecked = s.StationNames;
            if (SketchStationZDepthCheck != null)
                SketchStationZDepthCheck.IsChecked = s.StationZ;
            if (SketchLegSurveyDetailsCheck != null)
                SketchLegSurveyDetailsCheck.IsChecked = s.LegSurveyDetails;
            if (SketchStationEnvironmentCheck != null)
                SketchStationEnvironmentCheck.IsChecked = s.StationEnvironment;
            if (SketchDepthSpanAnnotationsCheck != null)
                SketchDepthSpanAnnotationsCheck.IsChecked = s.DepthSpanAnnotations;
            if (SketchBracketMarkersCheck != null)
                SketchBracketMarkersCheck.IsChecked = s.BracketMarkers;
            if (SketchCartographyOverlayCheck != null)
                SketchCartographyOverlayCheck.IsChecked = s.Overlay;
            if (SketchSnapToStationsCheck != null)
                SketchSnapToStationsCheck.IsChecked = s.SnapToStations;
            _inkStrokeWidthPx = s.InkStrokeWidthPx > 0 ? s.InkStrokeWidthPx : SketchStrokeStyleDefaults.DefaultStrokeWidthPx;
            _inkDashedStrokes = s.InkDashedStrokes;
            _inkWallProfile = NormalizeWallProfile(s.InkWallProfile);
            SyncInkStrokeWidthCombo(_inkStrokeWidthPx);
            SyncInkWallProfileCombo(_inkWallProfile);
            if (InkDashedStrokesCheck != null)
                InkDashedStrokesCheck.IsChecked = _inkDashedStrokes;
            SyncCartographicIntensityCombo(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);
            if (SketchToolPan != null && SketchToolSelect != null && SketchToolDraw != null
                && SketchToolLine != null && SketchToolSymbol != null && SketchToolErase != null)
            {
                SketchToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
                SketchToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
                SketchToolDraw.IsChecked = _currentTool == MapCanvasEditorTool.DrawFreehand;
                SketchToolLine.IsChecked = _currentTool == MapCanvasEditorTool.DrawLine;
                SketchToolSymbol.IsChecked = _currentTool == MapCanvasEditorTool.PlaceSymbol;
                SketchToolErase.IsChecked = _currentTool == MapCanvasEditorTool.Erase;
            }
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void ApplyDeferredSketchZoomFromSettings()
    {
        var s = AppUiSettingsStore.LoadOrDefault().Sketch;
        if (ZoomScale == null || ZoomPan == null)
            return;
        var zx = Math.Clamp(s.ZoomScale > 0 ? s.ZoomScale : 1, 0.12, 12.0);
        ZoomScale.ScaleX = zx;
        ZoomScale.ScaleY = zx;
        ZoomPan.X = s.PanX;
        ZoomPan.Y = s.PanY;
    }

    private void PersistSketchTab()
    {
        if (_applyingSettings)
            return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.Sketch.Tool = _currentTool.ToString();
        all.Sketch.StationNames = SketchStationNamesCheck?.IsChecked != false;
        all.Sketch.StationZ = SketchStationZDepthCheck?.IsChecked == true;
        all.Sketch.LegSurveyDetails = SketchLegSurveyDetailsCheck?.IsChecked != false;
        all.Sketch.StationEnvironment = SketchStationEnvironmentCheck?.IsChecked != false;
        all.Sketch.DepthSpanAnnotations = SketchDepthSpanAnnotationsCheck?.IsChecked != false;
        all.Sketch.BracketMarkers = SketchBracketMarkersCheck?.IsChecked != false;
        all.Sketch.Overlay = SketchCartographyOverlayCheck?.IsChecked != false;
        all.Sketch.SnapToStations = SketchSnapToStationsCheck?.IsChecked == true;
        all.Sketch.InkStrokeWidthPx = _inkStrokeWidthPx;
        all.Sketch.InkDashedStrokes = _inkDashedStrokes;
        all.Sketch.InkWallProfile = _inkWallProfile;
        if (CartographicIntensityCombo?.SelectedItem is ComboBoxItem { Tag: string tag })
            all.CartographicIntensity = tag;
        if (ZoomScale != null)
            all.Sketch.ZoomScale = ZoomScale.ScaleX;
        if (ZoomPan != null)
        {
            all.Sketch.PanX = ZoomPan.X;
            all.Sketch.PanY = ZoomPan.Y;
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

    private PlanCanvasDrawOptions SketchDrawOptions()
    {
        var intensity = CartographicIntensityParser.Parse(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);
        return PlanCanvasDrawOptions.ForPlan(
            SketchStationNamesCheck?.IsChecked != false,
            SketchCartographyOverlayCheck?.IsChecked != false,
            VisualizationMode,
            SketchStationZDepthCheck?.IsChecked == true,
            intensity,
            _surveyPickHighlight,
            SketchLegSurveyDetailsCheck?.IsChecked != false,
            SketchStationEnvironmentCheck?.IsChecked != false,
            SketchDepthSpanAnnotationsCheck?.IsChecked != false,
            SketchBracketMarkersCheck?.IsChecked != false,
            showPersistedSketchInk: false);
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
        ScheduleRedraw(SurveyRedrawMode.Full);
    }

    private void RestartCartographyDebounceTimer()
    {
        _cartographyDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _cartographyDebounceTimer.Stop();
        _cartographyDebounceTimer.Tick -= CartographyDebounceTimer_Tick;
        _cartographyDebounceTimer.Tick += CartographyDebounceTimer_Tick;
        _cartographyDebounceTimer.Start();
    }

    private void CartographyDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _cartographyDebounceTimer?.Stop();
        ScheduleRedraw(SurveyRedrawMode.Full);
    }

    private void CartographyOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
            return;
        RestartCartographyDebounceTimer();
        PersistSketchTab();
    }

    private void ScheduleRedraw(SurveyRedrawMode mode)
    {
        if (mode == SurveyRedrawMode.Full)
            _fullRedrawRequired = true;

        if (_redrawQueued)
            return;

        _redrawQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _redrawQueued = false;
            if (_fullRedrawRequired)
            {
                _fullRedrawRequired = false;
                RedrawFull();
            }
            else
                RedrawSurveyOverlayOnly();
        });
    }

    private void RedrawImmediate() => RedrawFull();

    private void Redraw() => ScheduleRedraw(SurveyRedrawMode.Full);

    private void RedrawSurveyOverlayOnly()
    {
        if (SurveyCanvas == null || DesignLayer == null || ZoomScale == null)
            return;

        var p = Project;
        if (p == null || _interactivePlanScene == null || !_surveyHitLayoutReady)
        {
            RedrawFull();
            return;
        }

        try
        {
            SurveyCanvas.Children.Clear();
            ZoomScale.CenterX = SurveyCanvas.Width / 2;
            ZoomScale.CenterY = SurveyCanvas.Height / 2;
            var underlays = _cachedUnderlays ?? Array.Empty<PlanRasterUnderlay>();
            PlanCanvasRenderer.Draw(
                _interactivePlanScene,
                SurveyCanvas,
                highContrast: false,
                SurveyCanvas.Width,
                SurveyCanvas.Height,
                underlays,
                p,
                ZipPath,
                SketchDrawOptions());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SketchEditorView] Overlay redraw failed: {ex}");
            RedrawFull();
        }
        finally
        {
            RefreshSurveyHud();
            UpdateEditorStatusBar();
        }
    }

    private void RedrawFull()
    {
        if (SurveyCanvas == null || DesignLayer == null || MapZoomRoot == null || HostScroll == null)
            return;

        try
        {
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
                        + "If cartography paths resolve to raster files inside the opened .zip, an underlay can appear here even without traverse.");
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
                    SketchDrawOptions());
                _interactivePlanScene = scene;
                _surveyHitLayoutReady = PlanCanvasRenderer.TryComputeSurveyLayout(
                    scene, SurveyCanvas.Width, SurveyCanvas.Height, out _surveyHitLayout);
                _cachedUnderlays = underlays;
                AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                MaybeHydrateDesignLayerFromProject(p);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SketchEditorView] Redraw failed: {ex}");
                SurveyCanvas.Children.Clear();
                AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                InvalidateSurveyPickState(true);
                MessageBox.Show(
                    $"Sketch editor could not render the plan for this project.\n\n{ex.Message}",
                    "Sketch map render error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                AddMessage($"Render error: {ex.Message}");
            }
        }
        finally
        {
            RefreshSurveyHud();
            UpdateEditorStatusBar();
        }
    }

    private void SyncInkStrokeWidthCombo(double widthPx)
    {
        if (InkStrokeWidthCombo == null)
            return;
        var key = widthPx.ToString("0.##", CultureInfo.InvariantCulture);
        foreach (ComboBoxItem item in InkStrokeWidthCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, key, StringComparison.OrdinalIgnoreCase))
            {
                InkStrokeWidthCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void InkStrokeStyle_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
            return;
        if (InkStrokeWidthCombo?.SelectedItem is ComboBoxItem { Tag: string tag }
            && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            _inkStrokeWidthPx = Math.Clamp(w, 0.5, 8);
        _inkDashedStrokes = InkDashedStrokesCheck?.IsChecked == true;
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private void InkWallProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings)
            return;
        if (InkWallProfileCombo?.SelectedItem is ComboBoxItem { Tag: string tag })
            _inkWallProfile = NormalizeWallProfile(tag);
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private static string NormalizeWallProfile(string? profile) =>
        profile switch
        {
            SketchWallInkProfiles.WallEstimated => SketchWallInkProfiles.WallEstimated,
            SketchWallInkProfiles.FillBoundary => SketchWallInkProfiles.FillBoundary,
            SketchWallInkProfiles.Ink => SketchWallInkProfiles.Ink,
            _ => SketchWallInkProfiles.Wall,
        };

    private void SyncInkWallProfileCombo(string profile)
    {
        if (InkWallProfileCombo == null)
            return;
        var normalized = NormalizeWallProfile(profile);
        foreach (ComboBoxItem item in InkWallProfileCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, normalized, StringComparison.OrdinalIgnoreCase))
            {
                InkWallProfileCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void PopulateSymbolLegend()
    {
        if (SymbolLegendList == null)
            return;
        SymbolLegendList.ItemsSource = SketchSymbolPaletteCatalog.All
            .Select(e => $"{e.ShortLabel} — {e.UisName}")
            .ToList();
    }

    private static string WallProfileLabel(string profile) =>
        profile switch
        {
            SketchWallInkProfiles.WallEstimated => "Estimated wall",
            SketchWallInkProfiles.FillBoundary => "Sand/clay edge",
            SketchWallInkProfiles.Ink => "Generic ink",
            _ => "Passage wall",
        };

    private void UpdateEditorStatusBar()
    {
        if (EditorStatusText == null)
            return;

        var toolLabel = _currentTool switch
        {
            MapCanvasEditorTool.PanZoom => "Pan / zoom",
            MapCanvasEditorTool.Select => "Select",
            MapCanvasEditorTool.DrawFreehand => $"Freehand · {WallProfileLabel(_inkWallProfile)}",
            MapCanvasEditorTool.DrawLine => $"Wall line · {WallProfileLabel(_inkWallProfile)}",
            MapCanvasEditorTool.PlaceSymbol => $"Symbol: {SketchSymbolPaletteCatalog.GetUisDisplayName(_stampKind)}",
            MapCanvasEditorTool.Erase => "Erase",
            _ => _currentTool.ToString(),
        };
        var snap = SketchSnapToStationsCheck?.IsChecked == true ? "Snap on" : "Snap off";
        var dash = _inkDashedStrokes && string.Equals(_inkWallProfile, SketchWallInkProfiles.Ink, StringComparison.OrdinalIgnoreCase)
            ? " · Dashed"
            : "";
        var lrud = !string.IsNullOrWhiteSpace(_cursorLrudHint) ? $" · {_cursorLrudHint}" : "";
        EditorStatusText.Text =
            $"{toolLabel} · {snap} · Width {_inkStrokeWidthPx.ToString("0.##", CultureInfo.InvariantCulture)} px{dash}{lrud}";
    }

    private void UpdateCursorLrudHint(Point canvasPoint)
    {
        _cursorLrudHint = null;
        if (SketchSnapToStationsCheck?.IsChecked != true
            || !_surveyHitLayoutReady
            || _interactivePlanScene == null
            || Project == null)
        {
            return;
        }

        if (!SketchStationSnapHelper.TryFindNearestStation(
                canvasPoint, _interactivePlanScene, _surveyHitLayout, out var stationName, out _, 18))
            return;

        var shot = SurveyStationInspector.TryGetRepresentativeShotForStation(Project, stationName);
        if (shot == null)
        {
            _cursorLrudHint = $"Station {stationName}";
            return;
        }

        var inv = CultureInfo.InvariantCulture;
        var (l, r, u, d) = shot.EffectivePlanLrud();
        _cursorLrudHint =
            $"{stationName} LRUD L{l.ToString("0.##", inv)} R{r.ToString("0.##", inv)} U{u.ToString("0.##", inv)} D{d.ToString("0.##", inv)} m";
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

    private void MaybeHydrateDesignLayerFromProject(CaveProjectDocument project)
    {
        if (_designLayerHydrated || DesignLayer == null || !_surveyHitLayoutReady)
            return;
        _designLayerHydrated = true;
        var added = DesignLayerMapObjectsHydrator.TryHydrate(DesignLayer, _surveyHitLayout, project);
        if (added > 0)
            _sketchPersistence?.ClearHistory();
    }

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
        if (SymbolPaletteFlow is { IsChecked: true })
            return;
        if (SymbolPaletteSand is { IsChecked: true })
            return;
        if (SymbolPalettePit is { IsChecked: true })
            return;
        if (SymbolPaletteAid is { IsChecked: true })
            return;
        if (SymbolPaletteChoke is { IsChecked: true })
            return;
        if (SymbolPaletteBreakdown is { IsChecked: true })
            return;
        if (SymbolPaletteColumn is { IsChecked: true })
            return;
        if (SymbolPaletteHelictite is { IsChecked: true })
            return;
        if (SymbolPaletteAven is { IsChecked: true })
            return;
        if (SymbolPaletteStream is { IsChecked: true })
            return;
        if (SymbolPaletteMud is { IsChecked: true })
            return;
        if (SymbolPaletteArchaeology is { IsChecked: true })
            return;
        if (SymbolPaletteGuano is { IsChecked: true })
            return;
        if (SymbolPaletteCrack is { IsChecked: true })
            return;
        SymbolPaletteRock.IsChecked = true;
    }

    private ToggleButton[] SymbolPaletteButtons() =>
    [
        SymbolPaletteRock, SymbolPaletteWater, SymbolPaletteSpele, SymbolPaletteFlow,
        SymbolPaletteSand, SymbolPalettePit, SymbolPaletteAid, SymbolPaletteChoke,
        SymbolPaletteBreakdown, SymbolPaletteColumn, SymbolPaletteHelictite,
        SymbolPaletteAven, SymbolPaletteStream, SymbolPaletteMud, SymbolPaletteArchaeology,
        SymbolPaletteGuano, SymbolPaletteCrack,
    ];

    private void SymbolPalette_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { IsChecked: true } t)
            return;
        _stampKind = t switch
        {
            _ when ReferenceEquals(t, SymbolPaletteRock) => SketchEditorSymbolKind.RockBlock,
            _ when ReferenceEquals(t, SymbolPaletteWater) => SketchEditorSymbolKind.WaterPool,
            _ when ReferenceEquals(t, SymbolPaletteFlow) => SketchEditorSymbolKind.FlowstoneCurtain,
            _ when ReferenceEquals(t, SymbolPaletteSand) => SketchEditorSymbolKind.SandMudFloor,
            _ when ReferenceEquals(t, SymbolPalettePit) => SketchEditorSymbolKind.PitOrShaft,
            _ when ReferenceEquals(t, SymbolPaletteAid) => SketchEditorSymbolKind.FixedAid,
            _ when ReferenceEquals(t, SymbolPaletteChoke) => SketchEditorSymbolKind.Choke,
            _ when ReferenceEquals(t, SymbolPaletteBreakdown) => SketchEditorSymbolKind.BreakdownPile,
            _ when ReferenceEquals(t, SymbolPaletteColumn) => SketchEditorSymbolKind.ColumnPillar,
            _ when ReferenceEquals(t, SymbolPaletteHelictite) => SketchEditorSymbolKind.Helictite,
            _ when ReferenceEquals(t, SymbolPaletteAven) => SketchEditorSymbolKind.AvenShaftUp,
            _ when ReferenceEquals(t, SymbolPaletteStream) => SketchEditorSymbolKind.SubterraneanStream,
            _ when ReferenceEquals(t, SymbolPaletteMud) => SketchEditorSymbolKind.MudDeposit,
            _ when ReferenceEquals(t, SymbolPaletteArchaeology) => SketchEditorSymbolKind.ArchaeologyBones,
            _ when ReferenceEquals(t, SymbolPaletteGuano) => SketchEditorSymbolKind.BatGuano,
            _ when ReferenceEquals(t, SymbolPaletteCrack) => SketchEditorSymbolKind.CrackFissure,
            _ => SketchEditorSymbolKind.StalactiteSpeleothem,
        };
        foreach (ToggleButton sibling in SymbolPaletteButtons())
        {
            if (!ReferenceEquals(sibling, t))
                sibling.IsChecked = false;
        }

        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private void SketchToolPan_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.PanZoom;
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private void SketchToolSelect_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.Select;
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private void SketchToolDraw_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.DrawFreehand;
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private void SketchToolLine_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.DrawLine;
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private void SketchToolSymbol_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
        {
            _currentTool = MapCanvasEditorTool.PlaceSymbol;
            EnsureSymbolPaletteHasSelection();
        }

        SyncSymbolPaletteEnabled();
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private void SketchToolErase_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.Erase;
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private void SketchSnapToStations_Changed(object sender, RoutedEventArgs e)
    {
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    private void SketchSelectAllInk_Click(object sender, RoutedEventArgs e)
    {
        if (_editor == null)
            return;
        _editor.TrySelectTopmostInk(out var total);
        if (total == 0)
        {
            if (EditorStatusText != null)
                EditorStatusText.Text = "No sketch ink on design layer";
            ResetPropertiesPanelToSummary();
            return;
        }

        UpdatePropertiesPanelForInk(_editor.SelectedInk);
        if (EditorStatusText != null)
            EditorStatusText.Text = total == 1
                ? "Selected 1 sketch object"
                : $"Selected topmost of {total} sketch objects on layer";
    }

    private void SketchFitSurvey_Click(object sender, RoutedEventArgs e) => FitMapToSurveyBounds();

    private void SketchDuplicateInk_Click(object sender, RoutedEventArgs e) => TryDuplicateSelectedInk();

    private void SketchClearAllInk_Click(object sender, RoutedEventArgs e)
    {
        if (_editor == null || DesignLayer == null)
            return;
        if (DesignLayer.Children.Count == 0 || _editor.ClearAllUserInk() == 0)
        {
            MessageBox.Show("No sketch ink on the design layer.", "Clear all", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _editor.ClearInkSelection();
        ResetPropertiesPanelToSummary();
    }

    private void SketchExportPng_Click(object sender, RoutedEventArgs e)
    {
        var p = Project;
        if (p == null)
        {
            MessageBox.Show("Open a cave project first.", "Export PNG", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var bytes = CaptureSketchPngBytes();
        if (bytes == null || bytes.Length == 0)
        {
            MessageBox.Show("Nothing to export — load traverse data and ensure the plan renders.",
                "Export PNG", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            FileName = SanitizeExportFileName(p.Name) + "-sketch-plan.png",
        };
        if (dlg.ShowDialog() != true)
            return;
        try
        {
            File.WriteAllBytes(dlg.FileName, bytes);
            if (DataContext is MainViewModel vm)
                vm.StatusMessage = $"Exported sketch plan PNG: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export PNG", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SanitizeExportFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "cave";
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private byte[]? CaptureSketchPngBytes()
    {
        var p = Project;
        if (p == null || SurveyCanvas == null || DesignLayer == null)
            return null;

        TryPersistSessionToProject(p);
        var underlays = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
        return SketchEditorPublishCapture.TryCaptureAiMapPng(
            p,
            VisualizationMode,
            SketchDrawOptions(),
            underlays,
            ZipPath,
            DesignLayer,
            SurveyCanvas.Width,
            SurveyCanvas.Height);
    }

    private Point SnapCanvasPoint(Point canvasPoint)
    {
        if (!_surveyHitLayoutReady || _interactivePlanScene == null)
            return canvasPoint;
        return SketchStationSnapHelper.TrySnap(canvasPoint, _interactivePlanScene, _surveyHitLayout);
    }

    private void SketchMapHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MapZoomRoot == null || SurveyCanvas == null || DesignLayer == null || ZoomScale == null)
            return;
        var innerW = Math.Max(120, e.NewSize.Width);
        var innerH = Math.Max(80, e.NewSize.Height);
        if (innerW < 120 || innerH < 80)
            return;
        MapZoomRoot.Width = innerW;
        MapZoomRoot.Height = innerH;
        SurveyCanvas.Width = innerW;
        SurveyCanvas.Height = innerH;
        DesignLayer.Width = innerW;
        DesignLayer.Height = innerH;
        ZoomScale.CenterX = innerW * 0.5;
        ZoomScale.CenterY = innerH * 0.5;
        RestartSizeDebounceTimer();
    }

    private void RestartSizeDebounceTimer()
    {
        _sizeDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _sizeDebounceTimer.Stop();
        _sizeDebounceTimer.Tick -= SizeDebounceTimer_Tick;
        _sizeDebounceTimer.Tick += SizeDebounceTimer_Tick;
        _sizeDebounceTimer.Start();
    }

    private void SizeDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _sizeDebounceTimer?.Stop();
        ScheduleRedraw(SurveyRedrawMode.Full);
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
        PersistSketchTab();
    }

    private void SketchZoomIn_Click(object sender, RoutedEventArgs e) => MapZoomIn();

    private void SketchZoomOut_Click(object sender, RoutedEventArgs e) => MapZoomOut();

    /// <inheritdoc />
    public void MapZoomIn() => ApplyMapZoom(zoomIn: true);

    /// <inheritdoc />
    public void MapZoomOut() => ApplyMapZoom(zoomIn: false);

    private void ApplyMapZoom(bool zoomIn)
    {
        if (ZoomScale == null || ZoomPan == null)
            return;
        var focus = new Point(ZoomScale.CenterX, ZoomScale.CenterY);
        if (!MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, zoomIn, focus))
            return;
        PersistSketchTab();
    }

    private void DesignLayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _editor?.OnMouseDownForMapPan(sender, e);
        DesignLayer_MapMouseDown(sender, e);
    }

    private void DesignLayer_MouseUp(object sender, MouseButtonEventArgs e) =>
        _editor?.OnMouseUpForMapPan(sender, e);

    private void DesignLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == MapCanvasEditorTool.Select && DesignLayer != null)
        {
            var ptInk = e.GetPosition(DesignLayer);
            if (_editor?.TrySelectInkAt(ptInk) == true)
            {
                UpdatePropertiesPanelForInk(_editor.SelectedInk);
                e.Handled = true;
                return;
            }

            _editor?.ClearInkSelection();
        }

        if (_currentTool == MapCanvasEditorTool.Select
            && _surveyHitLayoutReady
            && _interactivePlanScene != null
            && Project != null
            && SurveyCanvas != null)
        {
            _editor?.ClearTransientSelectHighlight();
            var pt = e.GetPosition(SurveyCanvas);
            var pick = SurveyPlanPickService.TryPick(pt, _surveyHitLayout, _interactivePlanScene, Project);
            if (pick is SurveyPickNone)
            {
                _surveyPickHighlight = null;
                _stationDetailsPaneDismissed = true;
                ApplySurveyDetailsColumnExpanded(false);
                ScheduleRedraw(SurveyRedrawMode.OverlayOnly);
                e.Handled = true;
                return;
            }

            _stationDetailsPaneDismissed = false;
            UpdatePropertiesPanel(pick);
            ScheduleRedraw(SurveyRedrawMode.OverlayOnly);
            e.Handled = true;
            return;
        }

        _editor?.OnMouseLeftButtonDown(sender, e);
    }

    private void DesignLayer_MouseMove(object sender, MouseEventArgs e)
    {
        _editor?.OnMouseMove(sender, e);
        if (DesignLayer == null)
            return;
        UpdateCursorLrudHint(e.GetPosition(DesignLayer));
        UpdateEditorStatusBar();
    }

    private void DesignLayer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _editor?.OnMouseLeftButtonUp(sender, e);

    private void DesignLayer_MouseLeave(object sender, MouseEventArgs e) =>
        _editor?.OnMouseLeave(sender, e);

    private void DesignLayer_MapMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || e.ChangedButton != MouseButton.Left || _currentTool != MapCanvasEditorTool.PanZoom)
            return;
        ResetMapView();
        e.Handled = true;
    }

    public void ClearMapSelectionAndRedraw()
    {
        _surveyPickHighlight = null;
        _stationDetailsPaneDismissed = false;
        _editor?.ClearInkSelection();
        ResetPropertiesPanelToSummary();
        ApplySurveyDetailsColumnExpanded(true);
        Redraw();
    }

    public void UndoSketchEdit()
    {
        if (_sketchPersistence?.CanUndo != true)
            return;
        _sketchPersistence.UndoCommand.Execute(null);
        _editor?.ClearInkSelection();
        ResetPropertiesPanelToSummary();
    }

    public void RedoSketchEdit()
    {
        if (_sketchPersistence?.CanRedo != true)
            return;
        _sketchPersistence.RedoCommand.Execute(null);
        _editor?.ClearInkSelection();
        ResetPropertiesPanelToSummary();
    }

    public bool TryDeleteSelectedInk()
    {
        if (_editor?.SelectedInk == null)
            return false;
        if (!_editor.DeleteSelectedInk())
            return false;
        ResetPropertiesPanelToSummary();
        return true;
    }

    public bool TryDuplicateSelectedInk()
    {
        if (_editor?.SelectedInk == null)
            return false;
        if (!_editor.TryDuplicateSelectedInk())
            return false;
        UpdatePropertiesPanelForInk(_editor.SelectedInk);
        return true;
    }

    public void FitMapToSurveyBounds()
    {
        if (ZoomScale == null || ZoomPan == null || MapZoomRoot == null || HostScroll == null
            || _interactivePlanScene == null || !_surveyHitLayoutReady)
            return;

        var hostW = HostScroll.ViewportWidth > 0 ? HostScroll.ViewportWidth : SketchMapHostGrid.ActualWidth;
        var hostH = HostScroll.ViewportHeight > 0 ? HostScroll.ViewportHeight : SketchMapHostGrid.ActualHeight;
        if (!SketchMapViewFitter.TryComputeFitTransform(
                _interactivePlanScene,
                _surveyHitLayout,
                MapZoomRoot.Width,
                MapZoomRoot.Height,
                hostW,
                hostH,
                out var scale,
                out var panX,
                out var panY,
                out var cx,
                out var cy))
            return;

        ZoomScale.CenterX = cx;
        ZoomScale.CenterY = cy;
        ZoomScale.ScaleX = scale;
        ZoomScale.ScaleY = scale;
        ZoomPan.X = panX;
        ZoomPan.Y = panY;
        PersistSketchTab();
    }

    public void ApplyMapEditorTool(MapCanvasEditorTool tool)
    {
        _applyingSettings = true;
        try
        {
            _currentTool = tool;
            if (SketchToolPan != null)
                SketchToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
            if (SketchToolSelect != null)
                SketchToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
            if (SketchToolDraw != null)
                SketchToolDraw.IsChecked = _currentTool == MapCanvasEditorTool.DrawFreehand;
            if (SketchToolLine != null)
                SketchToolLine.IsChecked = _currentTool == MapCanvasEditorTool.DrawLine;
            if (SketchToolSymbol != null)
                SketchToolSymbol.IsChecked = _currentTool == MapCanvasEditorTool.PlaceSymbol;
            if (SketchToolErase != null)
                SketchToolErase.IsChecked = _currentTool == MapCanvasEditorTool.Erase;
        }
        finally
        {
            _applyingSettings = false;
        }

        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
        UpdateEditorStatusBar();
    }

    public void ResetMapView()
    {
        if (ZoomPan == null || ZoomScale == null)
            return;
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = 1;
        ZoomScale.ScaleY = 1;
        Redraw();
        PersistSketchTab();
    }

    /// <summary>Clears sketch ink, undo history, and map zoom when the workspace is unloaded or replaced.</summary>
    public void ResetForProjectUnload()
    {
        _designLayerProjectScope = null;
        _designLayerHydrated = false;
        _cachedUnderlays = null;
        _interactivePlanScene = null;
        _surveyHitLayoutReady = false;
        DesignLayer?.Children.Clear();
        _editor?.OnDesignLayerCleared();
        _sketchPersistence?.ClearHistory();
        InvalidateSurveyPickState(clearDetails: true);
        ResetMapView();
        RedrawImmediate();
        UpdateEditorStatusBar();
    }

    /// <summary>Flushes design-layer strokes/symbols and AI assets onto <paramref name="project"/> before disk save.</summary>
    public bool TryPersistSessionToProject(CaveProjectDocument project) =>
        _sketchPersistence?.TryPersistSessionToProject(project) ?? false;

    /// <summary>After cartography — focus Procedural Assist and optionally generate LRUD preview.</summary>
    public void BeginDesignFromSurvey(bool runProceduralAssist)
    {
        EnsureProceduralSketchViewModel();
        Redraw();

        var project = Project;
        if (runProceduralAssist && project != null && SurveyDesignWorkflow.HasExistingDesignLayer(project))
            runProceduralAssist = false;

        void TryRun()
        {
            if (runProceduralAssist)
            {
                if (_proceduralSketch?.GenerateProceduralCommand.CanExecute(null) == true)
                    _proceduralSketch.GenerateProceduralCommand.Execute(null);
                else if (_proceduralSketch != null)
                    _proceduralSketch.StatusMessage =
                        "Survey layout ready — click Generate from survey for LRUD wall preview.";
            }
            else if (_proceduralSketch != null && project != null && SurveyDesignWorkflow.HasExistingDesignLayer(project))
            {
                _proceduralSketch.StatusMessage =
                    "Existing design ink loaded on the design layer — edit it directly or use Generate from survey for LRUD assist.";
            }

            ProceduralAssistPanel?.BringIntoView();
        }

        if (_surveyHitLayoutReady)
            TryRun();
        else
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, TryRun);
    }

    /// <summary>Design layer for compositing onto plan PNG exports from the Plan tab.</summary>
    public PlanDesignLayerExportContext? TryGetDesignLayerExportContext()
    {
        if (DesignLayer == null || SurveyCanvas == null ||
            SurveyCanvas.Width < 1 || SurveyCanvas.Height < 1)
            return null;
        return new PlanDesignLayerExportContext(DesignLayer, SurveyCanvas.Width, SurveyCanvas.Height);
    }

    /// <summary>Updates in-memory <c>mapObjects</c> / <c>sketches</c> after each edit (no disk write).</summary>
    private void SyncDesignLayerToInMemoryProject()
    {
        var p = Project;
        if (p == null)
            return;
        EnsureSketchPersistenceViewModel();
        _sketchPersistence?.TryPersistSessionToProject(p);
        _wiredMainVm?.MarkDirty("Sketch updated — Ctrl+S to save");
    }

    private void OnInkAdded(UIElement element)
    {
        EnsureSketchPersistenceViewModel();
        _sketchPersistence?.RecordInkAdded(element);
    }

    private void OnInkRemoved(UIElement element, int index)
    {
        EnsureSketchPersistenceViewModel();
        _sketchPersistence?.RecordInkRemoved(element, index);
    }

    private void UpdatePropertiesPanelForInk(UIElement? ink)
    {
        if (PropertySelectionStatusText == null
            || PropertyStationLegNameText == null
            || PropertyCoordinatesText == null
            || PropertySurveyDataText == null
            || PropertyWallDimensionsText == null)
            return;

        _surveyPickHighlight = null;
        ScheduleRedraw(SurveyRedrawMode.OverlayOnly);

        if (ink == null)
        {
            ResetPropertiesPanelToSummary();
            return;
        }

        if (PropertyPaneTitleText != null)
            PropertyPaneTitleText.Text = "Sketch object";
        ApplyPropertyCaptionState(PropertyCaptionStyle.Overview);
        ApplyPropertiesVisualState(PropertiesVisualState.None);

        switch (ink)
        {
            case Polyline poly:
                PropertySelectionStatusText.Text = "Selected freehand stroke";
                PropertyStationLegNameText.Text = "Design-layer stroke";
                PropertyCoordinatesText.Text = $"{poly.Points.Count} canvas points · press Delete to remove";
                PropertySurveyDataText.Text = "Saved to mapObjects on next sync / Save project";
                PropertyWallDimensionsText.Text = "Use Erase mode or Undo (Ctrl+Z) to revert edits";
                SetPropertyAndroidPayloadText("—");
                break;
            case Viewbox:
                PropertySelectionStatusText.Text = "Selected symbol stamp";
                PropertyStationLegNameText.Text = "Design-layer symbol";
                PropertyCoordinatesText.Text = "Press Delete to remove this stamp";
                PropertySurveyDataText.Text = "Saved to mapObjects on next sync / Save project";
                PropertyWallDimensionsText.Text = "Use Erase mode or Undo (Ctrl+Z) to revert edits";
                SetPropertyAndroidPayloadText("—");
                break;
            default:
                PropertySelectionStatusText.Text = "Selected sketch object";
                PropertyStationLegNameText.Text = ink.GetType().Name;
                PropertyCoordinatesText.Text = "Press Delete to remove";
                PropertySurveyDataText.Text = "—";
                PropertyWallDimensionsText.Text = "—";
                SetPropertyAndroidPayloadText("—");
                break;
        }

        ApplySurveyDetailsColumnExpanded(true);
    }

    private void EnsureSketchPersistenceViewModel()
    {
        if (_sketchPersistence != null)
            return;

        _sketchPersistence = new SketchEditorViewModel(new SketchEditorPersistenceHost
        {
            GetProject = () => Project,
            GetDesignLayer = () => DesignLayer,
            GetSurveyLayout = () => _surveyHitLayout,
            IsSurveyLayoutReady = () => _surveyHitLayoutReady && Project != null,
            CaptureStructureMask = () =>
            {
                var p = Project;
                if (p == null || SurveyCanvas == null || DesignLayer == null)
                    return null;
                return CloudPublishService.TryCaptureStructureMask(
                    p,
                    DesignLayer,
                    SurveyCanvas.Width,
                    SurveyCanvas.Height);
            },
            GetPrimarySourcePath = () => _wiredMainVm?.PrimarySourceFilePath,
        });
    }

    private void EnsureCloudPublishViewModel()
    {
        if (_cloudPublish != null || CloudPublishPanel == null)
            return;

        _cloudPublish = new CloudPublishViewModel(new CloudPublishEditorHost
        {
            GetProject = () => Project,
            GetLegalTermsAccepted = () => _wiredMainVm?.LegalTermsAccepted == true,
            CaptureArtifacts = TryCapturePublishArtifacts,
            GetOwnerWindow = () => Window.GetWindow(this),
            PersistLinkedLibraryCaveId = _ =>
            {
                var p = Project;
                if (p != null)
                    TryPersistSessionToProject(p);
                if (_wiredMainVm != null)
                    _wiredMainVm.MarkDirty("Linked library cave id updated — Ctrl+S to save");
            },
        });

        CloudPublishPanel.DataContext = _cloudPublish;
        _cloudPublish.NotifyProjectChanged(Project);
    }

    private void EnsureProceduralSketchViewModel()
    {
        if (_proceduralSketch != null || ProceduralAssistPanel == null)
            return;

        _proceduralSketch = new ProceduralSketchViewModel(new ProceduralSketchEditorHost
        {
            GetProject = () => Project,
            IsSurveyLayoutReady = () => _surveyHitLayoutReady && Project != null,
            GetSurveyLayout = () => _surveyHitLayout,
            GetDesignLayer = () => DesignLayer,
            OnDesignLayerChanged = () =>
            {
                SyncDesignLayerToInMemoryProject();
                _proceduralSketch?.NotifyProjectChanged();
            },
        });
        ProceduralAssistPanel.DataContext = _proceduralSketch;
        _proceduralSketch.NotifyProjectChanged();
    }

    public CloudPublishArtifactCapture? TryCaptureCloudPublishArtifacts() => TryCapturePublishArtifacts();

    private CloudPublishArtifactCapture? TryCapturePublishArtifacts()
    {
        var p = Project;
        if (p == null || SurveyCanvas == null || DesignLayer == null)
            return null;

        TryPersistSessionToProject(p);

        var underlays = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
        var aiMap = SketchEditorPublishCapture.TryCaptureAiMapPng(
            p,
            VisualizationMode,
            PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan, VisualizationMode),
            underlays,
            ZipPath,
            DesignLayer,
            SurveyCanvas.Width,
            SurveyCanvas.Height);

        var mask = CloudPublishService.TryCaptureStructureMask(
            p,
            DesignLayer,
            SurveyCanvas.Width,
            SurveyCanvas.Height);

        var surveyJson = CloudPublishService.SerializeProjectJsonUtf8(p);
        if (aiMap == null && mask == null && surveyJson.Length == 0)
            return null;

        return new CloudPublishArtifactCapture
        {
            AiMapPng = aiMap,
            StructureMaskPng = mask,
            SurveyJsonUtf8 = surveyJson,
        };
    }
}
