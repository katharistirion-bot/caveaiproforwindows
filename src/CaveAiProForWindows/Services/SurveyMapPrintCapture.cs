using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaveAiProForWindows.Services;

/// <summary>Renders a WPF visual tree branch to a <see cref="BitmapSource"/> for print layout.</summary>
public static class SurveyMapPrintCapture
{
    public static BitmapSource CaptureElement(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var w = element.Width > 0 ? element.Width : element.ActualWidth;
        var h = element.Height > 0 ? element.Height : element.ActualHeight;
        if (w < 1 || h < 1)
        {
            w = Math.Max(w, element.RenderSize.Width);
            h = Math.Max(h, element.RenderSize.Height);
        }

        w = Math.Max(1, w);
        h = Math.Max(1, h);

        element.Measure(new Size(w, h));
        element.Arrange(new Rect(0, 0, w, h));
        element.UpdateLayout();

        var pxW = (int)Math.Max(1, Math.Ceiling(w));
        var pxH = (int)Math.Max(1, Math.Ceiling(h));
        var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);
        rtb.Freeze();
        return rtb;
    }
}
