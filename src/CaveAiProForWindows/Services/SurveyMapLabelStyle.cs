using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>Cartographic label chips for survey map overlays (parchment tone, survey typography).</summary>
public static class SurveyMapLabelStyle
{
    private static readonly FontFamily CartoFont = new("Georgia, Palatino Linotype, Times New Roman, serif");
    private static readonly FontFamily DataFont = new("Consolas, Courier New, monospace");

    public static SurveyLabelPalette Palette(bool darkCanvas, bool highContrast) =>
        highContrast
            ? new SurveyLabelPalette(
                ChipFill: Color.FromArgb(230, 20, 20, 24),
                ChipBorder: Color.FromArgb(255, 240, 240, 240),
                Primary: Colors.White,
                Muted: Color.FromRgb(200, 200, 200),
                Accent: Color.FromRgb(120, 220, 210),
                Leader: Color.FromArgb(180, 240, 240, 240))
            : darkCanvas
                ? new SurveyLabelPalette(
                    ChipFill: Color.FromArgb(215, 28, 32, 38),
                    ChipBorder: Color.FromArgb(200, 80, 100, 110),
                    Primary: Color.FromRgb(236, 252, 250),
                    Muted: Color.FromRgb(148, 163, 184),
                    Accent: Color.FromRgb(45, 212, 191),
                    Leader: Color.FromArgb(140, 45, 212, 191))
                : new SurveyLabelPalette(
                    ChipFill: Color.FromArgb(235, 252, 248, 240),
                    ChipBorder: Color.FromArgb(210, 180, 140, 100),
                    Primary: Color.FromRgb(28, 52, 48),
                    Muted: Color.FromRgb(92, 108, 104),
                    Accent: Color.FromRgb(13, 148, 136),
                    Leader: Color.FromArgb(120, 13, 148, 136));

    public static UIElement LegChip(SurveyLegMapLabel leg, Point anchor, bool darkCanvas, bool highContrast)
    {
        var pal = Palette(darkCanvas, highContrast);
        var stack = new StackPanel { Orientation = Orientation.Vertical };

        stack.Children.Add(MakeLine(leg.PrimaryLine, 11.5, FontWeights.SemiBold, pal.Primary, CartoFont));
        if (!string.IsNullOrEmpty(leg.SecondaryLine))
            stack.Children.Add(MakeLine(leg.SecondaryLine, 9.25, FontWeights.Normal, pal.Muted, DataFont, topMargin: 1));
        if (!string.IsNullOrEmpty(leg.LrudLine))
            stack.Children.Add(MakeLine(leg.LrudLine, 8.5, FontWeights.Normal, pal.Accent, DataFont, topMargin: 2));

        return WrapChip(stack, pal, anchor, legAnchor: true);
    }

    public static UIElement EnvChip(IReadOnlyList<string> lines, Point anchor, bool darkCanvas, bool highContrast)
    {
        var pal = Palette(darkCanvas, highContrast);
        var row = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 168 };

        foreach (var line in lines)
        {
            foreach (var token in line.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb(48, pal.Accent.R, pal.Accent.G, pal.Accent.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, pal.ChipBorder.R, pal.ChipBorder.G, pal.ChipBorder.B)),
                    BorderThickness = new Thickness(0.8),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 3),
                    Child = MakeLine(token, 8.5, FontWeights.Normal, pal.Primary, DataFont),
                };
                row.Children.Add(pill);
            }
        }

        return WrapChip(row, pal, anchor, legAnchor: false);
    }

    public static UIElement DepthSpanChip(string label, Point mid, bool darkCanvas, bool highContrast)
    {
        var pal = Palette(darkCanvas, highContrast);
        var tb = MakeLine(label, 10, FontWeights.SemiBold, pal.Accent, CartoFont);
        return WrapChip(tb, pal, mid, legAnchor: false, accentBorder: true);
    }

    public static UIElement BracketCallout(string text, Point pin, bool darkCanvas, bool highContrast)
    {
        var pal = Palette(darkCanvas, highContrast);
        var group = new Canvas();

        var dot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(pal.Accent),
            Stroke = new SolidColorBrush(pal.ChipBorder),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(dot, pin.X - 3);
        Canvas.SetTop(dot, pin.Y - 3);
        group.Children.Add(dot);

        var chip = WrapChip(
            MakeLine(text, 9.25, FontWeights.Normal, pal.Primary, CartoFont, maxWidth: 150, wrap: true),
            pal,
            new Point(pin.X + 10, pin.Y - 14),
            legAnchor: false);

        if (chip is FrameworkElement fe)
        {
            fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var chipW = fe.DesiredSize.Width;
            var chipH = fe.DesiredSize.Height;
            Canvas.SetLeft(fe, pin.X + 8);
            Canvas.SetTop(fe, pin.Y - chipH * 0.5 - 2);
            group.Children.Add(fe);

            group.Children.Add(new Line
            {
                X1 = pin.X + 3,
                Y1 = pin.Y,
                X2 = pin.X + 8,
                Y2 = pin.Y - chipH * 0.25,
                Stroke = new SolidColorBrush(pal.Leader),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
            });
        }

        return group;
    }

    public static UIElement StationNameChip(string name, Point anchor, bool darkCanvas, bool highContrast)
    {
        var pal = Palette(darkCanvas, highContrast);
        var tb = MakeLine(name, 10.5, FontWeights.SemiBold, pal.Primary, CartoFont);
        return WrapChip(tb, pal, anchor, legAnchor: false, compact: true);
    }

    public static UIElement StationZChip(string zText, Point anchor, bool darkCanvas, bool highContrast)
    {
        var pal = Palette(darkCanvas, highContrast);
        var tb = MakeLine(zText, 9, FontWeights.Normal, pal.Muted, DataFont);
        return WrapChip(tb, pal, anchor, legAnchor: false, compact: true, subtle: true);
    }

    /// <summary>Map title block — cave name + optional subtitle (view mode, date).</summary>
    public static Border BuildCaveTitlePlate(
        string caveName,
        string? subtitle,
        bool darkCanvas,
        bool highContrast,
        double maxWidth = 400)
    {
        var pal = Palette(darkCanvas, highContrast);
        var stack = new StackPanel { Orientation = Orientation.Vertical };

        stack.Children.Add(MakeLine(
            caveName,
            16,
            FontWeights.Bold,
            pal.Primary,
            CartoFont,
            maxWidth: maxWidth - 28,
            wrap: true));

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Children.Add(MakeLine(
                subtitle,
                10,
                FontWeights.Normal,
                pal.Accent,
                DataFont,
                topMargin: 3,
                maxWidth: maxWidth - 28,
                wrap: true));
        }

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(pal.ChipFill),
            BorderBrush = new SolidColorBrush(pal.Accent),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(12, 9, 14, 10),
            MaxWidth = maxWidth,
            Child = stack,
            SnapsToDevicePixels = true,
        };
    }

    public static Line LeaderLine(Point from, Point to, SurveyLabelPalette pal) =>
        new()
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            Stroke = new SolidColorBrush(pal.Leader),
            StrokeThickness = 0.9,
            StrokeDashArray = new DoubleCollection { 1.5, 2.5 },
        };

    private static UIElement WrapChip(
        UIElement content,
        SurveyLabelPalette pal,
        Point anchor,
        bool legAnchor,
        bool accentBorder = false,
        bool compact = false,
        bool subtle = false)
    {
        var pad = compact ? new Thickness(5, 2, 5, 3) : new Thickness(7, 4, 7, 5);
        var border = new Border
        {
            CornerRadius = new CornerRadius(compact ? 3 : 5),
            Background = new SolidColorBrush(subtle ? Color.FromArgb(200, pal.ChipFill.R, pal.ChipFill.G, pal.ChipFill.B) : pal.ChipFill),
            BorderBrush = new SolidColorBrush(accentBorder ? pal.Accent : pal.ChipBorder),
            BorderThickness = new Thickness(accentBorder ? 1.2 : 0.9),
            Padding = pad,
            Child = content,
            SnapsToDevicePixels = true,
        };

        border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = border.DesiredSize.Width;
        var h = border.DesiredSize.Height;

        var group = new Canvas();
        if (legAnchor)
        {
            var chipX = anchor.X - w * 0.5;
            var chipY = anchor.Y - h - 6;
            group.Children.Add(LeaderLine(new Point(anchor.X, anchor.Y), new Point(anchor.X, chipY + h + 1), pal));
            Canvas.SetLeft(border, chipX);
            Canvas.SetTop(border, chipY);
        }
        else
        {
            Canvas.SetLeft(border, anchor.X);
            Canvas.SetTop(border, anchor.Y);
        }

        group.Children.Add(border);
        return group;
    }

    private static TextBlock MakeLine(
        string text,
        double size,
        FontWeight weight,
        Color color,
        FontFamily font,
        double topMargin = 0,
        double maxWidth = 0,
        bool wrap = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = new SolidColorBrush(color),
            FontFamily = font,
            Margin = new Thickness(0, topMargin, 0, 0),
            TextAlignment = TextAlignment.Left,
        };
        if (maxWidth > 0)
            tb.MaxWidth = maxWidth;
        if (wrap)
            tb.TextWrapping = TextWrapping.Wrap;
        return tb;
    }
}

public readonly record struct SurveyLabelPalette(
    Color ChipFill,
    Color ChipBorder,
    Color Primary,
    Color Muted,
    Color Accent,
    Color Leader);
