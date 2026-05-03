using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>Renders a <see cref="PlanScene"/> onto a WPF <see cref="Canvas"/> (plan or section).</summary>
public static class PlanCanvasRenderer
{
    public static void Draw(
        PlanScene scene,
        Canvas drawingCanvas,
        bool highContrast,
        double canvasWidth,
        double canvasHeight,
        BitmapSource? rasterUnderlay = null,
        PlanCanvasDrawOptions? drawOptions = null)
    {
        var opt = drawOptions ?? new PlanCanvasDrawOptions();
        drawingCanvas.Children.Clear();
        const double pad = 48;
        drawingCanvas.Width = canvasWidth;
        drawingCanvas.Height = canvasHeight;

        if (rasterUnderlay != null)
        {
            var underlayOpacity = highContrast
                ? 0.32
                : SurveyCanvasTheme.IsDark
                    ? 0.58
                    : 0.66;
            var img = new Image
            {
                Source = rasterUnderlay,
                Opacity = underlayOpacity,
                Stretch = Stretch.Fill,
                Width = canvasWidth - 2 * pad,
                Height = canvasHeight - 2 * pad,
                SnapsToDevicePixels = true,
            };
            Canvas.SetLeft(img, pad);
            Canvas.SetTop(img, pad);
            drawingCanvas.Children.Add(img);
        }

        var spanX = scene.SpanX;
        var spanY = scene.SpanY;
        var sx = (canvasWidth - 2 * pad) / spanX;
        var sy = (canvasHeight - 2 * pad) / spanY;
        var scale = Math.Min(sx, sy);
        var pxPerMetre = (double)scale;
        var minX = scene.MinX;
        var maxY = scene.MaxY;

        Point ToScreen(float x, float y)
        {
            var px = pad + (x - minX) * scale;
            var py = pad + (maxY - y) * scale;
            return new Point(px, py);
        }

        var sketchStroke = Brushes.Black;
        var sketchFill = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80));
        var vectorBrush = Brushes.DarkBlue;
        var vectorFill = new SolidColorBrush(Color.FromArgb(0x45, 0x40, 0x40, 0x90));
        var traverseColor = Brushes.Black;
        var stationFill = Brushes.White;
        var stationRing = Brushes.Black;
        var symbolBrush = Brushes.DarkMagenta;
        var symbolRing = Brushes.Black;
        var legend = Brushes.Black;

        if (!highContrast)
        {
            if (SurveyCanvasTheme.IsDark)
            {
                sketchStroke = new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C));
                sketchFill = new SolidColorBrush(Color.FromArgb(0x48, 0xFB, 0x92, 0x3C));
                vectorBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
                vectorFill = new SolidColorBrush(Color.FromArgb(0x38, 0x38, 0xBD, 0xF8));
                traverseColor = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                stationFill = new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15));
                stationRing = new SolidColorBrush(Color.FromRgb(0x15, 0x64, 0x3C));
                symbolBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x79, 0xF9));
                symbolRing = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));
                legend = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            }
            else
            {
                sketchStroke = new SolidColorBrush(Color.FromRgb(0xC2, 0x41, 0x0C));
                sketchFill = new SolidColorBrush(Color.FromArgb(0x55, 0xEA, 0x58, 0x0C));
                vectorBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x77, 0xF2));
                vectorFill = new SolidColorBrush(Color.FromArgb(0x40, 0x18, 0x77, 0xF2));
                traverseColor = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                stationFill = new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15));
                stationRing = new SolidColorBrush(Color.FromRgb(0x15, 0x64, 0x3C));
                symbolBrush = new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xD7));
                symbolRing = new SolidColorBrush(Color.FromRgb(0x5B, 0x21, 0xB6));
                legend = new SolidColorBrush(Color.FromRgb(0x65, 0x67, 0x6B));
            }
        }

        void AddClosedPolygon(SurveyStationGeometry.PlanVectorPolyline pl, Brush fill, Brush stroke, double thickness)
        {
            if (pl.Points.Count < 3)
                return;
            var pg = new Polygon
            {
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
            };
            foreach (var (vx, vy) in pl.Points)
                pg.Points.Add(ToScreen(vx, vy));
            drawingCanvas.Children.Add(pg);
        }

        void AddOpenPolyline(SurveyStationGeometry.PlanVectorPolyline pl, Brush stroke, double thickness)
        {
            if (pl.Points.Count < 2)
                return;
            var poly = new Polyline
            {
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
            };
            foreach (var (vx, vy) in pl.Points)
                poly.Points.Add(ToScreen(vx, vy));
            drawingCanvas.Children.Add(poly);
        }

        foreach (var pl in scene.WallPolylines)
        {
            if (pl.Closed && pl.Points.Count >= 3)
                AddClosedPolygon(pl, sketchFill, sketchStroke, 1.75);
            else if (pl.Points.Count >= 2)
                AddOpenPolyline(pl, sketchStroke, 2.0);
        }

        foreach (var pl in scene.VectorPolylines)
        {
            if (pl.Closed && pl.Points.Count >= 3)
                AddClosedPolygon(pl, vectorFill, vectorBrush, 1.65);
            else if (pl.Points.Count >= 2)
                AddOpenPolyline(pl, vectorBrush, 1.65);
        }

        foreach (var (x1, y1, x2, y2) in scene.TraverseSegments)
        {
            var pa = ToScreen(x1, y1);
            var pb = ToScreen(x2, y2);
            drawingCanvas.Children.Add(new Line
            {
                X1 = pa.X,
                Y1 = pa.Y,
                X2 = pb.X,
                Y2 = pb.Y,
                Stroke = traverseColor,
                StrokeThickness = highContrast ? 4 : 3,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }

        foreach (var c in scene.Stations.Values)
        {
            var pt = ToScreen(c.X, c.Y);
            var el = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = stationFill,
                Stroke = stationRing,
                StrokeThickness = 1.5,
            };
            Canvas.SetLeft(el, pt.X - 4);
            Canvas.SetTop(el, pt.Y - 4);
            drawingCanvas.Children.Add(el);
        }

        foreach (var sym in scene.Symbols)
        {
            var pt = ToScreen(sym.X, sym.Y);
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = symbolBrush,
                Stroke = symbolRing,
                StrokeThickness = 1.25,
                Opacity = 0.98,
            };
            Canvas.SetLeft(dot, pt.X - 4.5);
            Canvas.SetTop(dot, pt.Y - 4.5);
            drawingCanvas.Children.Add(dot);
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
                    Foreground = legend,
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
                drawingCanvas.Children.Add(lab);
            }
        }

        var legCount = scene.TraverseSegments.Count;
        var underNote = rasterUnderlay != null ? " · raster map underlay (not georeferenced)" : "";
        var spanNote = $" · extent ≈ {scene.SpanX:0.#} × {scene.SpanY:0.#} m (survey frame)";
        var tb = new TextBlock
        {
            Text =
                $"{scene.Stations.Count} stations · {legCount} traverse leg(s) · {scene.WallPolylines.Count} wall stroke(s) · {scene.VectorPolylines.Count} vector overlay(s) · {scene.Symbols.Count} symbol(s){underNote}{spanNote}",
            Foreground = legend,
            FontSize = 11,
            MaxWidth = Math.Max(200, canvasWidth - 20),
            TextWrapping = TextWrapping.Wrap,
        };
        Canvas.SetLeft(tb, 8);
        Canvas.SetTop(tb, canvasHeight - 100);
        drawingCanvas.Children.Add(tb);

        if (opt.ShowCartographyOverlay)
            AddCartographicOverlays(drawingCanvas, scene, ToScreen, pxPerMetre, canvasWidth, canvasHeight, legend, highContrast, opt.CanvasKind);
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
        SurveyCanvasKind canvasKind)
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
        canvas.Children.Add(scaleChip);

        var cx = scene.MinX + scene.SpanX * 0.5f;
        var cy = scene.MinY + scene.SpanY * 0.5f;
        var p0 = toScreen(cx, cy);
        var dySurvey = Math.Max(1e-4f, scene.SpanY * 0.05f);
        var dxSurvey = Math.Max(1e-4f, scene.SpanX * 0.05f);
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
            canvas.Children.Add(shaft);
            canvas.Children.Add(new Path
            {
                Fill = fg,
                Data = geom,
                SnapsToDevicePixels = true,
            });

            var northLabel = canvasKind == SurveyCanvasKind.Section
                ? "+Y section"
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
            canvas.Children.Add(ntb);

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

        var sub = canvasKind == SurveyCanvasKind.Section ? "+Y = N (section)" : "+Y = N (plan)";
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
        canvas.Children.Add(chip);
    }

    /// <summary>
    /// When there is no <see cref="PlanScene"/> (no traverse/sketches), still show a resolvable map image
    /// so users can preview rasters. Aspect ratio preserved; not georeferenced.
    /// </summary>
    public static void DrawRasterUnderlayOnly(
        Canvas drawingCanvas,
        bool highContrast,
        double canvasWidth,
        double canvasHeight,
        BitmapSource rasterUnderlay,
        string viewNameForLegend)
    {
        drawingCanvas.Children.Clear();
        const double pad = 48;
        drawingCanvas.Width = canvasWidth;
        drawingCanvas.Height = canvasHeight;

        var boxW = Math.Max(1, canvasWidth - 2 * pad);
        var boxH = Math.Max(1, canvasHeight - 2 * pad);

        var img = new Image
        {
            Source = rasterUnderlay,
            Width = boxW,
            Height = boxH,
            Stretch = Stretch.Uniform,
            Opacity = highContrast ? 0.92 : 1.0,
            SnapsToDevicePixels = true,
        };
        Canvas.SetLeft(img, pad);
        Canvas.SetTop(img, pad);
        drawingCanvas.Children.Add(img);

        var hintMuted = SurveyCanvasTheme.IsDark ? Color.FromRgb(0x94, 0xA3, 0xB8) : Color.FromRgb(0x65, 0x67, 0x6B);
        var hintFg = highContrast ? Brushes.Black : new SolidColorBrush(hintMuted);
        var hint = new TextBlock
        {
            Text =
                $"No survey geometry in this project — showing map image only ({viewNameForLegend}). Not georeferenced; add traverse or sketches in CaveAI Pro (Android) to align vectors.",
            Foreground = hintFg,
            FontSize = 12,
            MaxWidth = 640,
            TextWrapping = TextWrapping.Wrap,
        };
        Canvas.SetLeft(hint, 12);
        Canvas.SetTop(hint, 10);
        drawingCanvas.Children.Add(hint);

        var legFg = highContrast ? Brushes.Black : hintFg;
        var tb = new TextBlock
        {
            Text = $"Raster preview · {viewNameForLegend} · no stations or vectors drawn",
            Foreground = legFg,
            FontSize = 13,
            MaxWidth = 560,
            TextWrapping = TextWrapping.Wrap,
        };
        Canvas.SetLeft(tb, 8);
        Canvas.SetTop(tb, canvasHeight - 22);
        drawingCanvas.Children.Add(tb);
    }
}
