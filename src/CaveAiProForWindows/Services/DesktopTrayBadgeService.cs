using System.Windows;
using System.Windows.Shell;

namespace CaveAiProForWindows.Services;

/// <summary>Taskbar overlay badges for Android sync and collaboration notifications.</summary>
public static class DesktopTrayBadgeService
{
    public static void SetBadge(Window? window, int count)
    {
        if (window == null)
            return;

        window.Dispatcher.Invoke(() =>
        {
            if (count <= 0)
            {
                window.TaskbarItemInfo ??= new TaskbarItemInfo();
                window.TaskbarItemInfo.Overlay = null;
                return;
            }

            window.TaskbarItemInfo ??= new TaskbarItemInfo();
            window.TaskbarItemInfo.Overlay = CreateBadgeOverlay(Math.Min(count, 99));
            window.TaskbarItemInfo.Description = count == 1 ? "1 notification" : $"{count} notifications";
        });
    }

    private static System.Windows.Media.ImageSource CreateBadgeOverlay(int count)
    {
        const int size = 16;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawEllipse(
                System.Windows.Media.Brushes.Crimson,
                null,
                new System.Windows.Point(size / 2.0, size / 2.0),
                size / 2.0,
                size / 2.0);
            var text = new System.Windows.Media.FormattedText(
                count > 9 ? "9+" : count.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface("Segoe UI"),
                9,
                System.Windows.Media.Brushes.White,
                1.0);
            dc.DrawText(text, new System.Windows.Point(2, 1));
        }

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
