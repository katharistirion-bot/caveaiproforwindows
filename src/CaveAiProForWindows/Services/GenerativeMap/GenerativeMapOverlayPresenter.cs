using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>Places the latest generative render on the survey canvas aligned to plan layout.</summary>
public static class GenerativeMapOverlayPresenter
{
    public const string OverlayTag = "generative_map_overlay";

    private const int OverlayZIndex = 101;

    public static void Apply(
        Canvas surveyCanvas,
        PlanCanvasSurveyLayout layout,
        BitmapSource? bitmap,
        bool visible,
        double opacity = 0.94)
    {
        Clear(surveyCanvas);
        if (bitmap == null || !visible)
            return;

        var nw = layout.WorldToCanvas((float)layout.WMinX, (float)layout.WMaxY);
        var se = layout.WorldToCanvas((float)layout.WMaxX, (float)layout.WMinY);
        var left = Math.Min(nw.X, se.X);
        var top = Math.Min(nw.Y, se.Y);
        var width = Math.Max(4, Math.Abs(se.X - nw.X));
        var height = Math.Max(4, Math.Abs(se.Y - nw.Y));

        var img = new Image
        {
            Source = bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
            Opacity = opacity,
            SnapsToDevicePixels = true,
            Tag = OverlayTag,
            ToolTip = "AI generative map render (Replicate ControlNet)",
        };
        Panel.SetZIndex(img, OverlayZIndex);
        Canvas.SetLeft(img, left);
        Canvas.SetTop(img, top);
        surveyCanvas.Children.Add(img);
    }

    public static void Clear(Canvas surveyCanvas)
    {
        for (var i = surveyCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (surveyCanvas.Children[i] is FrameworkElement fe &&
                Equals(fe.Tag, OverlayTag))
                surveyCanvas.Children.RemoveAt(i);
        }
    }
}
