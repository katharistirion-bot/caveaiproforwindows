using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services.Visualization;

/// <summary>
/// Original speleological map symbols as WPF <see cref="StreamGeometry"/> — no proprietary icon packs.
/// Coordinates are normalised 0…1; scale at draw time in survey metres or pixels.
/// </summary>
public static class CaveMappingSymbolCatalog
{
    public enum SymbolKind
    {
        RockBlock,
        WaterPool,
        Stalactite,
        Stalagmite,
        Breakdown,
        Entrance,
        StationMarker,
        JunctionMarker,
    }

    public const string IconKeyPrefix = "caveai:";

    public static string IconKey(SymbolKind kind) => IconKeyPrefix + kind.ToString().ToLowerInvariant();

    public static bool TryParseIconKey(string? raw, out SymbolKind kind)
    {
        kind = SymbolKind.RockBlock;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim();
        if (!s.StartsWith(IconKeyPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var tail = s[IconKeyPrefix.Length..];
        return Enum.TryParse(tail, ignoreCase: true, out kind);
    }

    public const double DefaultLegendSpanMetres = 0.85;

    /// <summary>Legend symbol size in DIP from survey-metre span and layout scale.</summary>
    public static double LegendSymbolSizeDip(double pxPerMetre, double spanMetres = DefaultLegendSpanMetres) =>
        Math.Clamp(spanMetres * Math.Max(pxPerMetre, 1.0), 14.0, 28.0);

    public static string GetLabel(SymbolKind kind) =>
        kind switch
        {
            SymbolKind.StationMarker => "Station (fixed point)",
            SymbolKind.JunctionMarker => "Junction (3+ legs)",
            SymbolKind.Entrance => "Entrance (arch)",
            SymbolKind.RockBlock => "Rock block (boulder)",
            SymbolKind.WaterPool => "Water (pool/lake)",
            SymbolKind.Stalactite => "Stalactite (ceiling)",
            SymbolKind.Stalagmite => "Stalagmite (floor)",
            SymbolKind.Breakdown => "Breakdown (rubble)",
            _ => kind.ToString(),
        };

    public static IReadOnlyList<SymbolKind> DefaultLegendKinds { get; } =
    [
        SymbolKind.StationMarker,
        SymbolKind.JunctionMarker,
        SymbolKind.Entrance,
        SymbolKind.WaterPool,
        SymbolKind.Stalactite,
        SymbolKind.Stalagmite,
        SymbolKind.RockBlock,
        SymbolKind.Breakdown,
    ];

    public static StreamGeometry GetGeometry(SymbolKind kind) =>
        kind switch
        {
            SymbolKind.RockBlock => RockBlock(),
            SymbolKind.WaterPool => WaterPool(),
            SymbolKind.Stalactite => Stalactite(),
            SymbolKind.Stalagmite => Stalagmite(),
            SymbolKind.Breakdown => Breakdown(),
            SymbolKind.Entrance => Entrance(),
            SymbolKind.StationMarker => StationMarker(),
            SymbolKind.JunctionMarker => JunctionMarker(),
            _ => RockBlock(),
        };

    public static Path CreatePath(SymbolKind kind, double sizeDip, Brush fill, Brush? stroke = null, double strokeThickness = 1.0)
    {
        var g = GetGeometry(kind);
        g.Freeze();
        return new Path
        {
            Data = g,
            Fill = fill,
            Stroke = stroke ?? Brushes.Transparent,
            StrokeThickness = strokeThickness,
            Width = sizeDip,
            Height = sizeDip,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
        };
    }

    private static StreamGeometry RockBlock()
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(0.15, 0.55), true, true);
        ctx.LineTo(new Point(0.35, 0.20), true, false);
        ctx.LineTo(new Point(0.72, 0.28), true, false);
        ctx.LineTo(new Point(0.88, 0.62), true, false);
        ctx.LineTo(new Point(0.52, 0.85), true, false);
        g.Freeze();
        return g;
    }

    private static StreamGeometry WaterPool()
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(0.12, 0.52), false, false);
        ctx.QuadraticBezierTo(new Point(0.50, 0.18), new Point(0.88, 0.52), true, false);
        ctx.QuadraticBezierTo(new Point(0.50, 0.86), new Point(0.12, 0.52), true, false);
        g.Freeze();
        return g;
    }

    private static StreamGeometry Stalactite()
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(0.50, 0.08), true, true);
        ctx.LineTo(new Point(0.62, 0.42), true, false);
        ctx.LineTo(new Point(0.50, 0.92), true, false);
        ctx.LineTo(new Point(0.38, 0.42), true, false);
        g.Freeze();
        return g;
    }

    private static StreamGeometry Stalagmite()
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(0.50, 0.92), true, true);
        ctx.LineTo(new Point(0.68, 0.38), true, false);
        ctx.LineTo(new Point(0.50, 0.12), true, false);
        ctx.LineTo(new Point(0.32, 0.38), true, false);
        g.Freeze();
        return g;
    }

    private static StreamGeometry Breakdown()
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(0.20, 0.70), true, true);
        ctx.LineTo(new Point(0.42, 0.45), true, false);
        ctx.LineTo(new Point(0.58, 0.72), true, false);
        ctx.LineTo(new Point(0.78, 0.40), true, false);
        ctx.LineTo(new Point(0.65, 0.82), true, false);
        ctx.LineTo(new Point(0.35, 0.88), true, false);
        g.Freeze();
        return g;
    }

    private static StreamGeometry Entrance()
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(0.18, 0.78), true, true);
        ctx.ArcTo(new Point(0.82, 0.78), new Size(0.32, 0.32), 0, false, SweepDirection.Clockwise, true, false);
        ctx.LineTo(new Point(0.82, 0.92), true, false);
        ctx.LineTo(new Point(0.18, 0.92), true, false);
        g.Freeze();
        return g;
    }

    private static StreamGeometry StationMarker()
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(0.50, 0.14), true, true);
        ctx.ArcTo(new Point(0.50, 0.86), new Size(0.36, 0.36), 0, false, SweepDirection.Clockwise, true, false);
        ctx.ArcTo(new Point(0.50, 0.14), new Size(0.36, 0.36), 0, false, SweepDirection.Clockwise, true, false);
        ctx.BeginFigure(new Point(0.50, 0.38), true, true);
        ctx.ArcTo(new Point(0.50, 0.62), new Size(0.10, 0.10), 0, false, SweepDirection.Clockwise, true, false);
        ctx.ArcTo(new Point(0.50, 0.38), new Size(0.10, 0.10), 0, false, SweepDirection.Clockwise, true, false);
        g.Freeze();
        return g;
    }

    private static StreamGeometry JunctionMarker()
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(0.50, 0.12), true, true);
        ctx.LineTo(new Point(0.82, 0.78), true, false);
        ctx.LineTo(new Point(0.18, 0.78), true, false);
        ctx.BeginFigure(new Point(0.50, 0.28), true, true);
        ctx.ArcTo(new Point(0.50, 0.52), new Size(0.12, 0.12), 0, false, SweepDirection.Clockwise, true, false);
        ctx.ArcTo(new Point(0.50, 0.28), new Size(0.12, 0.12), 0, false, SweepDirection.Clockwise, true, false);
        g.Freeze();
        return g;
    }
}
