using System.IO;
using System.Text.Json;
using System.Windows;

namespace CaveAiProForWindows.Services;

/// <summary>Saves last main window bounds under LocalApplicationData.</summary>
public static class WindowPlacementStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaveAiProForWindows", "window.json");

    public static void ApplyTo(Window window)
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var json = File.ReadAllText(StorePath);
            var dto = JsonSerializer.Deserialize<PlacementDto>(json);
            if (dto == null || dto.Width < 400 || dto.Height < 320) return;

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = Clamp(dto.Left, dto.Width, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenWidth);
            window.Top = Clamp(dto.Top, dto.Height, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenHeight);
            window.Width = dto.Width;
            window.Height = dto.Height;
            if (dto.Maximized)
                window.WindowState = WindowState.Maximized;
        }
        catch
        {
            /* ignore */
        }
    }

    public static void SaveFrom(Window window)
    {
        try
        {
            var r = window.WindowState == WindowState.Maximized
                ? window.RestoreBounds
                : new System.Windows.Rect(window.Left, window.Top, window.Width, window.Height);
            if (r.Width < 400 || r.Height < 320) return;

            var dto = new PlacementDto
            {
                Left = r.Left,
                Top = r.Top,
                Width = r.Width,
                Height = r.Height,
                Maximized = window.WindowState == WindowState.Maximized,
            };
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(dto, JsonOpts));
        }
        catch
        {
            /* ignore */
        }
    }

    private static double Clamp(double left, double width, double screenLeft, double screenWidth)
    {
        var right = left + width;
        var screenRight = screenLeft + screenWidth;
        if (left < screenLeft - width + 80) return screenLeft - width + 80;
        if (right > screenRight - 40) return screenRight - width - 40;
        return left;
    }

    private sealed class PlacementDto
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }
}
