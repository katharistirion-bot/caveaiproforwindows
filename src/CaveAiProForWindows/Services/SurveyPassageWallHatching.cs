using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>Diagonal rock hatching clipped to filled LRUD passage polygons.</summary>
public static class SurveyPassageWallHatching
{
    public static Path? TryCreateOverlay(Geometry clipGeometry, bool darkCanvas, bool highContrast, double spacingPx = 7.0)
    {
        if (clipGeometry == null)
            return null;

        var bounds = clipGeometry.Bounds;
        if (bounds.Width < 2 || bounds.Height < 2)
            return null;

        var lineColor = highContrast
            ? Color.FromArgb(0x40, 0x40, 0x40, 0x40)
            : darkCanvas
                ? Color.FromArgb(0x30, 0xC8, 0xB8, 0xA8)
                : Color.FromArgb(0x38, 0x5C, 0x40, 0x33);

        var pen = new Pen(new SolidColorBrush(lineColor), highContrast ? 0.9 : 0.65);
        pen.Freeze();

        var lines = new GeometryGroup();
        var extent = Math.Max(bounds.Width, bounds.Height) * 1.5;
        for (var x = -extent; x < extent; x += spacingPx)
        {
            lines.Children.Add(new LineGeometry(
                new Point(bounds.Left + x, bounds.Top - extent * 0.25),
                new Point(bounds.Left + x + extent, bounds.Top + extent * 0.75)));
        }

        lines.Freeze();
        var drawing = new GeometryDrawing(null, pen, lines);
        var brush = new DrawingBrush(drawing)
        {
            Viewbox = bounds,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = bounds,
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            Stretch = Stretch.None,
        };
        brush.Freeze();

        var path = new Path
        {
            Data = clipGeometry,
            Fill = brush,
            IsHitTestVisible = false,
            Opacity = highContrast ? 0.45 : 0.5,
            SnapsToDevicePixels = false,
        };
        return path;
    }
}
