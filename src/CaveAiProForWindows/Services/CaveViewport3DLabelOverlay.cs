using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

public readonly record struct Viewport3DLabelHitTarget(Rect Bounds, Viewport3DLabelEntry Entry);

/// <summary>Screen-space cartographic chips projected from 3D survey coordinates.</summary>
public static class CaveViewport3DLabelOverlay
{
    public static void Sync(
        Viewport3D viewport,
        Canvas? canvas,
        IReadOnlyList<Viewport3DLabelEntry>? labels,
        double labelScale = 1.0,
        Action<Viewport3DLabelEntry>? onLabelClick = null,
        IList<Viewport3DLabelHitTarget>? hitTargets = null)
    {
        if (canvas == null)
            return;

        canvas.Children.Clear();
        hitTargets?.Clear();
        if (labels == null || labels.Count == 0 || viewport.Camera == null)
            return;

        labelScale = Math.Clamp(labelScale, 0.65, 1.5);
        var pal = SurveyMapLabelStyle.Palette(darkCanvas: true, highContrast: false);
        var ordered = labels
            .OrderBy(l => l.Kind switch
            {
                Viewport3DLabelKind.Station => 0,
                Viewport3DLabelKind.Symbol => 1,
                Viewport3DLabelKind.FieldCatalog => 2,
                Viewport3DLabelKind.StationSnapshot => 3,
                Viewport3DLabelKind.AiTag => 4,
                Viewport3DLabelKind.DepthSpan => 5,
                Viewport3DLabelKind.Bracket => 6,
                Viewport3DLabelKind.Leg => 7,
                Viewport3DLabelKind.Environment => 8,
                _ => 9,
            })
            .ToList();

        var legCount = ordered.Count(l => l.Kind == Viewport3DLabelKind.Leg);
        var legStride = legCount switch
        {
            > 100 => 8,
            > 80 => 6,
            > 50 => 4,
            > 30 => 3,
            > 15 => 2,
            _ => 1,
        };

        var stationCount = ordered.Count(l => l.Kind == Viewport3DLabelKind.Station);
        var stationStride = stationCount switch
        {
            > 80 => 4,
            > 50 => 3,
            > 30 => 2,
            _ => 1,
        };

        var legIndex = 0;
        var stationIndex = 0;
        var boxes = new List<Rect>();

        foreach (var entry in ordered)
        {
            if (entry.Kind == Viewport3DLabelKind.Leg)
            {
                var idx = legIndex++;
                if (legStride > 1 && idx % legStride != 0 && idx != 0)
                    continue;
            }

            if (entry.Kind == Viewport3DLabelKind.Station)
            {
                var idx = stationIndex++;
                if (stationStride > 1 && idx % stationStride != 0 && idx != 0)
                    continue;
            }

            if (entry.Kind is Viewport3DLabelKind.Environment && ordered.Count > 35)
                continue;

            if (!Viewport3DProjection.TryWorldToScreen(viewport, entry.World, out var anchor))
                continue;

            var chip = BuildChip(entry, labelScale, onLabelClick);
            chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var cw = chip.DesiredSize.Width;
            var ch = chip.DesiredSize.Height;

            var left = anchor.X - cw * 0.5;
            var top = anchor.Y - ch * 0.5;
            var rect = new Rect(left, top, cw, ch);

            if (entry.Kind is Viewport3DLabelKind.Leg or Viewport3DLabelKind.Environment or Viewport3DLabelKind.Station
                or Viewport3DLabelKind.Symbol or Viewport3DLabelKind.FieldCatalog or Viewport3DLabelKind.StationSnapshot
                or Viewport3DLabelKind.AiTag)
            {
                for (var attempt = 0; attempt < 6 && boxes.Any(b => b.IntersectsWith(rect)); attempt++)
                {
                    top -= ch * 0.55;
                    left += (attempt % 2 == 0 ? 1 : -1) * cw * 0.35;
                    rect = new Rect(left, top, cw, ch);
                }
            }

            if (boxes.Any(b => b.IntersectsWith(rect)) && entry.Kind == Viewport3DLabelKind.Leg)
                continue;

            var chipCenterX = left + cw * 0.5;
            var chipCenterY = top + ch * 0.5;
            if (Math.Abs(chipCenterX - anchor.X) > 4 || Math.Abs(chipCenterY - anchor.Y) > 4)
            {
                canvas.Children.Add(new Line
                {
                    X1 = anchor.X,
                    Y1 = anchor.Y,
                    X2 = chipCenterX,
                    Y2 = chipCenterY,
                    Stroke = new SolidColorBrush(pal.Leader),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    IsHitTestVisible = false,
                });
            }

            Canvas.SetLeft(chip, left);
            Canvas.SetTop(chip, top);
            canvas.Children.Add(chip);
            boxes.Add(rect);

            if (onLabelClick != null && IsClickableKind(entry.Kind) && hitTargets != null)
                hitTargets.Add(new Viewport3DLabelHitTarget(rect, entry));
        }
    }

    private static Border BuildChip(
        Viewport3DLabelEntry entry,
        double labelScale,
        Action<Viewport3DLabelEntry>? onLabelClick)
    {
        var pal = SurveyMapLabelStyle.Palette(darkCanvas: true, highContrast: false);
        var stack = new StackPanel { Orientation = Orientation.Vertical };

        if (entry.Kind == Viewport3DLabelKind.Symbol && entry.MapSymbol is { } sym)
        {
            var glyphRow = new StackPanel { Orientation = Orientation.Horizontal };
            var resolved = MapSymbolIconResolver.Resolve(sym, highContrast: false);
            if (resolved.Mode == MapSymbolIconResolver.RenderMode.EmojiGlyph && !string.IsNullOrEmpty(resolved.Emoji))
            {
                glyphRow.Children.Add(new TextBlock
                {
                    Text = resolved.Emoji,
                    FontSize = 14 * labelScale,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            else if (resolved.Geometry != null)
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = resolved.Geometry,
                    Stroke = resolved.Stroke,
                    Fill = resolved.Fill,
                    StrokeThickness = Math.Max(0.6, 1.1 * labelScale),
                    Width = 16 * labelScale,
                    Height = 16 * labelScale,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                glyphRow.Children.Add(new Viewbox
                {
                    Width = 16 * labelScale,
                    Height = 16 * labelScale,
                    Child = path,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            glyphRow.Children.Add(new TextBlock
            {
                Text = entry.Lines.Count > 0 ? entry.Lines[0] : "",
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 10 * labelScale,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(pal.Primary),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 175 * labelScale,
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(glyphRow);

            for (var i = 1; i < entry.Lines.Count; i++)
            {
                stack.Children.Add(MakeLine(entry, i, labelScale, pal));
            }
        }
        else
        {
            for (var i = 0; i < entry.Lines.Count; i++)
                stack.Children.Add(MakeLine(entry, i, labelScale, pal));
        }

        var accent = entry.Kind switch
        {
            Viewport3DLabelKind.Leg => pal.Accent,
            Viewport3DLabelKind.Symbol => Color.FromRgb(45, 168, 154),
            Viewport3DLabelKind.FieldCatalog => Color.FromRgb(124, 58, 237),
            Viewport3DLabelKind.StationSnapshot => Color.FromRgb(14, 165, 233),
            Viewport3DLabelKind.AiTag => Color.FromRgb(234, 179, 8),
            Viewport3DLabelKind.DepthSpan => Color.FromRgb(234, 88, 12),
            Viewport3DLabelKind.Environment => pal.Muted,
            _ => pal.ChipBorder,
        };

        var clickable = onLabelClick != null && IsClickableKind(entry.Kind);
        var pad = 5 * labelScale;
        var border = new Border
        {
            CornerRadius = new CornerRadius(4 * labelScale),
            Background = new SolidColorBrush(pal.ChipFill),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness((entry.Kind == Viewport3DLabelKind.Station ? 1.1 : 0.85) * labelScale),
            Padding = new Thickness(pad, pad * 0.6, pad, pad * 0.8),
            Child = stack,
            Opacity = 0.92,
            Tag = entry,
            Cursor = clickable ? Cursors.Hand : null,
            IsHitTestVisible = false,
        };

        return border;
    }

    private static TextBlock MakeLine(
        Viewport3DLabelEntry entry,
        int index,
        double labelScale,
        SurveyLabelPalette pal)
    {
        var isPrimary = index == 0;
        return new TextBlock
        {
            Text = entry.Lines[index],
            FontFamily = isPrimary && entry.Kind == Viewport3DLabelKind.Station
                ? new FontFamily("Georgia, Palatino Linotype, serif")
                : new FontFamily("Consolas, Courier New, monospace"),
            FontSize = entry.Kind switch
            {
                Viewport3DLabelKind.Station when isPrimary => 10.5 * labelScale,
                Viewport3DLabelKind.Symbol when isPrimary => 10 * labelScale,
                Viewport3DLabelKind.FieldCatalog when isPrimary => 9.5 * labelScale,
                Viewport3DLabelKind.Leg when isPrimary => 9.5 * labelScale,
                _ => 8.5 * labelScale,
            },
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(isPrimary ? pal.Primary : pal.Muted),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 175 * labelScale,
        };
    }

    private static bool IsClickableKind(Viewport3DLabelKind kind) =>
        kind is Viewport3DLabelKind.Station
            or Viewport3DLabelKind.Symbol
            or Viewport3DLabelKind.FieldCatalog;
}
