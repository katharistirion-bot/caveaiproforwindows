using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CaveAiProForWindows.Views;

public partial class AiRenderCompareWindow : Window
{
    public AiRenderCompareWindow(byte[] maskPng, byte[] resultPng, Window? owner)
    {
        InitializeComponent();
        if (owner != null)
            Owner = owner;
        MaskImage.Source = DecodePng(maskPng);
        ResultImage.Source = DecodePng(resultPng);
    }

    private static BitmapSource? DecodePng(byte[] png)
    {
        if (png is not { Length: > 0 })
            return null;
        try
        {
            using var ms = new MemoryStream(png);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
