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
    /// <summary>Creates a positioned symbol (Path in Viewbox) at survey coordinates.</summary>
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
        var (kind, ink) = AndroidSymbolIdGeometryCatalog.ResolveInk(sym.SymbolId, sym.Label, sym.IconKey);

        var path = new Path
        {
            Data = ink.Geometry,
            Stroke = highContrast ? Brushes.White : ink.Stroke,
            Fill = highContrast ? Brushes.White : ink.Fill,
            StrokeThickness = Math.Max(0.65, basePx / SketchSymbolDefinitions.ViewboxNominalSize * 1.05),
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
            ToolTip = BuildTooltip(sym, kind),
        };
        Canvas.SetLeft(vb, anchor.X - basePx * 0.5);
        Canvas.SetTop(vb, anchor.Y - basePx * 0.5);
        return vb;
    }

    private static string BuildTooltip(SurveyStationGeometry.PlanMapSymbol sym, SketchEditorSymbolKind kind)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sym.SymbolId))
            parts.Add($"id:{sym.SymbolId.Trim()}");
        if (!string.IsNullOrWhiteSpace(sym.Label))
            parts.Add(sym.Label.Trim());
        if (!string.IsNullOrWhiteSpace(sym.IconKey) &&
            !string.Equals(sym.IconKey, sym.Label, StringComparison.OrdinalIgnoreCase))
            parts.Add(sym.IconKey.Trim());
        parts.Add($"stamp → {kind}");
        if (sym.ScaleSurveyMetres is { } sm)
            parts.Add($"span {sm:0.##} m");
        else
            parts.Add($"scale ×{sym.Scale:0.##} (→ ~{SketchSymbolDefinitions.DefaultSymbolWorldSpanMetres * Math.Max(0.12, sym.Scale):0.##} m nominal)");
        if (System.Math.Abs(sym.Z) > 1e-4f)
            parts.Add($"Z {sym.Z:0.##} m (metadata)");
        parts.Add($"rotation {sym.RotationDegrees:0.#}°");
        parts.Add($"survey ({sym.X:0.##}, {sym.Y:0.##}) m");
        return string.Join(" · ", parts);
    }
}
