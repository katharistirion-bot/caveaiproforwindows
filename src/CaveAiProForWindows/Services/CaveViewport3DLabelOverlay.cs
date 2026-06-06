using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using System.Windows.Media.Media3D;



namespace CaveAiProForWindows.Services;



/// <summary>Screen-space cartographic chips projected from 3D survey coordinates.</summary>

public static class CaveViewport3DLabelOverlay

{

    private const double ChipWidthPx = 78;

    private const double ChipHeightPx = 56;



    public static void Sync(Viewport3D viewport, Canvas? canvas, IReadOnlyList<Viewport3DLabelEntry>? labels)

    {

        if (canvas == null)

            return;



        canvas.Children.Clear();

        if (labels == null || labels.Count == 0 || viewport.Camera == null)

            return;



        var w = viewport.ActualWidth;

        var h = viewport.ActualHeight;

        if (w < 1 || h < 1)

            return;



        var ordered = labels

            .OrderBy(l => l.Kind switch

            {

                Viewport3DLabelKind.Station => 0,

                Viewport3DLabelKind.DepthSpan => 1,

                Viewport3DLabelKind.Bracket => 2,

                Viewport3DLabelKind.Leg => 3,

                _ => 4,

            })

            .ToList();



        var legStride = ordered.Count(l => l.Kind == Viewport3DLabelKind.Leg) switch

        {

            > 80 => 6,

            > 40 => 4,

            > 20 => 2,

            _ => 1,

        };



        var legIndex = 0;

        var boxes = new List<Rect>();



        foreach (var entry in ordered)

        {

            if (entry.Kind == Viewport3DLabelKind.Leg)

            {

                var idx = legIndex++;

                if (legStride > 1 && idx % legStride != 0 && idx != 0)

                    continue;

            }



            if (entry.Kind is Viewport3DLabelKind.Environment && ordered.Count > 30)

                continue;



            if (!Viewport3DProjection.TryWorldToScreen(viewport, entry.World, out var screen))

                continue;



            var rect = new Rect(screen.X - ChipWidthPx * 0.5, screen.Y - ChipHeightPx * 0.5, ChipWidthPx, ChipHeightPx);

            if (entry.Kind is Viewport3DLabelKind.Leg or Viewport3DLabelKind.Environment)

            {

                if (boxes.Any(b => b.IntersectsWith(rect)))

                    continue;

            }



            var chip = BuildChip(entry);

            chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var cw = chip.DesiredSize.Width;

            var ch = chip.DesiredSize.Height;

            Canvas.SetLeft(chip, screen.X - cw * 0.5);

            Canvas.SetTop(chip, screen.Y - ch * 0.5);

            canvas.Children.Add(chip);

            boxes.Add(new Rect(screen.X - cw * 0.5, screen.Y - ch * 0.5, cw, ch));

        }

    }



    private static Border BuildChip(Viewport3DLabelEntry entry)

    {

        var pal = SurveyMapLabelStyle.Palette(darkCanvas: true, highContrast: false);

        var stack = new StackPanel { Orientation = Orientation.Vertical };



        for (var i = 0; i < entry.Lines.Count; i++)

        {

            var line = entry.Lines[i];

            var isPrimary = i == 0;

            stack.Children.Add(new TextBlock

            {

                Text = line,

                FontFamily = isPrimary && entry.Kind == Viewport3DLabelKind.Station

                    ? new FontFamily("Georgia, Palatino Linotype, serif")

                    : new FontFamily("Consolas, Courier New, monospace"),

                FontSize = entry.Kind switch

                {

                    Viewport3DLabelKind.Station when isPrimary => 11.5,

                    Viewport3DLabelKind.Leg when isPrimary => 10.5,

                    _ => 9,

                },

                FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,

                Foreground = new SolidColorBrush(isPrimary ? pal.Primary : pal.Muted),

                TextWrapping = TextWrapping.Wrap,

                MaxWidth = 160,

            });

        }



        var accent = entry.Kind switch

        {

            Viewport3DLabelKind.Leg => pal.Accent,

            Viewport3DLabelKind.DepthSpan => Color.FromRgb(234, 88, 12),

            Viewport3DLabelKind.Environment => pal.Muted,

            _ => pal.ChipBorder,

        };



        return new Border

        {

            CornerRadius = new CornerRadius(4),

            Background = new SolidColorBrush(pal.ChipFill),

            BorderBrush = new SolidColorBrush(accent),

            BorderThickness = new Thickness(entry.Kind == Viewport3DLabelKind.Station ? 1.2 : 0.9),

            Padding = new Thickness(6, 3, 6, 4),

            Child = stack,

            Opacity = 0.94,

        };

    }

}


