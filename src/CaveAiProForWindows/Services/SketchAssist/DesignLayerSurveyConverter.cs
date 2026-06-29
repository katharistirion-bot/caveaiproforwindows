using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>
/// Maps WPF <see cref="Canvas"/> design-layer visuals (screen DIPs local to the survey canvas) into survey metres
/// using <see cref="PlanCanvasSurveyLayout"/>.
/// </summary>
public static class DesignLayerSurveyConverter
{
    /// <summary>
    /// Extracts user freehand polylines and symbol stamps from <paramref name="designLayer"/>.
    /// Skips Android-imported symbols (<see cref="AndroidImportedSymbolPresenter.ImportedChildTag"/>).
    /// </summary>
    public static (List<SketchStrokeModel> Strokes, List<SketchSymbolStampModel> SymbolStamps) ExtractUserGeometry(
        Canvas? designLayer,
        PlanCanvasSurveyLayout layout)
    {
        var strokes = new List<SketchStrokeModel>();
        var stamps = new List<SketchSymbolStampModel>();
        if (designLayer == null)
            return (strokes, stamps);

        foreach (UIElement child in designLayer.Children)
        {
            if (child is FrameworkElement fe && Equals(fe.Tag, AndroidImportedSymbolPresenter.ImportedChildTag))
                continue;

            switch (child)
            {
                case Polyline poly when poly.Points.Count >= 2:
                    if (poly.Tag is DesignLayerInkMetadata { Source: var strokeSource }
                        && string.Equals(strokeSource, SketchStrokeStyleDefaults.ProceduralSource, StringComparison.OrdinalIgnoreCase))
                        break;
                    if (TryConvertPolyline(poly, layout, out var stroke))
                        strokes.Add(stroke);
                    break;
                case Viewbox vb:
                    if (vb.Tag is DesignLayerInkMetadata { Source: var stampSource }
                        && string.Equals(stampSource, SketchStrokeStyleDefaults.ProceduralSource, StringComparison.OrdinalIgnoreCase))
                        break;
                    if (TryConvertSymbolViewbox(vb, layout, out var stamp))
                        stamps.Add(stamp);
                    break;
                case Ellipse:
                    // Transient select marker or ink selection frame — ignore.
                    break;
                case Rectangle rect when Equals(rect.Tag, DesignLayerInkHitTest.SelectionFrameTag):
                    break;
            }
        }

        return (strokes, stamps);
    }

    /// <summary>Converts one canvas DIP point to survey metres.</summary>
    public static (float Wx, float Wy) CanvasPointToSurvey(double canvasX, double canvasY, PlanCanvasSurveyLayout layout)
    {
        var (wx, wy) = layout.CanvasToWorld(canvasX, canvasY);
        return ((float)wx, (float)wy);
    }

    /// <summary>Converts survey metres to canvas DIPs (same mapping as plan vector rendering).</summary>
    public static Point SurveyPointToCanvas(float surveyX, float surveyY, PlanCanvasSurveyLayout layout) =>
        layout.WorldToCanvas(surveyX, surveyY);

    private static bool TryConvertPolyline(
        Polyline poly,
        PlanCanvasSurveyLayout layout,
        out SketchStrokeModel stroke)
    {
        var meta = poly.Tag as DesignLayerInkMetadata;
        stroke = new SketchStrokeModel
        {
            Source = meta?.Source ?? SketchStrokeStyleDefaults.DesignLayerSource,
            Metadata = meta,
        };

        foreach (var pt in poly.Points)
        {
            var (wx, wy) = CanvasPointToSurvey(pt.X, pt.Y, layout);
            if (!IsFinite(wx) || !IsFinite(wy))
                continue;
            stroke.Points.Add((wx, wy));
        }

        return stroke.IsDrawable;
    }

    private static bool TryConvertSymbolViewbox(
        Viewbox vb,
        PlanCanvasSurveyLayout layout,
        out SketchSymbolStampModel stamp)
    {
        stamp = default!;
        var left = Canvas.GetLeft(vb);
        var top = Canvas.GetTop(vb);
        if (double.IsNaN(left))
            left = 0;
        if (double.IsNaN(top))
            top = 0;
        var w = vb.Width > 0 ? vb.Width : vb.ActualWidth;
        var h = vb.Height > 0 ? vb.Height : vb.ActualHeight;
        if (w <= 0)
            w = SketchSymbolDefinitions.StampDisplaySize;
        if (h <= 0)
            h = SketchSymbolDefinitions.StampDisplaySize;
        var cx = left + w * 0.5;
        var cy = top + h * 0.5;
        var (wx, wy) = CanvasPointToSurvey(cx, cy, layout);
        if (!IsFinite(wx) || !IsFinite(wy))
            return false;

        stamp = new SketchSymbolStampModel
        {
            SurveyX = wx,
            SurveyY = wy,
            Kind = ResolveSymbolKind(vb),
        };
        return true;
    }

    private static SketchEditorSymbolKind ResolveSymbolKind(Viewbox vb)
    {
        if (vb.Child is not Path path || path.Data == null)
            return SketchEditorSymbolKind.StalactiteSpeleothem;

        foreach (SketchEditorSymbolKind kind in Enum.GetValues(typeof(SketchEditorSymbolKind)))
        {
            try
            {
                var ink = SketchSymbolDefinitions.Get(kind);
                if (ReferenceEquals(ink.Geometry, path.Data))
                    return kind;
            }
            catch
            {
                /* ignore */
            }
        }

        return SketchEditorSymbolKind.StalactiteSpeleothem;
    }

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}

/// <summary>Snap sketch ink endpoints to nearby traverse station canvas positions.</summary>
public static class SketchStationSnapHelper
{
    public const double DefaultSnapRadiusDip = 14;

    public static Point TrySnap(
        Point canvasPoint,
        PlanScene? scene,
        PlanCanvasSurveyLayout layout,
        double maxRadiusDip = DefaultSnapRadiusDip)
    {
        if (scene == null || scene.Stations.Count == 0 || maxRadiusDip <= 0)
            return canvasPoint;

        var bestDist2 = maxRadiusDip * maxRadiusDip;
        var best = canvasPoint;
        var found = false;

        foreach (var st in scene.Stations.Values)
        {
            var pt = layout.WorldToCanvas(st.X, st.Y);
            var dx = canvasPoint.X - pt.X;
            var dy = canvasPoint.Y - pt.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 > bestDist2)
                continue;
            bestDist2 = d2;
            best = pt;
            found = true;
        }

        return found ? best : canvasPoint;
    }

    /// <summary>Finds the nearest traverse station within snap radius (for LRUD status hints).</summary>
    public static bool TryFindNearestStation(
        Point canvasPoint,
        PlanScene? scene,
        PlanCanvasSurveyLayout layout,
        out string stationName,
        out Point stationCanvas,
        double maxRadiusDip = DefaultSnapRadiusDip)
    {
        stationName = "";
        stationCanvas = canvasPoint;
        if (scene == null || scene.Stations.Count == 0 || maxRadiusDip <= 0)
            return false;

        var bestDist2 = maxRadiusDip * maxRadiusDip;
        var found = false;

        foreach (var (name, st) in scene.Stations)
        {
            var pt = layout.WorldToCanvas(st.X, st.Y);
            var dx = canvasPoint.X - pt.X;
            var dy = canvasPoint.Y - pt.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 > bestDist2)
                continue;
            bestDist2 = d2;
            stationCanvas = pt;
            stationName = name;
            found = true;
        }

        return found;
    }
}

/// <summary>Zoom/pan helpers to frame survey geometry inside a scroll host viewport.</summary>
public static class SketchMapViewFitter
{
    private const double MarginFactor = 0.92;

    public static bool TryComputeFitTransform(
        PlanScene scene,
        PlanCanvasSurveyLayout layout,
        double mapWidth,
        double mapHeight,
        double viewportWidth,
        double viewportHeight,
        out double scale,
        out double panX,
        out double panY,
        out double centerX,
        out double centerY)
    {
        scale = 1;
        panX = 0;
        panY = 0;
        centerX = mapWidth * 0.5;
        centerY = mapHeight * 0.5;

        if (mapWidth <= 0 || mapHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return false;

        var tl = layout.WorldToCanvas(scene.MinX, scene.MinY);
        var br = layout.WorldToCanvas(scene.MaxX, scene.MaxY);
        var minX = Math.Min(tl.X, br.X);
        var maxX = Math.Max(tl.X, br.X);
        var minY = Math.Min(tl.Y, br.Y);
        var maxY = Math.Max(tl.Y, br.Y);

        var pad = Math.Max(24, Math.Min(mapWidth, mapHeight) * 0.04);
        minX -= pad;
        minY -= pad;
        maxX += pad;
        maxY += pad;

        var contentW = Math.Max(1, maxX - minX);
        var contentH = Math.Max(1, maxY - minY);
        var fitScale = Math.Min(viewportWidth / contentW, viewportHeight / contentH) * MarginFactor;
        scale = MapZoomInteractions.ClampScale(fitScale);

        centerX = mapWidth * 0.5;
        centerY = mapHeight * 0.5;

        var cx = (minX + maxX) * 0.5;
        var cy = (minY + maxY) * 0.5;
        panX = viewportWidth * 0.5 - cx * scale;
        panY = viewportHeight * 0.5 - cy * scale;
        return true;
    }
}

/// <summary>Clone user sketch ink on the design layer (duplicate selection).</summary>
public static class DesignLayerInkDuplicator
{
    public const double DefaultOffsetDip = 12;

    public static UIElement? TryDuplicate(UIElement source, double offsetDip = DefaultOffsetDip)
    {
        if (!DesignLayerInkHitTest.IsEditableInk(source))
            return null;

        return source switch
        {
            Polyline poly => DuplicatePolyline(poly, offsetDip),
            Viewbox vb => DuplicateSymbol(vb, offsetDip),
            _ => null,
        };
    }

    private static Polyline DuplicatePolyline(Polyline source, double offsetDip)
    {
        var meta = source.Tag as DesignLayerInkMetadata;
        var copy = new Polyline
        {
            Stroke = source.Stroke,
            StrokeThickness = source.StrokeThickness,
            StrokeLineJoin = source.StrokeLineJoin,
            StrokeStartLineCap = source.StrokeStartLineCap,
            StrokeEndLineCap = source.StrokeEndLineCap,
            StrokeDashArray = source.StrokeDashArray?.Clone(),
            Opacity = source.Opacity,
            Tag = meta != null
                ? new DesignLayerInkMetadata
                {
                    Source = meta.Source,
                    StrokeWidthPx = meta.StrokeWidthPx,
                    StrokeColorArgb = meta.StrokeColorArgb,
                    BrushProfile = meta.BrushProfile,
                    LayerName = meta.LayerName,
                    LayerIndex = meta.LayerIndex,
                    LayerZOrder = meta.LayerZOrder,
                }
                : DesignLayerInkMetadata.ForUserStroke(SketchStrokeStyleDefaults.DefaultStrokeWidthPx),
        };

        foreach (var pt in source.Points)
            copy.Points.Add(new Point(pt.X + offsetDip, pt.Y + offsetDip));

        return copy;
    }

    private static Viewbox DuplicateSymbol(Viewbox source, double offsetDip)
    {
        var left = Canvas.GetLeft(source);
        var top = Canvas.GetTop(source);
        if (double.IsNaN(left))
            left = 0;
        if (double.IsNaN(top))
            top = 0;

        var meta = source.Tag as DesignLayerInkMetadata;
        Path? pathCopy = null;
        if (source.Child is Path path)
        {
            pathCopy = new Path
            {
                Data = path.Data,
                Stroke = path.Stroke,
                Fill = path.Fill,
                StrokeThickness = path.StrokeThickness,
            };
        }

        var copy = new Viewbox
        {
            Width = source.Width > 0 ? source.Width : SketchSymbolDefinitions.StampDisplaySize,
            Height = source.Height > 0 ? source.Height : SketchSymbolDefinitions.StampDisplaySize,
            Stretch = source.Stretch,
            Opacity = source.Opacity,
            Child = pathCopy,
            Tag = meta != null
                ? new DesignLayerInkMetadata
                {
                    Source = meta.Source,
                    StrokeWidthPx = meta.StrokeWidthPx,
                    StrokeColorArgb = meta.StrokeColorArgb,
                    BrushProfile = meta.BrushProfile,
                    LayerName = meta.LayerName,
                    LayerIndex = meta.LayerIndex,
                    LayerZOrder = meta.LayerZOrder,
                }
                : DesignLayerInkMetadata.ForUserStroke(SketchStrokeStyleDefaults.DefaultStrokeWidthPx),
        };
        Canvas.SetLeft(copy, left + offsetDip);
        Canvas.SetTop(copy, top + offsetDip);
        return copy;
    }
}
