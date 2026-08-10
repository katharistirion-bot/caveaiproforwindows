using System.Collections;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.ReferenceCatalog;
using Microsoft.Win32;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Geo-calibrated X-Ray map: Android satellite raster + traverse overlay in WGS-84 space,
/// with GIS-style zoom/pan and selectable stations.
/// </summary>
public partial class OfflineXRayView : UserControl, IMapSurfaceShortcuts
{
    private BitmapSource? _background;
    private XRayBackdropMetadata? _backdropMetadata;
    private XRayGeoLayout? _geoLayout;
    private PlanCanvasSurveyLayout? _overlayLayout;
    private Func<float, float, Point>? _worldToCanvas;

    private IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> _coords =
        new Dictionary<string, SurveyStationGeometry.StationPlanCoords>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, SurveyStationGpsFix> _stationGps =
        new Dictionary<string, SurveyStationGpsFix>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Ellipse> _stationDots = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedStation;
    private bool _isPanning;
    private bool _isMiddlePanning;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;
    private bool _showReferencePins = true;
    private bool _applyingSettings;
    private bool _referencePinsHandlersWired;
    private bool _calibrationMode;
    private XRayBackdropMetadata? _workingBounds;
    private CalibrationCorner? _draggingCorner;

    private enum CalibrationCorner { Nw, Ne, Se, Sw }

    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(OfflineXRayView),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(OfflineXRayView),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty MapInventoryProperty = DependencyProperty.Register(
        nameof(MapInventory),
        typeof(IEnumerable),
        typeof(OfflineXRayView),
        new PropertyMetadata(null, OnInputChanged));

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

    public IEnumerable? MapInventory
    {
        get => (IEnumerable?)GetValue(MapInventoryProperty);
        set => SetValue(MapInventoryProperty, value);
    }

    public OfflineXRayView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        _showReferencePins = settings.ShowReferencePinsOnXRay;

        _applyingSettings = true;
        try
        {
            if (ReferencePinsCheck != null && !_referencePinsHandlersWired)
            {
                ReferencePinsCheck.IsChecked = _showReferencePins;
                ReferencePinsCheck.Checked += ReferencePinsCheck_Changed;
                ReferencePinsCheck.Unchecked += ReferencePinsCheck_Changed;
                _referencePinsHandlersWired = true;
            }
        }
        finally
        {
            _applyingSettings = false;
        }

        SurveyStationSelectionHub.StationSelected += OnExternalStationSelected;
        SurveyStationSelectionHub.SelectionCleared += OnExternalSelectionCleared;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferencePinsCheck != null && _referencePinsHandlersWired)
        {
            ReferencePinsCheck.Checked -= ReferencePinsCheck_Changed;
            ReferencePinsCheck.Unchecked -= ReferencePinsCheck_Changed;
            _referencePinsHandlersWired = false;
        }

        SurveyStationSelectionHub.StationSelected -= OnExternalStationSelected;
        SurveyStationSelectionHub.SelectionCleared -= OnExternalSelectionCleared;
    }

    private void OnExternalSelectionCleared(object? sender, SurveyStationSelectionEventArgs e)
    {
        if (string.Equals(e.Source, "XRay", StringComparison.OrdinalIgnoreCase))
            return;
        ClearStationSelection(notifyHub: false);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OfflineXRayView v && !Equals(e.NewValue, e.OldValue))
            v.Refresh();
    }

    private void OnExternalStationSelected(object? sender, SurveyStationSelectionEventArgs e)
    {
        if (string.Equals(e.Source, "XRay", StringComparison.OrdinalIgnoreCase))
            return;
        SelectStation(e.StationName, notifyHub: false);
        if (e.RequestZoom)
            ZoomToStation(e.StationName);
    }

    /// <summary>Pan/zoom so <paramref name="stationName"/> is centered on the geo map.</summary>
    public void ZoomToStation(string stationName)
    {
        if (ZoomScale == null || ZoomPan == null || HostScroll == null || string.IsNullOrWhiteSpace(stationName))
            return;

        Point? canvasPt = null;
        if (_geoLayout is { } geo && _stationGps.TryGetValue(stationName.Trim(), out var gps))
            canvasPt = geo.GeoToCanvas(gps.Lat, gps.Lon);
        else if (_worldToCanvas != null && _coords.TryGetValue(stationName.Trim(), out var c))
            canvasPt = _worldToCanvas(c.X, c.Y);

        if (canvasPt is not { } pt)
            return;

        var targetScale = MapZoomInteractions.ClampScale(Math.Max(ZoomScale.ScaleX, 2.2));
        ZoomScale.CenterX = pt.X;
        ZoomScale.CenterY = pt.Y;
        ZoomScale.ScaleX = targetScale;
        ZoomScale.ScaleY = targetScale;

        var hostW = HostScroll.ViewportWidth > 0 ? HostScroll.ViewportWidth : MapHostGrid.ActualWidth;
        var hostH = HostScroll.ViewportHeight > 0 ? HostScroll.ViewportHeight : MapHostGrid.ActualHeight;
        if (hostW <= 0 || hostH <= 0)
            return;

        ZoomPan.X = hostW * 0.5 - pt.X * targetScale;
        ZoomPan.Y = hostH * 0.5 - pt.Y * targetScale;
    }

    private void MapHostGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.PreviousSize.Width <= 0 && e.PreviousSize.Height <= 0 && _background != null)
            FitMapToViewport();
    }

    private void Refresh()
    {
        BackgroundRaster.Source = null;
        TraverseLayer.Children.Clear();
        ReferencePinsLayer.Children.Clear();
        _stationDots.Clear();
        PlaceholderBorder.Visibility = Visibility.Collapsed;
        PlaceholderText.Text = "";
        AlignmentFooter.Text = "";
        SelectionFooter.Text = "";
        _background = null;
        _backdropMetadata = null;
        _geoLayout = null;
        _overlayLayout = null;
        _worldToCanvas = null;
        _selectedStation = null;
        _coords = new Dictionary<string, SurveyStationGeometry.StationPlanCoords>(StringComparer.OrdinalIgnoreCase);

        var project = Project;
        if (project == null)
        {
            PlaceholderText.Text = "Select a cave project.";
            PlaceholderBorder.Visibility = Visibility.Visible;
            return;
        }

        _background = ResolveStaticSatelliteSnapshot(project, ZipPath, MapInventory);
        if (_background == null)
        {
            ShowCloudFallback(project, ZipPath);
            return;
        }

        _backdropMetadata = ResolveActiveBackdropMetadata(project);
        _coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        _stationGps = SurveyStationGpsCatalog.Build(project);

        ConfigureMapCanvasSize();
        PositionBackgroundImage();
        RebuildLayoutAndOverlay();

        var hasLegs = project.Shots.Any(s => s.IsTraverseLeg);
        if (_coords.Count == 0 || !hasLegs)
            AddNoTraverseHint();
        else
            RedrawTraverseOverlay();

        UpdateAlignmentFooter();
        ApplyReferencePinsOverlay();
        RebuildCalibrationOverlay();
        FitMapToViewport();
        Dispatcher.BeginInvoke(() => ApplyDeferredXRayViewFromSettings());
    }

    private XRayBackdropMetadata? ResolveActiveBackdropMetadata(CaveProjectDocument project) =>
        _calibrationMode && _workingBounds is { IsValid: true } wb
            ? wb
            : XRayBackdropMetadataParser.TryRead(project);

    private void ConfigureMapCanvasSize()
    {
        if (_background == null || MapCanvas == null || MapZoomRoot == null)
            return;

        var w = _background.PixelWidth;
        var h = _background.PixelHeight;
        MapCanvas.Width = w;
        MapCanvas.Height = h;
        MapZoomRoot.Width = w;
        MapZoomRoot.Height = h;
        ReferencePinsLayer.Width = w;
        ReferencePinsLayer.Height = h;
        CalibrationLayer.Width = w;
        CalibrationLayer.Height = h;
        TraverseLayer.Width = w;
        TraverseLayer.Height = h;

        if (ZoomScale != null)
        {
            ZoomScale.CenterX = w * 0.5;
            ZoomScale.CenterY = h * 0.5;
        }
    }

    private void PositionBackgroundImage()
    {
        if (_background == null)
            return;

        BackgroundRaster.Width = MapCanvas.Width;
        BackgroundRaster.Height = MapCanvas.Height;
        Canvas.SetLeft(BackgroundRaster, 0);
        Canvas.SetTop(BackgroundRaster, 0);
        BackgroundRaster.Source = _background;
    }

    private void RebuildLayoutAndOverlay()
    {
        var p = Project;
        if (p == null || _background == null)
            return;

        var mapW = MapCanvas.Width;
        var mapH = MapCanvas.Height;
        if (mapW <= 0 || mapH <= 0)
            return;

        var displayRect = new Rect(0, 0, mapW, mapH);
        _worldToCanvas = BuildWorldToCanvas(p, mapW, mapH, displayRect);

        if (_backdropMetadata is { IsValid: true } md && p.Lat is { } lat0 && p.Lon is { } lon0)
            _geoLayout = XRayProjection.Build(lat0, lon0, md, mapW, mapH, displayRect);

        var symbols = SurveyStationGeometry.ParsePlanMapSymbols(p).ToList();
        _overlayLayout = _worldToCanvas != null
            ? XRaySurveyOverlayLayout.BuildFromSurveyBounds(_coords, symbols, _worldToCanvas)
            : null;
    }

    private Func<float, float, Point>? BuildWorldToCanvas(
        CaveProjectDocument p,
        double mapW,
        double mapH,
        Rect displayRect)
    {
        if (_backdropMetadata is { IsValid: true } md && p.Lat is { } lat0 && p.Lon is { } lon0)
        {
            var geo = XRayProjection.Build(lat0, lon0, md, mapW, mapH, displayRect);
            return (x, y) => geo.WorldMetresToCanvas(x, y);
        }

        var (worldToCanvas, _) = BuildFitToSurveyLayout(p, displayRect);
        return worldToCanvas;
    }

    private void RedrawTraverseOverlay()
    {
        TraverseLayer.Children.Clear();
        _stationDots.Clear();

        var p = Project;
        if (p == null || _worldToCanvas == null)
            return;

        var w = MapCanvas.Width;
        if (w <= 0)
            return;

        var points = _coords.Values.ToList();
        if (points.Count == 0)
            return;

        var planSyms = SurveyStationGeometry.ParsePlanMapSymbols(p).ToList();
        var lineThickness = Math.Max(2.0, w * 0.0025);

        foreach (var shot in p.Shots.Where(s => s.IsTraverseLeg))
        {
            if (!_coords.TryGetValue(shot.FromStation, out var a) || !_coords.TryGetValue(shot.ToStation, out var b))
                continue;
            var pa = _worldToCanvas(a.X, a.Y);
            var pb = _worldToCanvas(b.X, b.Y);
            TraverseLayer.Children.Add(new Line
            {
                X1 = pa.X,
                Y1 = pa.Y,
                X2 = pb.X,
                Y2 = pb.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 80, 80)),
                StrokeThickness = lineThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            });
        }

        foreach (var kv in _coords.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            Point pt;
            var hasGps = false;
            SurveyStationGpsFix gpsFix = default;
            if (_geoLayout is { } geoLayout && _stationGps.TryGetValue(kv.Key, out gpsFix))
            {
                hasGps = true;
                pt = geoLayout.GeoToCanvas(gpsFix.Lat, gpsFix.Lon);
            }
            else
            {
                pt = _worldToCanvas!(kv.Value.X, kv.Value.Y);
            }
            var dotSz = Math.Max(10.0, w * 0.012);
            var half = dotSz * 0.5;
            var dot = new Ellipse
            {
                Width = dotSz,
                Height = dotSz,
                Fill = hasGps
                    ? new SolidColorBrush(Color.FromRgb(120, 210, 255))
                    : Brushes.White,
                Stroke = new SolidColorBrush(hasGps ? Color.FromRgb(0, 120, 200) : Color.FromRgb(40, 40, 40)),
                StrokeThickness = hasGps ? 1.6 : 1.2,
                Cursor = Cursors.Hand,
                Tag = kv.Key,
                ToolTip = hasGps
                    ? $"{kv.Key} · GPS {gpsFix.Lat:0.######}°N, {gpsFix.Lon:0.######}°E"
                    : kv.Key,
            };
            dot.MouseLeftButtonDown += StationDot_MouseLeftButtonDown;
            Canvas.SetLeft(dot, pt.X - half);
            Canvas.SetTop(dot, pt.Y - half);
            Panel.SetZIndex(dot, 20);
            TraverseLayer.Children.Add(dot);
            _stationDots[kv.Key] = dot;
        }

        if (_overlayLayout is { } symLayout)
        {
            foreach (var sym in planSyms)
            {
                var el = AndroidMapSymbolVisualFactory.CreateVisual(sym, symLayout, highContrast: false);
                el.IsHitTestVisible = false;
                Panel.SetZIndex(el, 8);
                TraverseLayer.Children.Add(el);
            }
        }

        if (!string.IsNullOrEmpty(_selectedStation))
            UpdateStationHighlightVisuals();
    }

    private void StationDot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string station)
        {
            SelectStation(station, notifyHub: true);
            e.Handled = true;
        }
    }

    private void SelectStation(string stationName, bool notifyHub)
    {
        _selectedStation = stationName.Trim();
        UpdateStationHighlightVisuals();
        UpdateSelectionFooter();
        if (notifyHub)
            SurveyStationSelectionHub.Select(_selectedStation, "XRay");
    }

    private void ClearStationSelection(bool notifyHub)
    {
        if (string.IsNullOrEmpty(_selectedStation))
            return;

        _selectedStation = null;
        UpdateStationHighlightVisuals();
        SelectionFooter.Text = "";
        if (notifyHub)
            SurveyStationSelectionHub.ClearSelection("XRay");
    }

    private void UpdateSelectionFooter()
    {
        if (SelectionFooter == null || string.IsNullOrEmpty(_selectedStation))
        {
            if (SelectionFooter != null)
                SelectionFooter.Text = "";
            return;
        }

        var inv = CultureInfo.InvariantCulture;
        var p = Project;
        if (p == null)
            return;

        var sb = new System.Text.StringBuilder();
        sb.Append($"Selected station: {_selectedStation}");

        if (_coords.TryGetValue(_selectedStation, out var c))
        {
            sb.Append($" · plan X {c.X.ToString("0.##", inv)} m, Y {c.Y.ToString("0.##", inv)} m, Z {c.Z.ToString("0.##", inv)} m");
        }

        if (_geoLayout is { } geo && _stationGps.TryGetValue(_selectedStation, out var gps))
        {
            sb.Append($" · WGS-84 {gps.Lat.ToString("0.######", inv)}°N, {gps.Lon.ToString("0.######", inv)}°E");
        }

        var shot = SurveyStationInspector.TryGetRepresentativeShotForStation(p, _selectedStation);
        if (shot != null)
        {
            sb.Append(
                $" · tape {shot.Distance.ToString("0.##", inv)} m, az {shot.Azimuth.ToString("0.#", inv)}°, clino {shot.Clino.ToString("0.#", inv)}°");
        }

        sb.Append(" — synced to Plan and Section.");
        SelectionFooter.Text = sb.ToString();
    }

    private void UpdateStationHighlightVisuals()
    {
        foreach (var (name, dot) in _stationDots)
        {
            var selected = !string.IsNullOrEmpty(_selectedStation) &&
                           string.Equals(name, _selectedStation, StringComparison.OrdinalIgnoreCase);
            var hasGpsDot = _geoLayout != null && _stationGps.ContainsKey(name);
            dot.Fill = selected
                ? new SolidColorBrush(Color.FromRgb(255, 210, 64))
                : hasGpsDot
                    ? new SolidColorBrush(Color.FromRgb(120, 210, 255))
                    : Brushes.White;
            dot.Stroke = selected
                ? new SolidColorBrush(Color.FromRgb(255, 140, 0))
                : new SolidColorBrush(hasGpsDot ? Color.FromRgb(0, 120, 200) : Color.FromRgb(40, 40, 40));
            dot.StrokeThickness = selected ? 2.5 : 1.2;
            Panel.SetZIndex(dot, selected ? 30 : 20);
        }
    }

    private void ApplyReferencePinsOverlay()
    {
        ReferencePinsLayer.Children.Clear();
        if (!_showReferencePins || Project == null || _geoLayout is not { } geoLayout)
            return;

        var bounds = ReferenceCatalogNearbyPins.TryComputeSurveyBounds(Project);
        if (bounds == null)
            return;

        var index = ReferenceCatalogFetchService.TryLoadCachedIndexEntries();
        if (index.Count == 0)
            return;

        var (minLat, maxLat, minLon, maxLon) = bounds.Value;
        var pins = ReferenceCatalogNearbyPins.FindInBounds(index, minLat, maxLat, minLon, maxLon);
        if (pins.Count == 0)
            return;

        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.XRayReferencePinsShown);

        foreach (var pin in pins)
        {
            var pt = geoLayout.GeoToCanvas(pin.Lat, pin.Lon);
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = ReferenceCatalogMapDepthColors.ReferenceFillBrush(pin.DepthM),
                Stroke = Brushes.White,
                StrokeThickness = 0.75,
                Opacity = 0.85,
                ToolTip = pin.Name + (pin.Rich ? " (Surveyed)" : " (Sparse)"),
            };
            Canvas.SetLeft(dot, pt.X - 4);
            Canvas.SetTop(dot, pt.Y - 4);
            ReferencePinsLayer.Children.Add(dot);
        }
    }

    private void ReferencePinsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
            return;

        _showReferencePins = ReferencePinsCheck?.IsChecked == true;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.ShowReferencePinsOnXRay = _showReferencePins;
        AppUiSettingsStore.Save(all);
        ApplyReferencePinsOverlay();
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
        if (MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, e.Delta > 0, clamped))
            PersistXRayView();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyMapZoom(true);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyMapZoom(false);

    private void ApplyMapZoom(bool zoomIn)
    {
        if (ZoomScale == null || ZoomPan == null)
            return;
        var focus = new Point(ZoomScale.CenterX, ZoomScale.CenterY);
        if (MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, zoomIn, focus))
            PersistXRayView();
    }

    private void FitView_Click(object sender, RoutedEventArgs e) => FitMapToViewport();

    /// <inheritdoc />
    public void MapZoomIn() => ApplyMapZoom(zoomIn: true);

    /// <inheritdoc />
    public void MapZoomOut() => ApplyMapZoom(zoomIn: false);

    /// <inheritdoc />
    public void ClearMapSelectionAndRedraw() => ClearStationSelection(notifyHub: true);

    /// <inheritdoc />
    public void ApplyMapEditorTool(MapCanvasEditorTool tool)
    {
        if (tool == MapCanvasEditorTool.PanZoom)
            return;
    }

    /// <inheritdoc />
    public void ResetMapView() => ResetMapViewInternal();

    /// <inheritdoc />
    public void UndoSketchEdit() { }

    /// <inheritdoc />
    public void RedoSketchEdit() { }

    /// <inheritdoc />
    public bool TryDeleteSelectedInk() => false;

    public bool TryDuplicateSelectedInk() => false;

    public void FitMapToSurveyBounds() => FitMapToViewport();

    private void ResetView_Click(object sender, RoutedEventArgs e) => ResetMapViewInternal();

    private void ResetMapViewInternal()
    {
        if (ZoomPan == null || ZoomScale == null)
            return;
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = 1;
        ZoomScale.ScaleY = 1;
        FitMapToViewport();
        PersistXRayView();
    }

    private void FitMapToViewport()
    {
        if (HostScroll == null || MapZoomRoot == null || ZoomScale == null || ZoomPan == null)
            return;

        var mapW = MapZoomRoot.Width;
        var mapH = MapZoomRoot.Height;
        if (mapW <= 0 || mapH <= 0)
            return;

        var hostW = HostScroll.ViewportWidth > 0 ? HostScroll.ViewportWidth : MapHostGrid.ActualWidth;
        var hostH = HostScroll.ViewportHeight > 0 ? HostScroll.ViewportHeight : MapHostGrid.ActualHeight;
        if (hostW <= 0 || hostH <= 0)
            return;

        var scale = Math.Min(hostW / mapW, hostH / mapH) * 0.92;
        scale = MapZoomInteractions.ClampScale(scale);
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = scale;
        ZoomScale.ScaleY = scale;
        ZoomScale.CenterX = mapW * 0.5;
        ZoomScale.CenterY = mapH * 0.5;

        var scaledW = mapW * scale;
        var scaledH = mapH * scale;
        ZoomPan.X = (hostW - scaledW) * 0.5;
        ZoomPan.Y = (hostH - scaledH) * 0.5;
        PersistXRayView();
    }

    private void PersistXRayView()
    {
        if (_applyingSettings || ZoomScale == null || ZoomPan == null)
            return;

        var all = AppUiSettingsStore.LoadOrDefault();
        all.XRay.ZoomScale = ZoomScale.ScaleX;
        all.XRay.PanX = ZoomPan.X;
        all.XRay.PanY = ZoomPan.Y;
        AppUiSettingsStore.Save(all);
    }

    private void ApplyDeferredXRayViewFromSettings()
    {
        var s = AppUiSettingsStore.LoadOrDefault().XRay;
        if (ZoomScale == null || ZoomPan == null)
            return;

        _applyingSettings = true;
        try
        {
            var zx = Math.Clamp(s.ZoomScale > 0 ? s.ZoomScale : 1, MapZoomInteractions.MinScale, MapZoomInteractions.MaxScale);
            ZoomScale.ScaleX = zx;
            ZoomScale.ScaleY = zx;
            ZoomPan.X = s.PanX;
            ZoomPan.Y = s.PanY;
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void MapCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || e.OriginalSource is Ellipse)
            return;
        BeginPan(e.GetPosition(MapHostGrid), captureMiddle: true);
        e.Handled = true;
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_calibrationMode && e.OriginalSource is Ellipse { Tag: CalibrationCorner corner })
        {
            _draggingCorner = corner;
            MapCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is Ellipse)
            return;
        if (e.ClickCount == 2)
        {
            ResetMapViewInternal();
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is Canvas or Image)
            ClearStationSelection(notifyHub: true);

        if (_calibrationMode)
            return;

        BeginPan(e.GetPosition(MapHostGrid), captureLeft: true);
    }

    private void BeginPan(Point hostPt, bool captureLeft = false, bool captureMiddle = false)
    {
        _isPanning = captureLeft;
        _isMiddlePanning = captureMiddle;
        _panStart = hostPt;
        _panStartX = ZoomPan?.X ?? 0;
        _panStartY = ZoomPan?.Y ?? 0;
        if (captureLeft)
            MapCanvas.CaptureMouse();
        else if (captureMiddle)
            MapCanvas.CaptureMouse();
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        UpdateCursorGeo(e.GetPosition(MapCanvas));

        if (_draggingCorner is { } corner && _workingBounds is { IsValid: true } bounds)
        {
            var pt = e.GetPosition(MapCanvas);
            if (TryCanvasPointToGeo(pt, out var lat, out var lon))
            {
                _workingBounds = corner switch
                {
                    CalibrationCorner.Nw => bounds with { MaxLat = lat, MinLon = lon },
                    CalibrationCorner.Ne => bounds with { MaxLat = lat, MaxLon = lon },
                    CalibrationCorner.Se => bounds with { MinLat = lat, MaxLon = lon },
                    CalibrationCorner.Sw => bounds with { MinLat = lat, MinLon = lon },
                    _ => bounds,
                };
                if (_workingBounds.IsValid)
                {
                    _backdropMetadata = _workingBounds;
                    RebuildLayoutAndOverlay();
                    RedrawTraverseOverlay();
                    RebuildCalibrationOverlay();
                    UpdateAlignmentFooter();
                }
            }

            e.Handled = true;
            return;
        }

        if (!_isPanning && !_isMiddlePanning || ZoomPan == null)
            return;

        var pos = e.GetPosition(MapHostGrid);
        ZoomPan.X = _panStartX + (pos.X - _panStart.X);
        ZoomPan.Y = _panStartY + (pos.Y - _panStart.Y);
    }

    private void MapCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_isMiddlePanning)
            return;
        EndPan();
        e.Handled = true;
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingCorner != null)
        {
            CommitCalibrationBounds();
            _draggingCorner = null;
            MapCanvas.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        EndPan();
    }

    private void EndPan()
    {
        if (_isPanning || _isMiddlePanning)
            PersistXRayView();
        _isPanning = false;
        _isMiddlePanning = false;
        MapCanvas.ReleaseMouseCapture();
    }

    private void MapCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPanning || _isMiddlePanning)
            EndPan();
    }

    private void UpdateCursorGeo(Point canvasPt)
    {
        if (CursorGeoText == null)
            return;

        if (_geoLayout is { } geo && geo.TryCanvasToGeo(canvasPt) is { } ll)
        {
            CursorGeoText.Text =
                $"Cursor: {ll.Lat.ToString("0.######", CultureInfo.InvariantCulture)}°N, " +
                $"{ll.Lon.ToString("0.######", CultureInfo.InvariantCulture)}°E";
            return;
        }

        CursorGeoText.Text = _worldToCanvas != null
            ? $"Map px: {canvasPt.X:0}, {canvasPt.Y:0}"
            : "—";
    }

    private void UpdateAlignmentFooter()
    {
        var p = Project;
        if (p == null || _background == null)
        {
            AlignmentFooter.Text = "";
            return;
        }

        if (_backdropMetadata is { IsValid: true } md && p.Lat is { } && p.Lon is { })
        {
            var gpsNote = _stationGps.Count > 0 ? $" · {_stationGps.Count} station GPS fix(es)" : "";
            AlignmentFooter.Text =
                $"Geo-calibrated · {md.MinLat:0.######}…{md.MaxLat:0.######}°N, {md.MinLon:0.######}…{md.MaxLon:0.######}°E · " +
                $"{md.SpanLatDeg * XRayProjection.MetresPerDegreeLatitude:0} × " +
                $"{md.SpanLonDeg * XRayProjection.MetresPerDegreeLatitude * Math.Cos(md.CenterLat * Math.PI / 180):0} m · " +
                $"source: {md.SourceLabel}{gpsNote} · 1 canvas px = 1 image px";
        }
        else if (p.Lat is null || p.Lon is null)
        {
            AlignmentFooter.Text =
                "Best-fit (no entrance lat/lon) — survey fitted to image. Add lat/lon + xrayBackdropImageBounds for WGS-84 anchoring.";
        }
        else
        {
            AlignmentFooter.Text =
                "Best-fit — no bounding box in JSON. Export xrayBackdropImageBounds from Android for true geo calibration.";
        }
    }

    private void ShowCloudFallback(CaveProjectDocument project, string? zipPath)
    {
        var rasterCount = AndroidBackupImageDiscovery.CountRasterImageEntries(zipPath);
        PlaceholderText.Text = rasterCount > 0
            ? $"{OfflineBackupCloudUiMessages.NoCloudCaptureInBackup}\n\nThe backup contains {rasterCount} image(s) — none matched the X-Ray heuristic."
            : OfflineBackupCloudUiMessages.NoCloudCaptureInBackup;
        PlaceholderBorder.Visibility = Visibility.Visible;
    }

    private void AddNoTraverseHint()
    {
        TraverseLayer.Children.Clear();
        _stationDots.Clear();
        var tb = new TextBlock
        {
            Text = "Satellite loaded · no traverse geometry (export survey from Android).",
            Foreground = Brushes.White,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Min(460, MapCanvas.Width - 24),
            Effect = new DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 6,
                Opacity = 0.95,
                Color = Colors.Black,
            },
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(tb, 12);
        Canvas.SetTop(tb, 12);
        TraverseLayer.Children.Add(tb);
    }

    private (Func<float, float, Point> worldToCanvas, PlanCanvasSurveyLayout? symbolLayout) BuildFitToSurveyLayout(
        CaveProjectDocument p,
        Rect imageRectInCanvas)
    {
        var pts = _coords.Values.ToList();
        var symbols = SurveyStationGeometry.ParsePlanMapSymbols(p).ToList();
        var minX = pts.Count > 0 ? pts.Min(s => s.X) : 0f;
        var maxX = pts.Count > 0 ? pts.Max(s => s.X) : 1f;
        var minY = pts.Count > 0 ? pts.Min(s => s.Y) : 0f;
        var maxY = pts.Count > 0 ? pts.Max(s => s.Y) : 1f;
        foreach (var sym in symbols)
        {
            minX = Math.Min(minX, sym.X);
            maxX = Math.Max(maxX, sym.X);
            minY = Math.Min(minY, sym.Y);
            maxY = Math.Max(maxY, sym.Y);
        }

        if (maxX - minX < 1e-6f)
        {
            minX -= 0.5f;
            maxX += 0.5f;
        }

        if (maxY - minY < 1e-6f)
        {
            minY -= 0.5f;
            maxY += 0.5f;
        }

        const double pad = 24;
        var fitInside = new Rect(
            imageRectInCanvas.X + pad,
            imageRectInCanvas.Y + pad,
            Math.Max(1, imageRectInCanvas.Width - 2 * pad),
            Math.Max(1, imageRectInCanvas.Height - 2 * pad));
        var layout = PlanCanvasSurveyLayout.FromAxisAlignedBounds(
            minX, maxX, minY, maxY, fitInside.Width, fitInside.Height, pad: 0);
        var shifted = layout with
        {
            OriginX = layout.OriginX + fitInside.X,
            OriginY = layout.OriginY + fitInside.Y,
        };
        return ((float x, float y) => shifted.WorldToCanvas(x, y), shifted);
    }

    private static BitmapSource? ResolveStaticSatelliteSnapshot(
        CaveProjectDocument project,
        string? zipPath,
        IEnumerable? mapInventory)
    {
        foreach (var hint in AndroidBackupImageDiscovery.Enumerate(
                     AndroidBackupImageCategory.XRayBackdrop, project, zipPath, mapInventory))
        {
            if (hint.EmbeddedJson is { } subtree)
            {
                if (OfflineEmbeddedImageDecoder.TryFindEmbeddedBitmapInJson(subtree, _ => true) is { } embedded)
                    return embedded;
                continue;
            }

            var local = AndroidBackupImageDiscovery.TryResolveLocalFile(hint, project, zipPath);
            if (string.IsNullOrWhiteSpace(local))
                continue;
            if (RasterImageDecoder.TryLoadBitmap(local!) is { } bmp)
                return bmp;
        }

        return null;
    }

    private void CalibrateBounds_Click(object sender, RoutedEventArgs e)
    {
        var project = Project;
        if (project == null)
        {
            MessageBox.Show(Window.GetWindow(this), "Select a project first.", "X-Ray calibration",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (XRayManualCalibrationWindow.TryShowDialog(Window.GetWindow(this), project))
        {
            _calibrationMode = false;
            _workingBounds = null;
            if (DragCornersButton != null)
                DragCornersButton.Content = "Drag corners";
            Refresh();
            PromptSaveProjectAfterCalibration();
        }
    }

    private void ToggleCornerCalibration_Click(object sender, RoutedEventArgs e)
    {
        var project = Project;
        if (project == null || _background == null)
        {
            MessageBox.Show(Window.GetWindow(this), "Load a satellite backdrop first.", "X-Ray calibration",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _calibrationMode = !_calibrationMode;
        if (DragCornersButton != null)
            DragCornersButton.Content = _calibrationMode ? "Done dragging" : "Drag corners";

        if (_calibrationMode)
        {
            _workingBounds = SeedCalibrationBounds(project);
            _backdropMetadata = _workingBounds;
            RebuildLayoutAndOverlay();
            RedrawTraverseOverlay();
            UpdateAlignmentFooter();
            RebuildCalibrationOverlay();
        }
        else
        {
            CommitCalibrationBounds();
            CalibrationLayer.Children.Clear();
        }
    }

    private XRayBackdropMetadata SeedCalibrationBounds(CaveProjectDocument project)
    {
        if (_workingBounds is { IsValid: true } existing)
            return existing;
        if (XRayManualBoundsStore.TryRead(project, out var manual) && manual.IsValid)
            return manual;
        if (XRayBackdropMetadataParser.TryRead(project) is { IsValid: true } parsed)
            return parsed;
        if (project.Lat is double la && project.Lon is double lo)
        {
            const double span = 0.002;
            return new XRayBackdropMetadata(la - span, la + span, lo - span, lo + span, XRayManualBoundsStore.ExtensionKey);
        }

        return new XRayBackdropMetadata(0, 1, 0, 1, XRayManualBoundsStore.ExtensionKey);
    }

    private void RebuildCalibrationOverlay()
    {
        CalibrationLayer.Children.Clear();
        if (!_calibrationMode || _workingBounds is not { IsValid: true } bounds || MapCanvas.Width <= 0)
            return;

        var w = MapCanvas.Width;
        var h = MapCanvas.Height;
        var corners = new (CalibrationCorner Id, double X, double Y, string Label)[]
        {
            (CalibrationCorner.Nw, 0, 0, "NW"),
            (CalibrationCorner.Ne, w, 0, "NE"),
            (CalibrationCorner.Se, w, h, "SE"),
            (CalibrationCorner.Sw, 0, h, "SW"),
        };

        foreach (var (id, x, y, label) in corners)
        {
            var handle = CreateCalibrationHandle(id, label);
            Canvas.SetLeft(handle, x - handle.Width * 0.5);
            Canvas.SetTop(handle, y - handle.Height * 0.5);
            Panel.SetZIndex(handle, 40);
            CalibrationLayer.Children.Add(handle);
        }

        var outline = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = h,
            Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 200, 64)),
            StrokeThickness = 2,
            StrokeDashArray = [6, 4],
            IsHitTestVisible = false,
        };
        Panel.SetZIndex(outline, 35);
        CalibrationLayer.Children.Add(outline);
    }

    private static Ellipse CreateCalibrationHandle(CalibrationCorner corner, string label)
    {
        var dot = new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = new SolidColorBrush(Color.FromRgb(255, 210, 64)),
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Cursor = Cursors.SizeAll,
            Tag = corner,
            ToolTip = $"Drag {label} corner — sets WGS-84 bounds",
        };
        return dot;
    }

    private bool TryCanvasPointToGeo(Point canvasPt, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        if (_workingBounds is not { IsValid: true } bounds)
            return false;

        var w = MapCanvas.Width;
        var h = MapCanvas.Height;
        if (w <= 0 || h <= 0)
            return false;

        var tX = Math.Clamp(canvasPt.X / w, 0, 1);
        var tY = Math.Clamp(canvasPt.Y / h, 0, 1);
        lon = bounds.MinLon + tX * bounds.SpanLonDeg;
        lat = bounds.MaxLat - tY * bounds.SpanLatDeg;
        return true;
    }

    private void CommitCalibrationBounds()
    {
        var project = Project;
        if (project == null || _workingBounds is not { IsValid: true } bounds)
            return;

        XRayManualBoundsStore.Save(project, bounds);
        _backdropMetadata = bounds;
        UpdateAlignmentFooter();
        PromptSaveProjectAfterCalibration();
    }

    private void PromptSaveProjectAfterCalibration()
    {
        var owner = Window.GetWindow(this);
        if (owner?.DataContext is not ViewModels.MainViewModel vm)
            return;

        vm.MarkDirty("X-Ray calibration updated — Ctrl+S to save");
        if (MessageBox.Show(
                owner,
                "Geo bounds updated in project memory. Save project now to write extensionData to disk?",
                "X-Ray calibration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        vm.SaveProjectCommand.Execute(null);
    }

    private void ExportGeoMap_Click(object sender, RoutedEventArgs e)
    {
        if (_background == null || _backdropMetadata is not { IsValid: true } md)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Geo export needs a satellite backdrop and xrayBackdropImageBounds in the project JSON.",
                "Export geo PNG",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export georeferenced X-Ray map",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{(Project?.Name ?? "xray").Trim()}_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            AddExtension = true,
            DefaultExt = ".png",
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var basePath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(dlg.FileName) ?? "",
                System.IO.Path.GetFileNameWithoutExtension(dlg.FileName));
            var pngBytes = EncodeBitmapToPng(_background);
            XRayGeoreferencedExporter.ExportPngWithWorldFile(
                basePath,
                pngBytes,
                md,
                _background.PixelWidth,
                _background.PixelHeight);
            MessageBox.Show(
                Window.GetWindow(this),
                $"Saved {basePath}.png with {basePath}.pgw and {basePath}.prj for QGIS.",
                "Export geo PNG",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                ex.Message,
                "Export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static byte[] EncodeBitmapToPng(BitmapSource source)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
