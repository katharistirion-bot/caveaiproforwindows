using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Builds WPF visuals for <see cref="SurveyStationGeometry.PlanMapSymbol"/> using the same survey layout as vector rendering.
/// Symbol diameter follows survey metres × <see cref="PlanCanvasSurveyLayout.PxPerMetre"/> so zoom/pan on the map surface
/// preserves the same world footprint as Android sketch stamps (same transform group as traverse vectors).
/// </summary>
public static class AndroidMapSymbolVisualFactory
{
    /// <summary>Creates a positioned symbol (Path in Viewbox, or emoji TextBlock) at survey coordinates.</summary>
    public static UIElement CreateVisual(
        SurveyStationGeometry.PlanMapSymbol sym,
        PlanCanvasSurveyLayout layout,
        bool highContrast)
    {
        var anchor = layout.WorldToCanvas(sym.X, sym.Y);
        var spanWorldM = sym.ScaleSurveyMetres ??
                         SketchSymbolDefinitions.DefaultSymbolWorldSpanMetres * Math.Max(0.12, sym.Scale);
        spanWorldM = Math.Clamp(spanWorldM, 0.08, 80.0);
        var basePx = Math.Clamp(spanWorldM * layout.PxPerMetre, 8.0, 560.0);
        var resolved = MapSymbolIconResolver.Resolve(sym, highContrast);

        if (resolved.Mode == MapSymbolIconResolver.RenderMode.EmojiGlyph && !string.IsNullOrEmpty(resolved.Emoji))
        {
            var tb = new TextBlock
            {
                Text = resolved.Emoji,
                FontSize = Math.Clamp(basePx * 0.78, 10, 420),
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
                ToolTip = BuildTooltip(sym, resolved.DisplayLabel),
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var w = Math.Max(basePx, tb.DesiredSize.Width);
            var h = Math.Max(basePx, tb.DesiredSize.Height);
            tb.Width = w;
            tb.Height = h;
            tb.RenderTransformOrigin = new Point(0.5, 0.5);
            tb.RenderTransform = new RotateTransform(sym.RotationDegrees);
            Canvas.SetLeft(tb, anchor.X - w * 0.5);
            Canvas.SetTop(tb, anchor.Y - h * 0.5);
            return tb;
        }

        var path = new Path
        {
            Data = resolved.Geometry ?? Geometry.Empty,
            Stroke = resolved.Stroke,
            Fill = resolved.Fill,
            StrokeThickness = Math.Max(0.65, basePx / NominalViewboxSize(resolved) * 1.05),
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        var vb = new Viewbox
        {
            Width = basePx,
            Height = basePx,
            Stretch = Stretch.Uniform,
            Child = path,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(sym.RotationDegrees),
            ToolTip = BuildTooltip(sym, resolved.DisplayLabel),
        };
        Canvas.SetLeft(vb, anchor.X - basePx * 0.5);
        Canvas.SetTop(vb, anchor.Y - basePx * 0.5);
        return vb;
    }

    private static double NominalViewboxSize(MapSymbolIconResolver.ResolvedMapSymbol resolved) =>
        resolved.NormalizedUisSpace ? 2.0 : SketchSymbolDefinitions.ViewboxNominalSize;

    private static string BuildTooltip(SurveyStationGeometry.PlanMapSymbol sym, string displayLabel)
    {
        var parts = new List<string> { displayLabel };
        if (!string.IsNullOrWhiteSpace(sym.SymbolId))
            parts.Add($"id:{sym.SymbolId.Trim()}");
        if (!string.IsNullOrWhiteSpace(sym.IconKey) &&
            !string.Equals(sym.IconKey, displayLabel, StringComparison.OrdinalIgnoreCase))
            parts.Add(sym.IconKey.Trim());
        if (sym.ScaleSurveyMetres is { } sm)
            parts.Add($"span {sm:0.##} m");
        else
            parts.Add($"scale ×{sym.Scale:0.##}");
        if (System.Math.Abs(sym.Z) > 1e-4f)
            parts.Add($"Z {sym.Z:0.##} m");
        if (System.Math.Abs(sym.RotationDegrees) > 0.01f)
            parts.Add($"rotation {sym.RotationDegrees:0.#}°");
        parts.Add($"survey ({sym.X:0.##}, {sym.Y:0.##}) m");
        return string.Join(" · ", parts);
    }
}
