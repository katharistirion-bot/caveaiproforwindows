using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Offline X-Ray: Android-exported satellite raster + survey traverse, geo-aligned via the backdrop's
/// lat/lon bounding box. Mirrors the Android on-device X-Ray view: dark canvas, traverse on terrain. When the
/// JSON does not include a backdrop bbox, we still draw the satellite and best-fit the survey to the image so
/// the user always gets a visual.
/// </summary>
public partial class OfflineXRayView : UserControl
{
    private BitmapSource? _background;
    private XRayBackdropMetadata? _backdropMetadata;

    private IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> _coords =
        new Dictionary<string, SurveyStationGeometry.StationPlanCoords>(StringComparer.OrdinalIgnoreCase);

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
        Loaded += (_, _) => Refresh();
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OfflineXRayView v && !Equals(e.NewValue, e.OldValue))
            v.Refresh();
    }

    private void MapHostGrid_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawTraverseOverlay();

    private void Refresh()
    {
        BackgroundRaster.Source = null;
        TraverseCanvas.Children.Clear();
        PlaceholderBorder.Visibility = Visibility.Collapsed;
        PlaceholderText.Text = "";
        AlignmentFooter.Text = "";
        _background = null;
        _backdropMetadata = null;
        _coords = new Dictionary<string, SurveyStationGeometry.StationPlanCoords>(StringComparer.OrdinalIgnoreCase);

        var project = Project;
        if (project == null)
        {
            PlaceholderText.Text = "Select a cave project.";
            PlaceholderBorder.Visibility = Visibility.Visible;
            return;
        }

        _background = ResolveStaticSatelliteSnapshot(project, ZipPath, MapInventory);
        BackgroundRaster.Source = _background;

        if (_background == null)
        {
            ShowCloudFallback(project, ZipPath);
            return;
        }

        _backdropMetadata = XRayBackdropMetadataParser.TryRead(project);
        _coords = SurveyStationGeometry.CalculatePlanCoordinates(project);

        var hasLegs = project.Shots.Any(s => s.IsTraverseLeg);
        if (_coords.Count == 0 || !hasLegs)
        {
            AddNoTraverseHint();
            UpdateAlignmentFooter();
            return;
        }

        RedrawTraverseOverlay();
        UpdateAlignmentFooter();
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
            AlignmentFooter.Text =
                $"Geo-aligned · backdrop bounds {md.MinLat:0.######}…{md.MaxLat:0.######} N, {md.MinLon:0.######}…{md.MaxLon:0.######} E " +
                $"({md.SpanLatDeg * XRayProjection.MetresPerDegreeLatitude:0} × " +
                $"{md.SpanLonDeg * XRayProjection.MetresPerDegreeLatitude * Math.Cos(md.CenterLat * Math.PI / 180):0} m) · " +
                $"source: {md.SourceLabel}";
        }
        else if (p.Lat is null || p.Lon is null)
        {
            AlignmentFooter.Text =
                "Best-fit · entrance lat/lon missing in JSON. Survey is fitted to image bounds (not geographically anchored).";
        }
        else
        {
            AlignmentFooter.Text =
                "Best-fit · no satellite bounding box in JSON. Looking for keys like xrayBackdropImageBounds (NE/SW lat/lon), " +
                "xrayBackdropCenter{Lat,Lon}+Zoom+ImageWidthPx+ImageHeightPx, or geminiSatellite* equivalents — extend XRayBackdropMetadataParser when Android emits a new shape.";
        }
    }

    private void ShowCloudFallback(CaveProjectDocument project, string? zipPath)
    {
        var rasterCount = AndroidBackupImageDiscovery.CountRasterImageEntries(zipPath);
        PlaceholderText.Text = rasterCount > 0
            ? $"{OfflineBackupCloudUiMessages.NoCloudCaptureInBackup}\n\nThe backup ZIP does contain {rasterCount} image(s) — none matched the X-Ray heuristic " +
              "(satellite / aerial / basemap / x-ray). Open the BACKUP CONTENTS tab to inspect; if Android emitted a different slot/key name, expand the heuristic in AndroidBackupImageDiscovery.XRayKeywords."
            : OfflineBackupCloudUiMessages.NoCloudCaptureInBackup;
        PlaceholderBorder.Visibility = Visibility.Visible;
    }

    private void AddNoTraverseHint()
    {
        TraverseCanvas.Children.Clear();
        TraverseCanvas.Width = MapHostGrid.ActualWidth;
        TraverseCanvas.Height = MapHostGrid.ActualHeight;
        if (TraverseCanvas.Width < 8 || TraverseCanvas.Height < 8)
            return;
        var tb = new TextBlock
        {
            Text = "Satellite snapshot from backup · no traverse geometry in this payload (export survey from Android).",
            Foreground = Brushes.White,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Min(460, TraverseCanvas.Width - 24),
            Effect = new DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 6,
                Opacity = 0.95,
                Color = Colors.Black,
            },
        };
        Canvas.SetLeft(tb, 12);
        Canvas.SetTop(tb, 12);
        TraverseCanvas.Children.Add(tb);
    }

    private void RedrawTraverseOverlay()
    {
        TraverseCanvas.Children.Clear();
        var p = Project;
        if (p == null || _background == null)
            return;

        TraverseCanvas.Width = MapHostGrid.ActualWidth;
        TraverseCanvas.Height = MapHostGrid.ActualHeight;
        var w = TraverseCanvas.Width;
        var h = TraverseCanvas.Height;
        if (w < 8 || h < 8)
            return;

        var points = _coords.Values.ToList();
        if (points.Count == 0)
            return;

        var planSyms = SurveyStationGeometry.ParsePlanMapSymbols(p).ToList();

        var (worldToCanvas, layoutForSymbols) = BuildLayout(p, w, h);

        foreach (var shot in p.Shots.Where(s => s.IsTraverseLeg))
        {
            if (!_coords.TryGetValue(shot.FromStation, out var a) || !_coords.TryGetValue(shot.ToStation, out var b))
                continue;
            var pa = worldToCanvas(a.X, a.Y);
            var pb = worldToCanvas(b.X, b.Y);
            TraverseCanvas.Children.Add(new Line
            {
                X1 = pa.X,
                Y1 = pa.Y,
                X2 = pb.X,
                Y2 = pb.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(240, 255, 80, 80)),
                StrokeThickness = Math.Max(2.0, w * 0.003),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }

        foreach (var c in points)
        {
            var pt = worldToCanvas(c.X, c.Y);
            var dotSz = Math.Max(4.5, w * 0.008);
            var half = dotSz * 0.5;
            var dot = new Ellipse
            {
                Width = dotSz,
                Height = dotSz,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                StrokeThickness = 0.9,
            };
            Canvas.SetLeft(dot, pt.X - half);
            Canvas.SetTop(dot, pt.Y - half);
            TraverseCanvas.Children.Add(dot);
        }

        if (layoutForSymbols is { } symLayout)
        {
            foreach (var sym in planSyms)
            {
                var el = AndroidMapSymbolVisualFactory.CreateVisual(sym, symLayout, highContrast: false);
                Panel.SetZIndex(el, 8);
                TraverseCanvas.Children.Add(el);
            }
        }
    }

    /// <summary>
    /// Returns a survey-X/Y → canvas mapping. Geo-aligned path is preferred when (a) backdrop bbox is known and
    /// (b) entrance lat/lon are present; otherwise we fall back to fit-to-survey on the image rect.
    /// </summary>
    private (Func<float, float, Point> worldToCanvas, PlanCanvasSurveyLayout? symbolLayout) BuildLayout(
        CaveProjectDocument p,
        double hostW,
        double hostH)
    {
        var bmp = _background!;
        var imgPxW = (double)bmp.PixelWidth;
        var imgPxH = (double)bmp.PixelHeight;
        var displayRect = XRayProjection.ComputeImageDisplayRectInCanvas(imgPxW, imgPxH, hostW, hostH);

        if (_backdropMetadata is { IsValid: true } md && p.Lat is { } lat0 && p.Lon is { } lon0)
        {
            var geo = XRayProjection.Build(lat0, lon0, md, imgPxW, imgPxH, displayRect);
            // For symbols we need a PlanCanvasSurveyLayout — build one whose pixels-per-metre matches the geo
            // mapping at the entrance latitude (so symbol diameters in survey-metres still match terrain pixels).
            var pxPerLonDeg = displayRect.Width / Math.Max(1e-12, md.MaxLon - md.MinLon);
            var pxPerMetre = pxPerLonDeg * geo.LonPerMetre; // (px/deg) × (deg/m) = px/m
            var symLayout = BuildSymbolLayoutFromGeoMapping(geo, pxPerMetre);
            return ((float x, float y) => geo.WorldMetresToCanvas(x, y), symLayout);
        }

        return BuildFitToSurveyLayout(p, displayRect);
    }

    /// <summary>
    /// Build a <see cref="PlanCanvasSurveyLayout"/> whose <c>WorldToCanvas</c> matches the geo projection so the
    /// existing <see cref="AndroidMapSymbolVisualFactory"/> can place symbols on the satellite at correct scale.
    /// </summary>
    /// <remarks>
    /// <see cref="PlanCanvasSurveyLayout.WorldToCanvas"/> computes:<br/>
    /// <c>px = OriginX + (x − WMinX) × Scale</c><br/>
    /// <c>py = OriginY + (WMaxY − y) × Scale</c><br/>
    /// To make this match a geographic mapping at small areas (linear approximation), we choose <c>WMinX=0</c>,
    /// <c>WMaxY=worldSpan</c>, <c>Scale=pxPerMetre</c>, then solve for OriginX/Y so the entrance (survey 0,0) lands
    /// on the correct satellite pixel.
    /// </remarks>
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

    /// <summary>
    /// Walks <see cref="AndroidBackupImageDiscovery"/> hints in order (explicit JSON property → keyword-matched
    /// extension data → <c>map_inventory</c> rows → ZIP entry scan) and returns the first hint that resolves to
    /// a usable bitmap. Replaces the old hardcoded leaf-name search that missed Android's dynamic file names.
    /// </summary>
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
}
