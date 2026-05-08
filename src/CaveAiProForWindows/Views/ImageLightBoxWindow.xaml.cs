using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Full-screen modal viewer for clicking-through gallery thumbnails. Shows the bitmap on a black
/// background (Stretch=Uniform), with caption header and click/Esc to dismiss.
/// </summary>
public partial class ImageLightBoxWindow : Window
{
    public ImageLightBoxWindow(BitmapSource bitmap, string caption, Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        LightBoxImage.Source = bitmap;
        CaptionText.Text = caption ?? "";
    }

    /// <summary>Convenience: open a non-modal lightbox (modeless so the parent stays interactive).</summary>
    public static void Show(BitmapSource bitmap, string caption, Window? owner)
    {
        if (bitmap == null)
            return;
        var win = new ImageLightBoxWindow(bitmap, caption, owner);
        win.Show();
    }

    private void OnRootClick(object sender, MouseButtonEventArgs e) => Close();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter or Key.Space)
        {
            Close();
            e.Handled = true;
        }
    }
}
