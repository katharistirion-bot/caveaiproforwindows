using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>Diagonal rock hatching clipped to filled LRUD passage polygons.</summary>
public static class SurveyPassageWallHatching
{
    /// <summary>Therion-style rock hatch angle (degrees, screen space +X toward +Y).</summary>
    public const double HatchAngleDegrees = 45.0;

    /// <summary>Target spacing in survey metres — converted to pixels via <see cref="ComputeSpacingPx"/>.</summary>
    public const double HatchSpacingMetres = 0.32;

    public static double ComputeSpacingPx(double pxPerMetre, bool isPrintPreset)
    {
        var raw = HatchSpacingMetres * Math.Max(pxPerMetre, 0.01);
        return isPrintPreset
            ? Math.Clamp(raw, 4.0, 12.0)
            : Math.Clamp(raw, 5.0, 16.0);
    }

    /// <summary>
    /// Builds parallel diagonal hatch lines in absolute canvas coordinates.
    /// <paramref name="phaseOriginScreen"/> aligns spacing across adjacent LRUD ribbons.
    /// </summary>
    public static GeometryGroup BuildHatchLineGeometry(
        Rect bounds,
        double spacingPx,
        Point phaseOriginScreen,
        double angleDegrees = HatchAngleDegrees)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        if (spacingPx < 0.5 || bounds.Width < 1 || bounds.Height < 1)
            return group;

        var angleRad = angleDegrees * Math.PI / 180.0;
        var dirX = Math.Cos(angleRad);
        var dirY = Math.Sin(angleRad);
        var normX = -dirY;
        var normY = dirX;

        var globalPhase = phaseOriginScreen.X * normX + phaseOriginScreen.Y * normY;

        static double Project(double x, double y, double nx, double ny) => x * nx + y * ny;

        var minN = Math.Min(
            Math.Min(Project(bounds.Left, bounds.Top, normX, normY), Project(bounds.Right, bounds.Top, normX, normY)),
            Math.Min(Project(bounds.Left, bounds.Bottom, normX, normY), Project(bounds.Right, bounds.Bottom, normX, normY)));
        var maxN = Math.Max(
            Math.Max(Project(bounds.Left, bounds.Top, normX, normY), Project(bounds.Right, bounds.Top, normX, normY)),
            Math.Max(Project(bounds.Left, bounds.Bottom, normX, normY), Project(bounds.Right, bounds.Bottom, normX, normY)));

        var extent = Math.Max(bounds.Width, bounds.Height) * 2.5;
        var kStart = (int)Math.Floor((minN - globalPhase) / spacingPx) - 1;
        var kEnd = (int)Math.Ceiling((maxN - globalPhase) / spacingPx) + 1;

        for (var k = kStart; k <= kEnd; k++)
        {
            var off = globalPhase + k * spacingPx;
            var cx = bounds.X + bounds.Width * 0.5;
            var cy = bounds.Y + bounds.Height * 0.5;
            var centerProj = Project(cx, cy, normX, normY);
            var anchorX = cx + (off - centerProj) * normX;
            var anchorY = cy + (off - centerProj) * normY;
            group.Children.Add(new LineGeometry(
                new Point(anchorX - dirX * extent, anchorY - dirY * extent),
                new Point(anchorX + dirX * extent, anchorY + dirY * extent)));
        }

        return group;
    }

    public static Path? TryCreateOverlay(
        Geometry clipGeometry,
        bool darkCanvas,
        bool highContrast,
        double spacingPx,
        Point phaseOriginScreen,
        bool isPrintPreset)
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
                : isPrintPreset
                    ? Color.FromArgb(0x44, 0x48, 0x38, 0x2A)
                    : Color.FromArgb(0x38, 0x5C, 0x40, 0x33);

        var penWidth = isPrintPreset ? 0.55 : highContrast ? 0.9 : 0.65;
        var pen = new Pen(new SolidColorBrush(lineColor), penWidth);
        pen.Freeze();

        var lines = BuildHatchLineGeometry(bounds, spacingPx, phaseOriginScreen);
        if (lines.Children.Count == 0)
            return null;

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

        return new Path
        {
            Data = clipGeometry,
            Fill = brush,
            IsHitTestVisible = false,
            Opacity = isPrintPreset ? 0.52 : highContrast ? 0.45 : 0.5,
            SnapsToDevicePixels = isPrintPreset,
        };
    }
}
