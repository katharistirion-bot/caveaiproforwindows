using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>Deep-clones user ink on the sketch design layer for undo/redo restore.</summary>
public static class DesignLayerElementCloner
{
    public static UIElement Clone(UIElement source) =>
        source switch
        {
            Polyline poly => ClonePolyline(poly),
            Viewbox vb => CloneViewbox(vb),
            _ => throw new NotSupportedException($"Cannot clone design-layer element type {source.GetType().Name}."),
        };

    private static Polyline ClonePolyline(Polyline source)
    {
        var copy = new Polyline
        {
            Stroke = source.Stroke,
            StrokeThickness = source.StrokeThickness,
            StrokeLineJoin = source.StrokeLineJoin,
            StrokeStartLineCap = source.StrokeStartLineCap,
            StrokeEndLineCap = source.StrokeEndLineCap,
            Fill = source.Fill,
            Opacity = source.Opacity,
        };
        foreach (var pt in source.Points)
            copy.Points.Add(pt);
        return copy;
    }

    private static Viewbox CloneViewbox(Viewbox source)
    {
        if (source.Child is not Path path || path.Data == null)
            throw new NotSupportedException("Symbol stamp Viewbox must contain a Path.");

        var clonePath = new Path
        {
            Data = path.Data.Clone(),
            Stroke = path.Stroke,
            Fill = path.Fill,
            StrokeThickness = path.StrokeThickness,
            StrokeLineJoin = path.StrokeLineJoin,
            StrokeStartLineCap = path.StrokeStartLineCap,
            StrokeEndLineCap = path.StrokeEndLineCap,
            IsHitTestVisible = false,
        };
        var vb = new Viewbox
        {
            Width = source.Width > 0 ? source.Width : SketchSymbolDefinitions.StampDisplaySize,
            Height = source.Height > 0 ? source.Height : SketchSymbolDefinitions.StampDisplaySize,
            Stretch = Stretch.Uniform,
            Child = clonePath,
        };
        Canvas.SetLeft(vb, Canvas.GetLeft(source));
        Canvas.SetTop(vb, Canvas.GetTop(source));
        return vb;
    }
}
