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
using CaveAiProForWindows.Services.GenerativeMap;
using Microsoft.Win32;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Geo-calibrated X-Ray map: Android satellite raster + traverse overlay in WGS-84 space,
/// with GIS-style zoom/pan, selectable stations, and optional AI render layer.
/// </summary>
public partial class OfflineXRayView : UserControl
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
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;
    private bool _showAiOverlay = true;

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
        _showAiOverlay = AppUiSettingsStore.LoadOrDefault().GenerativeMap.ShowAiRenderOnCanvas;
        if (AiOverlayCheck != null)
            AiOverlayCheck.IsChecked = _showAiOverlay;
        SurveyStationSelectionHub.StationSelected += OnExternalStationSelected;
        GenerativeMapSessionCache.SessionChanged += OnGenerativeMapSessionChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SurveyStationSelectionHub.StationSelected -= OnExternalStationSelected;
        GenerativeMapSessionCache.SessionChanged -= OnGenerativeMapSessionChanged;
    }

    private void OnGenerativeMapSessionChanged(object? sender, GenerativeMapSessionChangedEventArgs e)
    {
        if (Project == null || !ReferenceEquals(e.Project, Project))
            return;
        Dispatcher.BeginInvoke(() => ApplyAiOverlay());
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
        AiOverlayCanvas.Children.Clear();
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

        _backdropMetadata = XRayBackdropMetadataParser.TryRead(project);
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
        ApplyAiOverlay();
        FitMapToViewport();
    }

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
        AiOverlayCanvas.Width = w;
        AiOverlayCanvas.Height = h;
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
        var (worldToCanvas, symLayout) = BuildLayout(p, mapW, mapH, displayRect);
        _worldToCanvas = worldToCanvas;
        _overlayLayout = symLayout;

        if (_backdropMetadata is { IsValid: true } md && p.Lat is { } lat0 && p.Lon is { } lon0)
            _geoLayout = XRayProjection.Build(lat0, lon0, md, mapW, mapH, displayRect);
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
        SelectionFooter.Text = $"Selected station: {_selectedStation} — highlighted on Plan and Section tabs.";
        if (notifyHub)
            SurveyStationSelectionHub.Select(_selectedStation, "XRay");
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

    private void ApplyAiOverlay()
    {
        AiOverlayCanvas.Children.Clear();
        if (!_showAiOverlay || Project == null || _overlayLayout is not PlanCanvasSurveyLayout layout)
            return;

        var entry = GenerativeMapSessionCache.TryGet(Project);
        if (entry?.PngBytes == null || entry.PngBytes.Length == 0)
            return;

        BitmapSource? bmp;
        try
        {
            using var ms = new MemoryStream(entry.PngBytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            bmp = decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
        }
        catch
        {
            return;
        }

        if (bmp == null)
            return;

        GenerativeMapOverlayPresenter.Apply(AiOverlayCanvas, layout, bmp, visible: true, opacity: 0.52);
    }

    private void AiOverlayCheck_Changed(object sender, RoutedEventArgs e)
    {
        _showAiOverlay = AiOverlayCheck?.IsChecked == true;
        ApplyAiOverlay();
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
        MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, e.Delta > 0, clamped);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyMapZoom(true);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyMapZoom(false);

    private void ApplyMapZoom(bool zoomIn)
    {
        if (ZoomScale == null || ZoomPan == null)
            return;
        var focus = new Point(ZoomScale.CenterX, ZoomScale.CenterY);
        MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, zoomIn, focus);
    }

    private void FitView_Click(object sender, RoutedEventArgs e) => FitMapToViewport();

    private void ResetView_Click(object sender, RoutedEventArgs e) => ResetMapView();

    private void ResetMapView()
    {
        if (ZoomPan == null || ZoomScale == null)
            return;
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = 1;
        ZoomScale.ScaleY = 1;
        FitMapToViewport();
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
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Ellipse)
            return;
        if (e.ClickCount == 2)
        {
            ResetMapView();
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(MapHostGrid);
        _panStartX = ZoomPan?.X ?? 0;
        _panStartY = ZoomPan?.Y ?? 0;
        MapCanvas.CaptureMouse();
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        UpdateCursorGeo(e.GetPosition(MapCanvas));

        if (!_isPanning || ZoomPan == null)
            return;

        var pos = e.GetPosition(MapHostGrid);
        ZoomPan.X = _panStartX + (pos.X - _panStart.X);
        ZoomPan.Y = _panStartY + (pos.Y - _panStart.Y);
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        MapCanvas.ReleaseMouseCapture();
    }

    private void MapCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            MapCanvas.ReleaseMouseCapture();
        }
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

    private (Func<float, float, Point> worldToCanvas, PlanCanvasSurveyLayout? symbolLayout) BuildLayout(
        CaveProjectDocument p,
        double mapW,
        double mapH,
        Rect displayRect)
    {
        if (_backdropMetadata is { IsValid: true } md && p.Lat is { } lat0 && p.Lon is { } lon0)
        {
            var geo = XRayProjection.Build(lat0, lon0, md, mapW, mapH, displayRect);
            var pxPerLonDeg = displayRect.Width / Math.Max(1e-12, md.MaxLon - md.MinLon);
            var pxPerMetre = pxPerLonDeg * geo.LonPerMetre;
            var symLayout = BuildSymbolLayoutFromGeoMapping(geo, pxPerMetre);
            return ((float x, float y) => geo.WorldMetresToCanvas(x, y), symLayout);
        }

        return BuildFitToSurveyLayout(p, displayRect);
    }

    private static PlanCanvasSurveyLayout BuildSymbolLayoutFromGeoMapping(XRayGeoLayout geo, double pxPerMetre)
    {
        const double worldSpan = 100000;
        var entrance = geo.WorldMetresToCanvas(0, 0);
        return new PlanCanvasSurveyLayout(
            WMinX: 0,
            WMaxX: worldSpan,
            WMinY: 0,
            WMaxY: worldSpan,
            OriginX: entrance.X,
            OriginY: entrance.Y - worldSpan * pxPerMetre,
            Scale: pxPerMetre);
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
