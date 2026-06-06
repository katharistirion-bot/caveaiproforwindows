using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>Print-quality linear scale bar and survey compass rose for map frames in <see cref="PrintLayoutService"/>.</summary>
public static class PrintCartographyOverlayBuilder
{
    public static void AddToMapFrame(
        Grid mapHost,
        double mapDrawWidth,
        double mapDrawHeight,
        double effectivePxPerMetre,
        SurveyCanvasKind canvasKind,
        bool highContrast)
    {
        if (mapDrawWidth <= 0 || mapDrawHeight <= 0 || effectivePxPerMetre <= 1e-9)
            return;

        var uiScale = Math.Clamp(Math.Min(mapDrawWidth, mapDrawHeight) / 420.0, 0.72, 1.35);
        var fg = Brushes.Black;
        var chipBg = highContrast
            ? new SolidColorBrush(Color.FromArgb(245, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
        var chipBorder = new SolidColorBrush(Color.FromRgb(160, 166, 174));

        var scaleChip = BuildScaleBarChip(effectivePxPerMetre, fg, chipBg, chipBorder, uiScale);
        scaleChip.HorizontalAlignment = HorizontalAlignment.Left;
        scaleChip.VerticalAlignment = VerticalAlignment.Bottom;
        scaleChip.Margin = new Thickness(10 * uiScale, 0, 0, 10 * uiScale);
        Panel.SetZIndex(scaleChip, 4);
        mapHost.Children.Add(scaleChip);

        var compassChip = BuildCompassRoseChip(canvasKind, fg, chipBg, chipBorder, uiScale);
        compassChip.HorizontalAlignment = HorizontalAlignment.Right;
        compassChip.VerticalAlignment = VerticalAlignment.Bottom;
        compassChip.Margin = new Thickness(0, 0, 10 * uiScale, 10 * uiScale);
        Panel.SetZIndex(compassChip, 4);
        mapHost.Children.Add(compassChip);
    }

    private static Border BuildScaleBarChip(
        double pxPerMetre,
        Brush fg,
        Brush chipBg,
        Brush chipBorder,
        double uiScale)
    {
        const double targetPx = 120;
        var barM = NiceScaleBarMetres(targetPx / pxPerMetre);
        var barPx = barM * pxPerMetre * uiScale;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Border
        {
            Width = barPx,
            Height = 4 * uiScale,
            Background = fg,
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8 * uiScale, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(CultureInfo.InvariantCulture, "0 — {0:0.##} m", barM),
            Foreground = fg,
            FontSize = 11 * uiScale,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Border
        {
            Background = chipBg,
            BorderBrush = chipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6 * uiScale),
            Padding = new Thickness(10 * uiScale, 6 * uiScale, 12 * uiScale, 6 * uiScale),
            Child = panel,
            SnapsToDevicePixels = true,
        };
    }

    private static Border BuildCompassRoseChip(
        SurveyCanvasKind canvasKind,
        Brush fg,
        Brush chipBg,
        Brush chipBorder,
        double uiScale)
    {
        var box = 96 * uiScale;
        var rcx = box * 0.5;
        var rcy = box * 0.5;
        var rOuter = 28 * uiScale;
        var rInner = 21 * uiScale;
        var rLabel = 33 * uiScale;

        // Plan / section survey frame: +Y = N (screen up).
        const double nX = 0;
        const double nY = -1;
        const double eX = 1;
        const double eY = 0;
        var sX = -nX;
        var sY = -nY;
        var wX = -eX;
        var wY = -eY;

        var inner = new Canvas { Width = box, Height = box };
        inner.Children.Add(new Ellipse
        {
            Width = rOuter * 2,
            Height = rOuter * 2,
            Stroke = fg,
            StrokeThickness = 1.5 * uiScale,
            Fill = Brushes.Transparent,
            SnapsToDevicePixels = true,
        });
        Canvas.SetLeft(inner.Children[0], rcx - rOuter);
        Canvas.SetTop(inner.Children[0], rcy - rOuter);

        for (var step = 0; step < 8; step++)
        {
            var deg = step * 45.0 * (Math.PI / 180.0);
            var ux = Math.Sin(deg) * eX + Math.Cos(deg) * nX;
            var uy = Math.Sin(deg) * eY + Math.Cos(deg) * nY;
            var ulen = Math.Sqrt(ux * ux + uy * uy);
            if (ulen < 1e-9)
                continue;
            ux /= ulen;
            uy /= ulen;
            inner.Children.Add(new Line
            {
                X1 = rcx + ux * rInner,
                Y1 = rcy + uy * rInner,
                X2 = rcx + ux * rOuter,
                Y2 = rcy + uy * rOuter,
                Stroke = fg,
                StrokeThickness = (step % 2 == 0 ? 1.25 : 0.75) * uiScale,
                Opacity = step % 2 == 0 ? 1.0 : 0.55,
                SnapsToDevicePixels = true,
            });
        }

        AddCompassLetter(inner, "N", fg, 11.5 * uiScale, rcx + nX * rLabel, rcy + nY * rLabel);
        AddCompassLetter(inner, "E", fg, 11.5 * uiScale, rcx + eX * rLabel, rcy + eY * rLabel);
        AddCompassLetter(inner, "S", fg, 11.5 * uiScale, rcx + sX * rLabel, rcy + sY * rLabel);
        AddCompassLetter(inner, "W", fg, 11.5 * uiScale, rcx + wX * rLabel, rcy + wY * rLabel);

        var sub = canvasKind == SurveyCanvasKind.Section
            ? "Chainage · Z"
            : "+Y = N";
        inner.Children.Add(new TextBlock
        {
            Text = sub,
            Foreground = fg,
            FontSize = 8.5 * uiScale,
            Opacity = 0.88,
            TextAlignment = TextAlignment.Center,
            Width = box,
        });
        Canvas.SetLeft(inner.Children[^1], 0);
        Canvas.SetTop(inner.Children[^1], box - 16 * uiScale);

        return new Border
        {
            Background = chipBg,
            BorderBrush = chipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8 * uiScale),
            Padding = new Thickness(8 * uiScale),
            Child = inner,
            SnapsToDevicePixels = true,
        };
    }

    private static void AddCompassLetter(Canvas canvas, string letter, Brush fg, double fontSize, double x, double y)
    {
        var tb = new TextBlock
        {
            Text = letter,
            Foreground = fg,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, x - tb.DesiredSize.Width * 0.5);
        Canvas.SetTop(tb, y - tb.DesiredSize.Height * 0.5);
        canvas.Children.Add(tb);
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
}
