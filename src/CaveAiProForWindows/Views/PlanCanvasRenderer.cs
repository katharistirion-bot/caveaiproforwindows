using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>Renders a <see cref="PlanScene"/> onto a WPF <see cref="Canvas"/> (plan or section).</summary>
public static class PlanCanvasRenderer
{
    private const int ZIndexRasterUnderlay = -1;
    /// <summary>Survey paths/lines/stations — must sit above raster underlay.</summary>
    private const int ZIndexSurveyVectors = 100;
    /// <summary>Scale bar, compass — above vectors so UI stays readable.</summary>
    private const int ZIndexCartographyChrome = 105;
    /// <summary>Footer / hints — topmost chrome.</summary>
    private const int ZIndexSurveyAnnotation = 110;

    private static void AddChildZ(Canvas canvas, UIElement element, int zIndex)
    {
        Panel.SetZIndex(element, zIndex);
        canvas.Children.Add(element);
    }

    /// <summary>Prefer anti-aliased, sub-pixel strokes for organic survey geometry.</summary>
    private static void ApplySurveyRenderQuality(UIElement el)
    {
        RenderOptions.SetEdgeMode(el, EdgeMode.Unspecified);
        RenderOptions.SetBitmapScalingMode(el, BitmapScalingMode.HighQuality);
        el.SnapsToDevicePixels = false;
    }

    private static bool IsFiniteDouble(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    private static bool IsFiniteFloat(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    private static double ClampFinite(double v, double fallback)
    {
        if (!IsFiniteDouble(v))
            return fallback;
        return v;
    }

    private sealed record SurveyVectorStyle(
        Brush SketchFill,
        Brush VectorFill,
        Brush StationFill,
        Brush SymbolFill,
        Brush Legend,
        Brush TraverseStroke,
        Brush SplayStroke,
        Brush WallStrokePrimary,
        Brush WallStrokeAlt,
        bool AlternateWallStrokes,
        double SplayOpacity);

    private static SurveyVectorStyle ResolveSurveyVectorStyle(PlanCanvasDrawOptions opt, bool highContrast)
    {
        var v = opt.VisualizationMode;

        if (highContrast)
        {
            var stroke = Brushes.White;
            return new SurveyVectorStyle(
                SketchFill: new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                VectorFill: new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                StationFill: Brushes.White,
                SymbolFill: Brushes.White,
                Legend: Brushes.White,
                TraverseStroke: stroke,
                SplayStroke: new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                WallStrokePrimary: stroke,
                WallStrokeAlt: stroke,
                AlternateWallStrokes: false,
                SplayOpacity: v.ShowSplayXRayGeometry() ? 0.95 : 0.55);
        }

        if (v.UsesDarkSurveyCanvas())
        {
            var traverse = v == SurveyVisualizationMode.SectionNight
                ? new SolidColorBrush(Color.FromRgb(0x7E, 0xFF, 0x2A))
                : new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0xE0));
            var splay = new SolidColorBrush(Color.FromArgb(230, 0xD8, 0xDC, 0xE8));
            var wallA = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x6B));
            var wallB = new SolidColorBrush(Color.FromRgb(0x6B, 0xC4, 0xFF));
            var twoTone = v == SurveyVisualizationMode.Plan2Tone;
            return new SurveyVectorStyle(
                SketchFill: new SolidColorBrush(Color.FromArgb(0x5A, 0x2A, 0x2A, 0x32)),
                VectorFill: new SolidColorBrush(Color.FromArgb(0x42, 0x50, 0x3A, 0x78)),
                StationFill: new SolidColorBrush(Color.FromRgb(0xFA, 0xE6, 0x2E)),
                SymbolFill: new SolidColorBrush(Color.FromRgb(0xF4, 0x72, 0xFF)),
                Legend: new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xD4)),
                TraverseStroke: traverse,
                SplayStroke: splay,
                WallStrokePrimary: wallA,
                WallStrokeAlt: wallB,
                AlternateWallStrokes: twoTone,
                SplayOpacity: v.ShowSplayXRayGeometry() ? 0.92 : 0.68);
        }

        Brush sketchFill;
        Brush vectorFill;
        Brush stationFill;
        Brush symbolFill;
        Brush legend;
        if (SurveyCanvasTheme.IsDark)
        {
            sketchFill = new SolidColorBrush(Color.FromArgb(0x48, 0xFB, 0x92, 0x3C));
            vectorFill = new SolidColorBrush(Color.FromArgb(0x38, 0x38, 0xBD, 0xF8));
            stationFill = new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15));
            symbolFill = new SolidColorBrush(Color.FromRgb(0xE8, 0x79, 0xF9));
            legend = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        }
        else
        {
            sketchFill = new SolidColorBrush(Color.FromArgb(0x55, 0xEA, 0x58, 0x0C));
            vectorFill = new SolidColorBrush(Color.FromArgb(0x40, 0x18, 0x77, 0xF2));
            stationFill = new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15));
            symbolFill = new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xD7));
            legend = new SolidColorBrush(Color.FromRgb(0x65, 0x67, 0x6B));
        }

        var traverseStd = Brushes.Cyan;
        var splayStd = new SolidColorBrush(Color.FromArgb(200, 0x90, 0x90, 0x98));
        return new SurveyVectorStyle(
            SketchFill: sketchFill,
            VectorFill: vectorFill,
            StationFill: stationFill,
            SymbolFill: symbolFill,
            Legend: legend,
            TraverseStroke: traverseStd,
            SplayStroke: splayStd,
            WallStrokePrimary: traverseStd,
            WallStrokeAlt: traverseStd,
            AlternateWallStrokes: false,
            SplayOpacity: v.ShowSplayXRayGeometry() ? 0.88 : 0.52);
    }

    /// <summary>
    /// Union of all survey geometry (stations, walls, vector overlays, traverse legs, symbols) plus a small margin.
    /// Used so mapping matches visible content even when scene metadata bounds omit some segments.
    /// Never throws; falls back to scene bounds or a 1×1 m box if inputs are degenerate.
    /// </summary>
    private static (double minX, double maxX, double minY, double maxY) ComputeRenderingWorldBounds(PlanScene scene)
    {
        var has = false;
        double minX = 0, maxX = 0, minY = 0, maxY = 0;
        void Consider(double x, double y)
        {
            if (!IsFiniteDouble(x) || !IsFiniteDouble(y))
                return;
            if (!has)
            {
                minX = maxX = x;
                minY = maxY = y;
                has = true;
            }
            else
            {
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        try
        {
            foreach (var c in scene.Stations.Values)
            {
                if (IsFiniteFloat(c.X) && IsFiniteFloat(c.Y))
                    Consider(c.X, c.Y);
            }

            foreach (var pl in scene.WallPolylines)
            {
                foreach (var pt in pl.Points)
                {
                    if (IsFiniteFloat(pt.x) && IsFiniteFloat(pt.y))
                        Consider(pt.x, pt.y);
                }
            }

            foreach (var pl in scene.VectorPolylines)
            {
                foreach (var pt in pl.Points)
                {
                    if (IsFiniteFloat(pt.x) && IsFiniteFloat(pt.y))
                        Consider(pt.x, pt.y);
                }
            }

            foreach (var (x1, y1, x2, y2) in scene.TraverseSegments)
            {
                if (IsFiniteFloat(x1) && IsFiniteFloat(y1))
                    Consider(x1, y1);
                if (IsFiniteFloat(x2) && IsFiniteFloat(y2))
                    Consider(x2, y2);
            }

            foreach (var sym in scene.Symbols)
            {
                if (IsFiniteFloat(sym.X) && IsFiniteFloat(sym.Y))
                    Consider(sym.X, sym.Y);
            }

            foreach (var (sx1, sy1, sx2, sy2) in scene.SplaySegments)
            {
                if (IsFiniteFloat(sx1) && IsFiniteFloat(sy1))
                    Consider(sx1, sy1);
                if (IsFiniteFloat(sx2) && IsFiniteFloat(sy2))
                    Consider(sx2, sy2);
            }

            // Union <see cref="PlanScene"/> metadata AABB so fit matches what scene builders recorded
            // (and so splays/walls stay in frame if any numeric path skipped enumeration).
            if (IsFiniteFloat(scene.MinX) && IsFiniteFloat(scene.MaxX) &&
                IsFiniteFloat(scene.MinY) && IsFiniteFloat(scene.MaxY))
            {
                Consider(scene.MinX, scene.MinY);
                Consider(scene.MaxX, scene.MinY);
                Consider(scene.MinX, scene.MaxY);
                Consider(scene.MaxX, scene.MaxY);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Vectors] ComputeRenderingWorldBounds enumeration failed: {ex}");
        }

        if (!has)
        {
            minX = ClampFinite(scene.MinX, 0);
            maxX = ClampFinite(scene.MaxX, 1);
            minY = ClampFinite(scene.MinY, 0);
            maxY = ClampFinite(scene.MaxY, 1);
            if (maxX <= minX)
                maxX = minX + 1;
            if (maxY <= minY)
                maxY = minY + 1;
        }

        if (maxX < minX)
            (minX, maxX) = (maxX, minX);
        if (maxY < minY)
            (minY, maxY) = (maxY, minY);

        Debug.WriteLine(
            $"[Vectors] ComputeRenderingWorldBounds hasGeometry={has} x=[{minX:0.####},{maxX:0.####}] y=[{minY:0.####},{maxY:0.####}] " +
            $"stations={scene.Stations.Count} splays={scene.SplaySegments.Count} walls={scene.WallPolylines.Count} " +
            $"vectors={scene.VectorPolylines.Count} traverseSegs={scene.TraverseSegments.Count}");

        if (!IsFiniteDouble(minX) || !IsFiniteDouble(maxX) || !IsFiniteDouble(minY) || !IsFiniteDouble(maxY))
        {
            minX = 0;
            maxX = 1;
            minY = 0;
            maxY = 1;
        }

        var spanX = maxX - minX;
        var spanY = maxY - minY;
        var span = Math.Max(Math.Max(spanX, spanY), 1e-6);
        if (!IsFiniteDouble(span))
            span = 1;
        var padM = Math.Clamp(Math.Max(span * 0.02, 0.25), 0.01, span * 2 + 10);
        if (!IsFiniteDouble(padM))
            padM = 0.25;

        minX -= padM;
        maxX += padM;
        minY -= padM;
        maxY += padM;

        if (!IsFiniteDouble(minX) || !IsFiniteDouble(maxX) || !IsFiniteDouble(minY) || !IsFiniteDouble(maxY) ||
            maxX <= minX || maxY <= minY)
        {
            return (0, 1, 0, 1);
        }

        return (minX, maxX, minY, maxY);
    }

    /// <summary>
    /// Draws every raster at <see cref="ZIndexRasterUnderlay"/>. Lower-priority rows (see loader ordering) are added first
    /// so higher-priority maps paint above. Opacity is reduced when multiple layers share the same frame so stacked maps stay visible.
    /// </summary>
    private static void AddRasterUnderlayLayers(
        Canvas drawingCanvas,
        IReadOnlyList<PlanRasterUnderlay>? layers,
        bool highContrast,
        double pad,
        double canvasWidth,
        double canvasHeight,
        Func<float, float, Point>? toScreenSurveyMetres,
        Stretch stretchInDestination)
    {
        if (layers == null || layers.Count == 0)
            return;

        var boxW = Math.Max(1, canvasWidth - 2 * pad);
        var boxH = Math.Max(1, canvasHeight - 2 * pad);
        var n = layers.Count;
        var baseOp = highContrast
            ? 0.32
            : SurveyCanvasTheme.IsDark
                ? 0.58
                : 0.66;
        var eachOp = Math.Clamp(baseOp / Math.Sqrt(n), 0.12, 0.88);

        for (var i = n - 1; i >= 0; i--)
        {
            var layer = layers[i];
            double left;
            double top;
            double bw;
            double bh;

            if (toScreenSurveyMetres != null &&
                layer.WorldExtentMetres is { } wx &&
                IsUsableRasterWorldExtent(wx))
            {
                var pNw = toScreenSurveyMetres((float)wx.MinX, (float)wx.MaxY);
                var pSe = toScreenSurveyMetres((float)wx.MaxX, (float)wx.MinY);
                left = Math.Min(pNw.X, pSe.X);
                top = Math.Min(pNw.Y, pSe.Y);
                bw = Math.Abs(pSe.X - pNw.X);
                bh = Math.Abs(pSe.Y - pNw.Y);
                if (bw < 4 || bh < 4 || !IsFiniteDouble(left) || !IsFiniteDouble(top))
                {
                    left = pad;
                    top = pad;
                    bw = boxW;
                    bh = boxH;
                }
            }
            else
            {
                left = pad;
                top = pad;
                bw = boxW;
                bh = boxH;
            }

            var img = new Image
            {
                Source = layer.Bitmap,
                Stretch = stretchInDestination,
                Width = bw,
                Height = bh,
                Opacity = eachOp,
                SnapsToDevicePixels = true,
                ToolTip = $"{layer.Category}\n{layer.ResolvedPath}",
            };
            Canvas.SetLeft(img, left);
            Canvas.SetTop(img, top);
            AddChildZ(drawingCanvas, img, ZIndexRasterUnderlay);
        }
    }

    private static bool IsUsableRasterWorldExtent(PlanRasterWorldExtentMetres wx) =>
        IsFiniteDouble(wx.MinX) && IsFiniteDouble(wx.MaxX) && IsFiniteDouble(wx.MinY) && IsFiniteDouble(wx.MaxY) &&
        wx.MaxX > wx.MinX && wx.MaxY > wx.MinY;

    /// <summary>
    /// Field catalog / rocks / sketch / shot photos: one <see cref="Image"/> per ref, same Z as global rasters, centered on station.
    /// </summary>
    private static void AddStationAttachedSurveyImages(
        Canvas drawingCanvas,
        PlanScene scene,
        CaveProjectDocument? project,
        string? zipPath,
        bool highContrast,
        Func<float, float, Point> toScreen,
        double pxPerMetre)
    {
        if (project == null || scene.StationAttachedImages.Count == 0)
            return;

        const double defaultWidthMetres = 2.75;
        foreach (var r in scene.StationAttachedImages)
        {
            if (!scene.Stations.TryGetValue(r.StationName, out var sc))
                continue;

            BitmapSource? bmp;
            try
            {
                bmp = PlanMapUnderlayLoader.TryLoadProjectImageBitmap(project, zipPath, r.UriOrPath);
            }
            catch
            {
                continue;
            }

            if (bmp == null)
                continue;

            var center = toScreen(sc.X, sc.Y);
            if (!IsFiniteDouble(center.X) || !IsFiniteDouble(center.Y))
                continue;

            var scaleHint = r.ScaleHint is > 0 and < 20 ? r.ScaleHint.Value : 1.0;
            var targetW = Math.Clamp(defaultWidthMetres * pxPerMetre * scaleHint, 28, 520);
            var aspect = bmp.PixelHeight / Math.Max(1.0, (double)bmp.PixelWidth);
            var w = targetW;
            var h = w * aspect;
            if (!IsFiniteDouble(h) || h < 6)
                h = 48;

            var img = new Image
            {
                Source = bmp,
                Width = w,
                Height = h,
                Stretch = Stretch.Uniform,
                Opacity = highContrast ? 0.9 : 0.93,
                SnapsToDevicePixels = true,
                ToolTip = $"{r.Category}\n{r.StationName}\n{r.UriOrPath}",
            };

            img.RenderTransformOrigin = new Point(0.5, 0.5);
            var rotDeg = r.RotationDegrees ?? 0;
            img.RenderTransform = Math.Abs(rotDeg) > 1e-4
                ? new RotateTransform(rotDeg)
                : Transform.Identity;

            Canvas.SetLeft(img, center.X - w * 0.5);
            Canvas.SetTop(img, center.Y - h * 0.5);
            AddChildZ(drawingCanvas, img, ZIndexRasterUnderlay);
        }
    }

    public static void Draw(
        PlanScene scene,
        Canvas drawingCanvas,
        bool highContrast,
        double canvasWidth,
        double canvasHeight,
        IReadOnlyList<PlanRasterUnderlay>? rasterUnderlays = null,
        CaveProjectDocument? stationImageResolveProject = null,
        string? stationImageResolveZipPath = null,
        PlanCanvasDrawOptions? drawOptions = null)
    {
        var opt = drawOptions ?? new PlanCanvasDrawOptions();

        drawingCanvas.Children.Clear();
        ApplySurveyRenderQuality(drawingCanvas);
        drawingCanvas.Background = opt.VisualizationMode.UsesDarkSurveyCanvas()
            ? new SolidColorBrush(Color.FromRgb(10, 10, 14))
            : Brushes.Transparent;
        const double pad = 48;
        drawingCanvas.Width = canvasWidth;
        drawingCanvas.Height = canvasHeight;

        var (wMinX, wMaxX, wMinY, wMaxY) = ComputeRenderingWorldBounds(scene);
        var wSpanX = Math.Max(1e-6, wMaxX - wMinX);
        var wSpanY = Math.Max(1e-6, wMaxY - wMinY);
        if (wSpanX > 1e7 || wSpanY > 1e7)
            Debug.WriteLine($"[Vectors] Warning: very large world span ({wSpanX},{wSpanY}) — check wall/vector units in JSON.");
        var usableW = Math.Max(1, canvasWidth - 2 * pad);
        var usableH = Math.Max(1, canvasHeight - 2 * pad);
        var scale = Math.Min(usableW / wSpanX, usableH / wSpanY);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            scale = 1;
        var pxPerMetre = scale;
        var plotW = wSpanX * scale;
        var plotH = wSpanY * scale;
        var originX = pad + (usableW - plotW) * 0.5;
        var originY = pad + (usableH - plotH) * 0.5;
        if (!IsFiniteDouble(originX) || !IsFiniteDouble(originY))
        {
            originX = pad;
            originY = pad;
        }

        if (!IsFiniteDouble(pxPerMetre) || pxPerMetre <= 0)
            pxPerMetre = 1;

        Debug.WriteLine(
            $"[Vectors] worldBounds x=[{wMinX:0.##},{wMaxX:0.##}] y=[{wMinY:0.##},{wMaxY:0.##}] span≈{wSpanX:0.##}×{wSpanY:0.##} m · scale={scale:0.####} px/m · origin=({originX:0.#},{originY:0.#})");

        var sty = ResolveSurveyVectorStyle(opt, highContrast);
        var vMode = opt.VisualizationMode;
        var plan2Tone = vMode == SurveyVisualizationMode.Plan2Tone;
        var sectionNight = vMode == SurveyVisualizationMode.SectionNight;
        var splayXRay = vMode.ShowSplayXRayGeometry();

        Brush lrudPlanFill;
        Brush lrudPlanStroke;
        if (highContrast)
        {
            lrudPlanFill = new SolidColorBrush(Color.FromArgb((byte)(splayXRay ? 0x38 : 0x52), 0xFF, 0xFF, 0xFF));
            lrudPlanStroke = Brushes.White;
        }
        else if (plan2Tone)
        {
            // Semi-transparent neutral fill for passage volume (Plan 2-Tone).
            lrudPlanFill = new SolidColorBrush(Color.FromArgb(0x1A, 0x80, 0x80, 0x80));
            lrudPlanStroke = new SolidColorBrush(Color.FromArgb(230, 0x42, 0x48, 0x52));
        }
        else if (vMode.UsesDarkSurveyCanvas())
        {
            lrudPlanFill = new SolidColorBrush(Color.FromArgb((byte)(splayXRay ? 0x40 : 0x58), 0x48, 0x55, 0x66));
            lrudPlanStroke = new SolidColorBrush(Color.FromArgb(0xCC, 0x9A, 0xA8, 0xB8));
        }
        else
        {
            lrudPlanFill = new SolidColorBrush(Color.FromArgb((byte)(splayXRay ? 0x40 : 0x55), 0xC8, 0xCC, 0xD4));
            lrudPlanStroke = new SolidColorBrush(Color.FromArgb(0xAA, 0x58, 0x5C, 0x62));
        }

        var lrudProfileFill = new SolidColorBrush(
            Color.FromArgb((byte)(sectionNight ? (splayXRay ? 20 : 28) : (splayXRay ? 48 : 72)), 0x70, 0x90, 0xB0));
        var lrudProfileStroke = sectionNight
            ? (highContrast ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xF2, 0xF8, 0xFF)))
            : lrudPlanStroke;

        var lrud3dFaceFill = highContrast
            ? new SolidColorBrush(Color.FromArgb(0x32, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x40, 0x6B, 0x7A, 0x9E));
        var lrud3dFaceStroke = highContrast
            ? new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x88, 0xA8, 0xB8, 0xD8));
        var lrud3dEdgeStroke = highContrast
            ? new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x52, 0x88, 0x98, 0xB0));

        var vectorStrokeThickness = Math.Max(2.0, Math.Min(8.0, pxPerMetre * 0.15));
        if (!IsFiniteDouble(vectorStrokeThickness))
            vectorStrokeThickness = 2.0;
        var splayStrokeThickness = Math.Max(1.35, Math.Min(5.0, pxPerMetre * 0.12));
        if (!IsFiniteDouble(splayStrokeThickness))
            splayStrokeThickness = 1.35;
        if (splayXRay)
        {
            // Soft "cloud" of overlapping splays (Android-style depth hint).
            splayStrokeThickness = 0.5;
        }

        var wallStrokeCount = scene.WallPolylines.Count;
        var wallPointCount = 0;
        foreach (var w in scene.WallPolylines)
            wallPointCount += w.Points.Count;

        Debug.WriteLine(
            $"[Vectors] Draw viz={opt.VisualizationMode} canvas={canvasWidth:0.#}×{canvasHeight:0.#} " +
            $"stations={scene.Stations.Count} traverseSegs={scene.TraverseSegments.Count} " +
            $"walls={wallStrokeCount} (pts={wallPointCount}) splays={scene.SplaySegments.Count} " +
            $"vectors={scene.VectorPolylines.Count} pxPerM={pxPerMetre:0.######} " +
            $"vectorStrokePx={vectorStrokeThickness:0.##} splayStrokePx={splayStrokeThickness:0.##} splayOpacity={sty.SplayOpacity:0.##}");

        Point ToScreen(float x, float y)
        {
            if (!IsFiniteFloat(x) || !IsFiniteFloat(y))
                return new Point(0, 0);
            var px = originX + ((double)x - wMinX) * scale;
            var py = originY + (wMaxY - (double)y) * scale;
            if (!IsFiniteDouble(px) || !IsFiniteDouble(py))
                return new Point(0, 0);
            return new Point(px, py);
        }

        if (scene.SplaySegments.Count > 0)
        {
            var (sx1, sy1, sx2, sy2) = scene.SplaySegments[0];
            var p0 = ToScreen(sx1, sy1);
            var p1 = ToScreen(sx2, sy2);
            Debug.WriteLine(
                $"[Vectors] Sample splay #0 survey ({sx1:0.###},{sy1:0.###})→({sx2:0.###},{sy2:0.###}) " +
                $"screen ({p0.X:0.#},{p0.Y:0.#})→({p1.X:0.#},{p1.Y:0.#}) (same ToScreen as walls/traverse)");
        }

        if (scene.WallPolylines.Count > 0 && scene.WallPolylines[0].Points.Count > 0)
        {
            var (wx, wy) = scene.WallPolylines[0].Points[0];
            var wp = ToScreen(wx, wy);
            Debug.WriteLine(
                $"[Vectors] Sample wall pt0 survey ({wx:0.###},{wy:0.###}) -> screen ({wp.X:0.#},{wp.Y:0.#})");
        }

        var suppressRaster = opt.VisualizationMode.SuppressRasterUnderlays();
        AddRasterUnderlayLayers(
            drawingCanvas,
            suppressRaster ? null : rasterUnderlays,
            highContrast,
            pad,
            canvasWidth,
            canvasHeight,
            ToScreen,
            Stretch.Fill);

        AddStationAttachedSurveyImages(
            drawingCanvas,
            scene,
            suppressRaster ? null : stationImageResolveProject,
            suppressRaster ? null : stationImageResolveZipPath,
            highContrast,
            ToScreen,
            pxPerMetre);

        var vectorLayer = new Canvas
        {
            Width = canvasWidth,
            Height = canvasHeight,
            Background = Brushes.Transparent,
            IsHitTestVisible = true,
        };
        Panel.SetZIndex(vectorLayer, ZIndexSurveyVectors);
        drawingCanvas.Children.Add(vectorLayer);
        ApplySurveyRenderQuality(vectorLayer);

        var wallLayer = new Canvas
        {
            Width = canvasWidth,
            Height = canvasHeight,
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        var fgLayer = new Canvas
        {
            Width = canvasWidth,
            Height = canvasHeight,
            Background = Brushes.Transparent,
            IsHitTestVisible = true,
        };
        Panel.SetZIndex(wallLayer, 0);
        Panel.SetZIndex(fgLayer, 1);
        vectorLayer.Children.Add(wallLayer);
        vectorLayer.Children.Add(fgLayer);
        ApplySurveyRenderQuality(wallLayer);
        ApplySurveyRenderQuality(fgLayer);

        void AddVectorElement(UIElement el)
        {
            ApplySurveyRenderQuality(el);
            fgLayer.Children.Add(el);
        }

        void AddWallBehindTraverse(UIElement el)
        {
            ApplySurveyRenderQuality(el);
            wallLayer.Children.Add(el);
        }

        static List<Point> ToScreenPolyline(Func<float, float, Point> toScreen, SurveyStationGeometry.PlanVectorPolyline pl)
        {
            var list = new List<Point>(pl.Points.Count);
            foreach (var (vx, vy) in pl.Points)
            {
                if (!IsFiniteFloat(vx) || !IsFiniteFloat(vy))
                    continue;
                var p = toScreen(vx, vy);
                if (IsFiniteDouble(p.X) && IsFiniteDouble(p.Y))
                    list.Add(p);
            }

            return list;
        }

        void AddClosedSmoothedWall(
            SurveyStationGeometry.PlanVectorPolyline pl,
            Brush fill,
            Brush stroke,
            double thickness,
            Action<UIElement> addChild)
        {
            if (pl.Points.Count < 3)
                return;
            var pts = ToScreenPolyline(ToScreen, pl);
            if (pts.Count < 3)
                return;
            var geom = SurveyPathSmoothing.TryBuildClosedCatmullRomPath(pts);
            if (geom == null)
                return;
            var path = new Path
            {
                Data = geom,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                SnapsToDevicePixels = false,
            };
            ApplySurveyRenderQuality(path);
            addChild(path);
        }

        void AddOpenSmoothedWall(
            SurveyStationGeometry.PlanVectorPolyline pl,
            Brush stroke,
            double thickness,
            Action<UIElement> addChild)
        {
            if (pl.Points.Count < 2)
                return;
            var pts = ToScreenPolyline(ToScreen, pl);
            if (pts.Count < 2)
                return;
            if (pts.Count == 2)
            {
                var seg = new LineSegment(pts[1], true);
                var fig = new PathFigure(pts[0], new[] { seg }, false);
                var g2 = new PathGeometry(new[] { fig });
                var path2 = new Path
                {
                    Data = g2,
                    Fill = Brushes.Transparent,
                    Stroke = stroke,
                    StrokeThickness = thickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    SnapsToDevicePixels = false,
                };
                ApplySurveyRenderQuality(path2);
                addChild(path2);
                return;
            }

            var geom = SurveyPathSmoothing.TryBuildOpenCatmullRomPath(pts);
            if (geom == null)
                return;
            var path = new Path
            {
                Data = geom,
                Fill = Brushes.Transparent,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                SnapsToDevicePixels = false,
            };
            ApplySurveyRenderQuality(path);
            addChild(path);
        }

        var wallIndex = 0;
        foreach (var pl in scene.WallPolylines)
        {
            // X-ray: scene has no LRUD hull; skip any legacy passage polys so only radials draw.
            if (splayXRay && pl.Type is "lrudPlanRibbon" or "lrudPlan" or "lrudProfile")
                continue;

            switch (pl.Type)
            {
                case "lrudPlanRibbon":
                {
                    Brush ribbonFill;
                    Brush ribbonStroke;
                    double ribbonTh;
                    if (plan2Tone)
                    {
                        ribbonFill = new SolidColorBrush(Color.FromArgb(0x1A, 0x80, 0x80, 0x80));
                        ribbonStroke = new SolidColorBrush(Color.FromArgb(210, 0x46, 0x4C, 0x54));
                        ribbonTh = Math.Max(1.0, vectorStrokeThickness * 0.34);
                    }
                    else if (highContrast)
                    {
                        ribbonFill = lrudPlanFill;
                        ribbonStroke = lrudPlanStroke;
                        ribbonTh = Math.Max(1.5, vectorStrokeThickness * 0.52);
                    }
                    else
                    {
                        ribbonFill = lrudPlanFill;
                        ribbonStroke = lrudPlanStroke;
                        ribbonTh = Math.Max(1.0, vectorStrokeThickness * 0.44);
                    }

                    if (pl.Closed && pl.Points.Count >= 3)
                        AddClosedSmoothedWall(pl, ribbonFill, ribbonStroke, ribbonTh, AddWallBehindTraverse);
                    break;
                }
                case "lrud3dEdge":
                    if (pl.Points.Count >= 2)
                        AddOpenSmoothedWall(pl, lrud3dEdgeStroke, Math.Max(0.75, vectorStrokeThickness * 0.32), AddWallBehindTraverse);
                    break;
                case "lrud3dFace":
                    if (pl.Closed && pl.Points.Count >= 3)
                        AddClosedSmoothedWall(
                            pl,
                            lrud3dFaceFill,
                            lrud3dFaceStroke,
                            Math.Max(0.85, vectorStrokeThickness * 0.42),
                            AddWallBehindTraverse);
                    break;
                case "lrudPlan":
                    if (pl.Closed && pl.Points.Count >= 3)
                        AddClosedSmoothedWall(
                            pl,
                            lrudPlanFill,
                            lrudPlanStroke,
                            Math.Max(1.1, vectorStrokeThickness * 0.62),
                            AddWallBehindTraverse);
                    break;
                case "lrudProfile":
                {
                    var profTh = sectionNight
                        ? Math.Max(2.4, vectorStrokeThickness * 1.35)
                        : Math.Max(1.1, vectorStrokeThickness * 0.62);
                    if (pl.Closed && pl.Points.Count >= 3)
                        AddClosedSmoothedWall(pl, lrudProfileFill, lrudProfileStroke, profTh, AddWallBehindTraverse);
                    break;
                }
                default:
                {
                    var wallStroke = sty.AlternateWallStrokes && wallIndex % 2 == 1
                        ? sty.WallStrokeAlt
                        : sty.WallStrokePrimary;
                    wallIndex++;
                    var sketchTh = sectionNight
                        ? Math.Max(2.2, vectorStrokeThickness * 1.22)
                        : vectorStrokeThickness;
                    if (pl.Closed && pl.Points.Count >= 3)
                        AddClosedSmoothedWall(pl, sty.SketchFill, wallStroke, sketchTh, AddVectorElement);
                    else if (pl.Points.Count >= 2)
                        AddOpenSmoothedWall(pl, wallStroke, sketchTh, AddVectorElement);
                    break;
                }
            }
        }

        foreach (var pl in scene.VectorPolylines)
        {
            if (pl.Closed && pl.Points.Count >= 3)
                AddClosedSmoothedWall(pl, sty.VectorFill, sty.TraverseStroke, vectorStrokeThickness, AddVectorElement);
            else if (pl.Points.Count >= 2)
                AddOpenSmoothedWall(pl, sty.TraverseStroke, vectorStrokeThickness, AddVectorElement);
        }

        foreach (var (x1, y1, x2, y2) in scene.TraverseSegments)
        {
            var pa = ToScreen(x1, y1);
            var pb = ToScreen(x2, y2);
            var leg = new Line
            {
                X1 = pa.X,
                Y1 = pa.Y,
                X2 = pb.X,
                Y2 = pb.Y,
                Stroke = sty.TraverseStroke,
                StrokeThickness = vectorStrokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };
            AddVectorElement(leg);
        }

        var splayStrokeBrush = splayXRay
            ? (highContrast ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xE8, 0xDC, 0xC4)))
            : sty.SplayStroke;
        var splayLineOpacity = splayXRay ? (highContrast ? 0.18 : 0.15) : sty.SplayOpacity;

        if (!plan2Tone)
        {
            foreach (var (sx1, sy1, sx2, sy2) in scene.SplaySegments)
            {
                if (!IsFiniteFloat(sx1) || !IsFiniteFloat(sy1) || !IsFiniteFloat(sx2) || !IsFiniteFloat(sy2))
                    continue;
                var pa = ToScreen(sx1, sy1);
                var pb = ToScreen(sx2, sy2);
                var splayLine = new Line
                {
                    X1 = pa.X,
                    Y1 = pa.Y,
                    X2 = pb.X,
                    Y2 = pb.Y,
                    Stroke = splayStrokeBrush,
                    StrokeThickness = splayXRay ? 0.5 : splayStrokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Opacity = splayLineOpacity,
                };
                AddVectorElement(splayLine);
            }
        }

        var stationDot = Math.Max(6.0, Math.Min(20.0, pxPerMetre * 0.4));
        var stationHalf = stationDot * 0.5;

        foreach (var c in scene.Stations.Values)
        {
            var pt = ToScreen(c.X, c.Y);
            var el = new Ellipse
            {
                Width = stationDot,
                Height = stationDot,
                Fill = sty.StationFill,
                Stroke = sty.TraverseStroke,
                StrokeThickness = vectorStrokeThickness,
            };
            Canvas.SetLeft(el, pt.X - stationHalf);
            Canvas.SetTop(el, pt.Y - stationHalf);
            AddVectorElement(el);
        }

        foreach (var sym in scene.Symbols)
        {
            var pt = ToScreen(sym.X, sym.Y);
            var symSize = Math.Max(7.0, Math.Min(18.0, pxPerMetre * 0.38));
            var symHalf = symSize * 0.5;
            var dot = new Ellipse
            {
                Width = symSize,
                Height = symSize,
                Fill = sty.SymbolFill,
                Stroke = sty.TraverseStroke,
                StrokeThickness = vectorStrokeThickness,
                Opacity = 0.98,
            };
            Canvas.SetLeft(dot, pt.X - symHalf);
            Canvas.SetTop(dot, pt.Y - symHalf);
            AddVectorElement(dot);
        }

        if (opt.ShowStationNames)
        {
            foreach (var c in scene.Stations.Values)
            {
                var pt = ToScreen(c.X, c.Y);
                var lab = new TextBlock
                {
                    Text = c.Name,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = sty.Legend,
                };
                if (!highContrast)
                {
                    lab.Effect = new DropShadowEffect
                    {
                        BlurRadius = 3,
                        ShadowDepth = 0,
                        Color = SurveyCanvasTheme.IsDark ? Colors.Black : Colors.White,
                        Opacity = 0.85,
                    };
                }

                Canvas.SetLeft(lab, pt.X + 7);
                Canvas.SetTop(lab, pt.Y - 14);
                AddVectorElement(lab);
            }
        }

        var legCount = scene.TraverseSegments.Count;
        var nMaps = suppressRaster ? 0 : (rasterUnderlays?.Count ?? 0);
        var nStationImg = suppressRaster ? 0 : scene.StationAttachedImages.Count;
        var underNote = nMaps > 0
            ? (nMaps == 1
                ? " · 1 raster map underlay (per-map JSON extent when present; else same fit as vectors)"
                : $" · {nMaps} raster map underlays (stacked; per-map JSON extent when present)")
            : "";
        var stationImgNote = nStationImg > 0
            ? (nStationImg == 1 ? " · 1 station-attached image" : $" · {nStationImg} station-attached images")
            : "";
        var spanNote = $" · extent ≈ {wSpanX:0.#} × {wSpanY:0.#} m (fit bounds)";
        var tb = new TextBlock
        {
            Text =
                $"{scene.Stations.Count} stations · {legCount} traverse leg(s) · {scene.WallPolylines.Count} wall stroke(s) · {scene.VectorPolylines.Count} vector overlay(s) · {scene.Symbols.Count} symbol(s){underNote}{stationImgNote}{spanNote}",
            Foreground = sty.Legend,
            FontSize = 11,
            MaxWidth = Math.Max(200, canvasWidth - 20),
            TextWrapping = TextWrapping.Wrap,
        };
        Canvas.SetLeft(tb, 8);
        Canvas.SetTop(tb, canvasHeight - 100);
        AddChildZ(drawingCanvas, tb, ZIndexSurveyAnnotation);

        if (opt.ShowCartographyOverlay)
        {
            var fitCx = (float)((wMinX + wMaxX) * 0.5);
            var fitCy = (float)((wMinY + wMaxY) * 0.5);
            AddCartographicOverlays(
                drawingCanvas,
                scene,
                ToScreen,
                pxPerMetre,
                canvasWidth,
                canvasHeight,
                sty.Legend,
                highContrast,
                opt.CanvasKind,
                fitCx,
                fitCy,
                (float)wSpanX,
                (float)wSpanY);
        }
    }

    private static double NiceScaleBarMetres(double rawMetres)
    {
        if (rawMetres <= 0 || double.IsNaN(rawMetres) || double.IsInfinity(rawMetres))
            return 1;
        var p = Math.Pow(10, Math.Floor(Math.Log10(rawMetres)));
        var m = rawMetres / p;
        if (m <= 1.5) return p;
        if (m <= 3.5) return 2 * p;
        if (m <= 7.5) return 5 * p;
        return 10 * p;
    }

    private static void AddCartographicOverlays(
        Canvas canvas,
        PlanScene scene,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        double canvasWidth,
        double canvasHeight,
        Brush fg,
        bool highContrast,
        SurveyCanvasKind canvasKind,
        float fitCenterX,
        float fitCenterY,
        float fitSpanX,
        float fitSpanY)
    {
        if (pxPerMetre <= 1e-9)
            return;

        var chipBg = highContrast
            ? new SolidColorBrush(Color.FromArgb(235, 255, 255, 255))
            : SurveyCanvasTheme.IsDark
                ? new SolidColorBrush(Color.FromArgb(210, 28, 33, 40))
                : new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
        var chipBorder = highContrast
            ? new SolidColorBrush(Color.FromRgb(40, 40, 40))
            : SurveyCanvasTheme.IsDark
                ? new SolidColorBrush(Color.FromRgb(80, 86, 96))
                : new SolidColorBrush(Color.FromRgb(190, 194, 200));

        const double targetPx = 130;
        var barM = NiceScaleBarMetres(targetPx / pxPerMetre);
        var barPx = barM * pxPerMetre;
        var scalePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var bar = new Border
        {
            Width = barPx,
            Height = 4,
            Background = fg,
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var scaleText = new TextBlock
        {
            Text = $"0 — {barM:0.##} m",
            Foreground = fg,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        scalePanel.Children.Add(bar);
        scalePanel.Children.Add(scaleText);
        var scaleChip = new Border
        {
            Background = chipBg,
            BorderBrush = chipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 12, 6),
            Child = scalePanel,
            SnapsToDevicePixels = true,
        };
        Canvas.SetLeft(scaleChip, 10);
        Canvas.SetTop(scaleChip, canvasHeight - 52);
        AddChildZ(canvas, scaleChip, ZIndexCartographyChrome);

        var cx = fitCenterX;
        var cy = fitCenterY;
        var p0 = toScreen(cx, cy);
        var dySurvey = Math.Max(1e-4f, fitSpanY * 0.05f);
        var dxSurvey = Math.Max(1e-4f, fitSpanX * 0.05f);
        var pNy = toScreen(cx, cy + dySurvey);
        var pEx = toScreen(cx + dxSurvey, cy);
        var nX = pNy.X - p0.X;
        var nY = pNy.Y - p0.Y;
        var lenN = Math.Sqrt(nX * nX + nY * nY);
        if (lenN > 1e-3)
        {
            nX /= lenN;
            nY /= lenN;
            var eX = pEx.X - p0.X;
            var eY = pEx.Y - p0.Y;
            var lenE = Math.Sqrt(eX * eX + eY * eY);
            if (lenE > 1e-3)
            {
                eX /= lenE;
                eY /= lenE;
            }
            else
            {
                eX = -nY;
                eY = nX;
            }

            var ax = canvasWidth - 78;
            var ay = canvasHeight - 48;
            var tip = new Point(ax + nX * 26, ay + nY * 26);
            var tail = new Point(ax - nX * 10, ay - nY * 10);
            var px = -nY;
            var py = nX;
            const double wing = 8.5;
            var w1 = new Point(tip.X - nX * 12 + px * wing, tip.Y - nY * 12 + py * wing);
            var w2 = new Point(tip.X - nX * 12 - px * wing, tip.Y - nY * 12 - py * wing);
            var fig = new PathFigure(tip, new PathSegment[] { new LineSegment(w1, true), new LineSegment(w2, true) }, true);
            var geom = new PathGeometry(new[] { fig });
            var shaft = new Line
            {
                X1 = tail.X,
                Y1 = tail.Y,
                X2 = tip.X - nX * 6,
                Y2 = tip.Y - nY * 6,
                Stroke = fg,
                StrokeThickness = 2.25,
                StrokeEndLineCap = PenLineCap.Round,
                SnapsToDevicePixels = true,
            };
            AddChildZ(canvas, shaft, ZIndexCartographyChrome);
            var head = new Path
            {
                Fill = fg,
                Data = geom,
                SnapsToDevicePixels = true,
            };
            AddChildZ(canvas, head, ZIndexCartographyChrome);

            var northLabel = canvasKind == SurveyCanvasKind.Section
                ? "Developed distance →"
                : "+Y survey";
            var ntb = new TextBlock
            {
                Text = northLabel,
                Foreground = fg,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Effect = !highContrast
                    ? new DropShadowEffect
                    {
                        BlurRadius = 2,
                        ShadowDepth = 0,
                        Color = SurveyCanvasTheme.IsDark ? Colors.Black : Colors.White,
                        Opacity = 0.8,
                    }
                    : null,
            };
            Canvas.SetLeft(ntb, ax - 28);
            Canvas.SetTop(ntb, ay + 18);
            AddChildZ(canvas, ntb, ZIndexCartographyChrome);

            AddSurveyCompassRose(canvas, fg, chipBg, chipBorder, nX, nY, eX, eY, canvasKind);
        }
    }

    /// <summary>Small survey-frame compass (N/E/S/W) — N matches Android plan convention 0° = +Y survey axis, not device north.</summary>
    private static void AddSurveyCompassRose(
        Canvas canvas,
        Brush fg,
        Brush chipBg,
        Brush chipBorder,
        double nX,
        double nY,
        double eX,
        double eY,
        SurveyCanvasKind canvasKind)
    {
        const double pad = 10;
        const double box = 112;
        const double rcx = 56;
        const double rcy = 56;
        const double rOuter = 34;
        const double rInner = 26;
        const double rLabel = 40;

        var sX = -nX;
        var sY = -nY;
        var wX = -eX;
        var wY = -eY;

        var inner = new Canvas { Width = box, Height = box };
        var ring = new Ellipse
        {
            Width = rOuter * 2,
            Height = rOuter * 2,
            Stroke = fg,
            StrokeThickness = 1.65,
            Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(ring, rcx - rOuter);
        Canvas.SetTop(ring, rcy - rOuter);
        inner.Children.Add(ring);

        for (var step = 0; step < 8; step++)
        {
            var deg = step * 45.0;
            var th = deg * (Math.PI / 180.0);
            var ux = Math.Sin(th) * eX + Math.Cos(th) * nX;
            var uy = Math.Sin(th) * eY + Math.Cos(th) * nY;
            var ulen = Math.Sqrt(ux * ux + uy * uy);
            if (ulen < 1e-9)
                continue;
            ux /= ulen;
            uy /= ulen;
            var thick = step % 2 == 0 ? 1.35 : 0.75;
            var op = step % 2 == 0 ? 1.0 : 0.55;
            inner.Children.Add(new Line
            {
                X1 = rcx + ux * rInner,
                Y1 = rcy + uy * rInner,
                X2 = rcx + ux * rOuter,
                Y2 = rcy + uy * rOuter,
                Stroke = fg,
                StrokeThickness = thick,
                Opacity = op,
                SnapsToDevicePixels = true,
            });
        }

        void addLetter(string letter, double lx, double ly)
        {
            var tb = new TextBlock
            {
                Text = letter,
                Foreground = fg,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
            };
            tb.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, lx - tb.DesiredSize.Width * 0.5);
            Canvas.SetTop(tb, ly - tb.DesiredSize.Height * 0.5);
            inner.Children.Add(tb);
        }

        addLetter("N", rcx + nX * rLabel, rcy + nY * rLabel);
        addLetter("E", rcx + eX * rLabel, rcy + eY * rLabel);
        addLetter("S", rcx + sX * rLabel, rcy + sY * rLabel);
        addLetter("W", rcx + wX * rLabel, rcy + wY * rLabel);

        var sub = canvasKind == SurveyCanvasKind.Section
            ? "Horizontal = chainage (m), vertical = Z (m)"
            : "+Y = N (plan)";
        var hint = new TextBlock
        {
            Text = sub,
            Foreground = fg,
            FontSize = 8.5,
            Opacity = 0.88,
            TextAlignment = TextAlignment.Center,
            Width = box,
        };
        Canvas.SetLeft(hint, 0);
        Canvas.SetTop(hint, box - 18);
        inner.Children.Add(hint);

        var chip = new Border
        {
            Background = chipBg,
            BorderBrush = chipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = inner,
            SnapsToDevicePixels = true,
            ToolTip =
                "Survey compass: N aligns with +Y in the survey frame (same as CaveAI Pro Android plan / rotation-vector heading math vs map). Not magnetic north.",
        };
        Canvas.SetLeft(chip, pad);
        Canvas.SetTop(chip, pad);
        AddChildZ(canvas, chip, ZIndexCartographyChrome);
    }

    /// <summary>
    /// When there is no <see cref="PlanScene"/> (no traverse/sketches), still show resolvable map rasters
    /// so users can preview them. Aspect ratio preserved inside the padded box when extents are unknown.
    /// </summary>
    public static void DrawRasterUnderlaysOnly(
        Canvas drawingCanvas,
        bool highContrast,
        double canvasWidth,
        double canvasHeight,
        IReadOnlyList<PlanRasterUnderlay> underlays,
        string viewNameForLegend)
    {
        drawingCanvas.Children.Clear();
        const double pad = 48;
        drawingCanvas.Width = canvasWidth;
        drawingCanvas.Height = canvasHeight;

        AddRasterUnderlayLayers(
            drawingCanvas,
            underlays,
            highContrast,
            pad,
            canvasWidth,
            canvasHeight,
            toScreenSurveyMetres: null,
            Stretch.Uniform);

        var hintMuted = SurveyCanvasTheme.IsDark ? Color.FromRgb(0x94, 0xA3, 0xB8) : Color.FromRgb(0x65, 0x67, 0x6B);
        var hintFg = highContrast ? Brushes.Black : new SolidColorBrush(hintMuted);
        var n = underlays.Count;
        var hint = new TextBlock
        {
            Text =
                n <= 1
                    ? $"No survey geometry in this project — showing map image only ({viewNameForLegend}). Not georeferenced; add traverse or sketches in CaveAI Pro (Android) to align vectors."
                    : $"No survey geometry — showing {n} map image(s) stacked ({viewNameForLegend}). Per-map JSON extent is used when present.",
            Foreground = hintFg,
            FontSize = 12,
            MaxWidth = 640,
            TextWrapping = TextWrapping.Wrap,
        };
        Canvas.SetLeft(hint, 12);
        Canvas.SetTop(hint, 10);
        AddChildZ(drawingCanvas, hint, ZIndexCartographyChrome);

        var legFg = highContrast ? Brushes.Black : hintFg;
        var tb = new TextBlock
        {
            Text =
                n <= 1
                    ? $"Raster preview · {viewNameForLegend} · no stations or vectors drawn"
                    : $"Raster preview · {viewNameForLegend} · {n} map layer(s) · no stations or vectors drawn",
            Foreground = legFg,
            FontSize = 13,
            MaxWidth = 560,
            TextWrapping = TextWrapping.Wrap,
        };
        Canvas.SetLeft(tb, 8);
        Canvas.SetTop(tb, canvasHeight - 22);
        AddChildZ(drawingCanvas, tb, ZIndexSurveyAnnotation);
    }
}
