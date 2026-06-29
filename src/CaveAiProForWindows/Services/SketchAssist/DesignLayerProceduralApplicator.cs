using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Applies or removes procedurally generated ink on the Sketch Editor design layer.</summary>
public static class DesignLayerProceduralApplicator
{
    public static int RemoveProcedural(Canvas designLayer)
    {
        ArgumentNullException.ThrowIfNull(designLayer);
        var removed = 0;
        for (var i = designLayer.Children.Count - 1; i >= 0; i--)
        {
            if (designLayer.Children[i] is not UIElement el)
                continue;
            if (!IsProcedural(el))
                continue;
            designLayer.Children.RemoveAt(i);
            removed++;
        }

        return removed;
    }

    public static int Apply(
        Canvas designLayer,
        PlanCanvasSurveyLayout layout,
        ProceduralSketchResult result)
    {
        ArgumentNullException.ThrowIfNull(designLayer);
        ArgumentNullException.ThrowIfNull(result);
        RemoveProcedural(designLayer);
        var added = 0;

        foreach (var stroke in result.Strokes.Where(s => s.IsDrawable))
        {
            var meta = stroke.Metadata ?? DesignLayerInkMetadata.ForProceduralWall();
            var poly = CreatePolyline(stroke, layout, meta);
            designLayer.Children.Add(poly);
            added++;
        }

        foreach (var stamp in result.SymbolStamps)
        {
            var vb = CreateSymbolStamp(stamp, layout, DesignLayerInkMetadata.ForProceduralSymbol());
            designLayer.Children.Add(vb);
            added++;
        }

        return added;
    }

    public static bool IsProcedural(UIElement element) =>
        element switch
        {
            FrameworkElement { Tag: DesignLayerInkMetadata meta } => string.Equals(
                meta.Source,
                SketchStrokeStyleDefaults.ProceduralSource,
                StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    public static Polyline CreatePolyline(
        SketchStrokeModel stroke,
        PlanCanvasSurveyLayout layout,
        DesignLayerInkMetadata meta)
    {
        var poly = new Polyline();
        SketchWallInkStyle.ApplyToPolyline(poly, meta);

        foreach (var (sx, sy) in stroke.Points)
        {
            var pt = DesignLayerSurveyConverter.SurveyPointToCanvas(sx, sy, layout);
            poly.Points.Add(pt);
        }

        return poly;
    }

    internal static Viewbox CreateSymbolStamp(
        SketchSymbolStampModel stamp,
        PlanCanvasSurveyLayout layout,
        DesignLayerInkMetadata meta)
    {
        var ink = SketchSymbolDefinitions.Get(stamp.Kind);
        var size = SketchSymbolDefinitions.StampDisplaySize;
        var center = DesignLayerSurveyConverter.SurveyPointToCanvas(stamp.SurveyX, stamp.SurveyY, layout);
        var vb = new Viewbox
        {
            Width = size,
            Height = size,
            Tag = meta,
            Child = new Path
            {
                Data = ink.Geometry,
                Stroke = ink.Stroke,
                Fill = ink.Fill,
                StrokeThickness = 1,
            },
        };
        Canvas.SetLeft(vb, center.X - size * 0.5);
        Canvas.SetTop(vb, center.Y - size * 0.5);
        if (string.Equals(meta.Source, SketchStrokeStyleDefaults.ProceduralSource, StringComparison.OrdinalIgnoreCase))
            vb.Opacity = 0.75;
        return vb;
    }
}
