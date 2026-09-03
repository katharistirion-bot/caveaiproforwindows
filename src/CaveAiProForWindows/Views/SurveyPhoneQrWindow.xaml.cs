using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Shows a QR code and copy-link controls so the user can start a Cave AI Pro survey on Android.
/// </summary>
public partial class SurveyPhoneQrWindow : Window
{
    private readonly string _url;

    public SurveyPhoneQrWindow(string caveName, string url)
    {
        _url = url;
        InitializeComponent();
        CaveNameText.Text = caveName;
        UrlBox.Text = url;
        LoadQrImage(url);
    }

    private void LoadQrImage(string url)
    {
        try
        {
            var modules = QrCodeGenerator.Encode(url, 3);
            QrImage.Source = RenderQrBitmap(modules);
        }
        catch
        {
            // QR render failed; user can still copy the URL
        }
    }

    private static BitmapSource RenderQrBitmap(bool[,] modules)
    {
        int size = modules.GetLength(0);
        const int cellPx = 8;
        const int border = 24;
        int imgSize = size * cellPx + border * 2;

        var bitmap = new WriteableBitmap(imgSize, imgSize, 96, 96, PixelFormats.Bgr32, null);
        var pixels = new int[imgSize * imgSize];

        // Fill white
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = unchecked((int)0xFFFFFFFF);

        // Draw modules
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (!modules[row, col])
                    continue;
                int x0 = border + col * cellPx;
                int y0 = border + row * cellPx;
                for (int dy = 0; dy < cellPx; dy++)
                    for (int dx = 0; dx < cellPx; dx++)
                        pixels[(y0 + dy) * imgSize + (x0 + dx)] = unchecked((int)0xFF000000);
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, imgSize, imgSize), pixels, imgSize * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_url); }
        catch { /* ignore */ }
        CopyBtn.Content = "Copied!";
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open link", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}