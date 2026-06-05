using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using CaveAiProForWindows.Models;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.GenerativeMap;
using CaveAiProForWindows.Services.SketchAssist;
using CaveAiProForWindows.ViewModels;

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
        v.Redraw();
        v._cloudPublish?.NotifyProjectChanged(v.Project);
        v._sketchAssist?.NotifyProjectChanged(v.Project);
    }

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(SketchEditorView),
        new PropertyMetadata(null, (d, _) => ((SketchEditorView)d).Redraw()));

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
        v.Redraw();
        v.PersistSketchTab();
    }

    private static void OnMapRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SketchEditorView)d;
        v.UnwireMapRows(e.OldValue);
        v.WireMapRows(e.NewValue);
        v.Redraw();
    }

    private static void OnMapInventoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SketchEditorView)d;
        v.UnwireMapInventory(e.OldValue);
        v.WireMapInventory(e.NewValue);
        v.Redraw();
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
    private SketchAssistViewModel? _sketchAssist;
    private CaveProjectDocument? _designLayerProjectScope;
    private PlanScene? _interactivePlanScene;
    private PlanCanvasSurveyLayout _surveyHitLayout;
    private bool _surveyHitLayoutReady;
    private bool _applyingSettings;
    private SurveyMapPickHighlight? _surveyPickHighlight;
    private bool _stationDetailsPaneDismissed;

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
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Redraw));
            _sketchAssist?.RefreshApiTokenStatus();
        }
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
        {
            _cloudPublish?.NotifyLegalTermsChanged();
            _sketchAssist?.NotifyLegalTermsChanged();
        }
    }

    private void MainViewModel_SurveyDataChanged(object? sender, EventArgs e) => Redraw();

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SurveyCanvasTheme.Changed += OnSurveyCanvasThemeChanged;
        GenerativeMapSessionCache.SessionChanged += OnGenerativeMapSessionChanged;
        WireMapRows(MapRows);
        WireMapInventory(MapInventory);
        if (DesignLayer != null && HostScroll != null && ZoomPan != null)
        {
            _editor = new MapCanvasEditorController(
                DesignLayer, HostScroll, ZoomPan, () => _currentTool, () => _stampKind);
        }

        ApplySketchTabFromSettings();
        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        else if (SymbolPaletteRock != null)
            SymbolPaletteRock.IsChecked = true;
        SyncSymbolPaletteEnabled();
        WireMainViewModel(DataContext as MainViewModel);
        EnsureCloudPublishViewModel();
        EnsureSketchAssistViewModel();
        ResetPropertiesPanelToSummary();
        Redraw();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            ApplyDeferredSketchZoomFromSettings();
            ResetPropertiesPanelToSummary();
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PersistSketchTab();
        SurveyCanvasTheme.Changed -= OnSurveyCanvasThemeChanged;
        GenerativeMapSessionCache.SessionChanged -= OnGenerativeMapSessionChanged;
        WireMainViewModel(null);
        UnwireMapRows(MapRows);
        UnwireMapInventory(MapInventory);
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
        _editor?.OnDesignLayerCleared();
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
            if (SketchCartographyOverlayCheck != null)
                SketchCartographyOverlayCheck.IsChecked = s.Overlay;
            SyncCartographicIntensityCombo(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);
            if (SketchToolPan != null && SketchToolSelect != null && SketchToolDraw != null && SketchToolSymbol != null)
            {
                SketchToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
                SketchToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
                SketchToolDraw.IsChecked = _currentTool == MapCanvasEditorTool.DrawFreehand;
                SketchToolSymbol.IsChecked = _currentTool == MapCanvasEditorTool.PlaceSymbol;
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
        all.Sketch.Overlay = SketchCartographyOverlayCheck?.IsChecked != false;
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

    private PlanCanvasDrawOptions SketchDrawOptions()
    {
        var intensity = CartographicIntensityParser.Parse(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);
        return PlanCanvasDrawOptions.ForPlan(
            SketchStationNamesCheck?.IsChecked != false,
            SketchCartographyOverlayCheck?.IsChecked != false,
            VisualizationMode,
            SketchStationZDepthCheck?.IsChecked == true,
            intensity,
            _surveyPickHighlight);
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

    private void CartographyOptions_Changed(object sender, RoutedEventArgs e)
    {
        Redraw();
        PersistSketchTab();
    }

    private void Redraw()
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
                if (_surveyHitLayoutReady)
                    AndroidImportedSymbolPresenter.SyncDesignLayer(DesignLayer, scene, _surveyHitLayout, highContrast: false);
                else
                    AndroidImportedSymbolPresenter.ClearImported(DesignLayer);

                ApplyGenerativeMapOverlay(p);
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
        foreach (ToggleButton sibling in new ToggleButton[] { SymbolPaletteRock, SymbolPaletteWater, SymbolPaletteSpele })
        {
            if (!ReferenceEquals(sibling, t))
                sibling.IsChecked = false;
        }

        PersistSketchTab();
    }

    private void SketchToolPan_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.PanZoom;
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
    }

    private void SketchToolSelect_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.Select;
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
    }

    private void SketchToolDraw_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.DrawFreehand;
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
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
        Redraw();
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
                Redraw();
                e.Handled = true;
                return;
            }

            _stationDetailsPaneDismissed = false;
            UpdatePropertiesPanel(pick);
            Redraw();
            e.Handled = true;
            return;
        }

        _editor?.OnMouseLeftButtonDown(sender, e);
    }

    private void DesignLayer_MouseMove(object sender, MouseEventArgs e) =>
        _editor?.OnMouseMove(sender, e);

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
            if (SketchToolPan != null)
                SketchToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
            if (SketchToolSelect != null)
                SketchToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
            if (SketchToolDraw != null)
                SketchToolDraw.IsChecked = _currentTool == MapCanvasEditorTool.DrawFreehand;
            if (SketchToolSymbol != null)
                SketchToolSymbol.IsChecked = _currentTool == MapCanvasEditorTool.PlaceSymbol;
        }
        finally
        {
            _applyingSettings = false;
        }

        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        SyncSymbolPaletteEnabled();
        PersistSketchTab();
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
            PersistLinkedLibraryCaveId = id =>
            {
                if (_wiredMainVm != null)
                    _wiredMainVm.StatusMessage = $"Linked library cave id set to {id.Trim()}.";
            },
        });

        CloudPublishPanel.DataContext = _cloudPublish;
        _cloudPublish.NotifyProjectChanged(Project);
    }

    private void EnsureSketchAssistViewModel()
    {
        if (_sketchAssist != null || SketchAssistPanel == null)
            return;

        _sketchAssist = new SketchAssistViewModel(new SketchAssistEditorHost
        {
            GetProject = () => Project,
            GetLegalTermsAccepted = () => _wiredMainVm?.LegalTermsAccepted == true,
            BuildSession = () => SketchAssistInputBuilder.TryBuild(
                Project!,
                DesignLayer,
                SurveyCanvas?.Width ?? 0,
                SurveyCanvas?.Height ?? 0,
                VisualizationMode),
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
            OnGenerativeRenderCompleted = () => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Redraw)),
            GetOwnerWindow = () => Window.GetWindow(this),
            OpenLegalSettingsTab = () =>
            {
                if (Window.GetWindow(this) is MainWindow mw)
                    mw.SelectLegalSettingsTab();
            },
        });

        SketchAssistPanel.DataContext = _sketchAssist;
        _sketchAssist.NotifyProjectChanged(Project);
        _sketchAssist.RefreshApiTokenStatus();
    }

    private void OnGenerativeMapSessionChanged(object? sender, GenerativeMapSessionChangedEventArgs e)
    {
        if (Project == null || !ReferenceEquals(e.Project, Project))
            return;
        Dispatcher.BeginInvoke(() =>
        {
            if (Project != null)
                ApplyGenerativeMapOverlay(Project);
        });
    }

    private void ApplyGenerativeMapOverlay(CaveProjectDocument project)
    {
        if (SurveyCanvas == null || !_surveyHitLayoutReady)
            return;

        var entry = GenerativeMapSessionCache.TryGet(project);
        var show = _sketchAssist?.ShowAiRenderOnCanvas != false;
        GenerativeMapOverlayPresenter.Apply(
            SurveyCanvas,
            _surveyHitLayout,
            entry?.Bitmap,
            show && entry != null);
    }

    private CloudPublishArtifactCapture? TryCapturePublishArtifacts()
    {
        var p = Project;
        if (p == null || SurveyCanvas == null || DesignLayer == null)
            return null;

        var underlays = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
        var generative = GenerativeMapSessionCache.TryGet(p)?.PngBytes;
        var aiMap = generative ?? SketchEditorPublishCapture.TryCaptureAiMapPng(
            p,
            VisualizationMode,
            SketchDrawOptions(),
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

        if (aiMap == null && mask == null)
            return null;

        return new CloudPublishArtifactCapture
        {
            AiMapPng = aiMap,
            StructureMaskPng = mask,
            SurveyJsonUtf8 = CloudPublishService.SerializeProjectJsonUtf8(p),
        };
    }
}
