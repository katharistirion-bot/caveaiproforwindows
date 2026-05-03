using System.Diagnostics;
using System.Windows;

namespace CaveAiProForWindows.Services;
/// <summary>Merges <c>ThemePalette.Light.xaml</c> only — this desktop build is light appearance only.</summary>
public static class ThemePaletteSwitcher
{
    private static readonly Uri LightUri = new("pack://application:,,,/ThemePalette.Light.xaml", UriKind.Absolute);

    public static bool IsDark => false;

    public static event EventHandler? PaletteChanged;

    /// <summary>Call after <see cref="Application"/> resources exist; always applies the light palette.</summary>
    public static void ApplyInitial() => Apply();

    private static void Apply()
    {
        SurveyCanvasTheme.SetDark(false);

        var app = Application.Current;
        if (app?.Resources.MergedDictionaries is not { } m)
            return;

        try
        {
            for (var i = m.Count - 1; i >= 0; i--)
            {
                var s = m[i].Source?.OriginalString ?? "";
                if (s.Contains("ThemePalette.Light", StringComparison.OrdinalIgnoreCase) ||
                    s.Contains("ThemePalette.Dark", StringComparison.OrdinalIgnoreCase))
                    m.RemoveAt(i);
            }

            m.Insert(0, new ResourceDictionary { Source = LightUri });
        }
        catch (Exception ex)
        {
            Trace.TraceError("ThemePaletteSwitcher.Apply: {0}", ex);
        }

        PaletteChanged?.Invoke(null, EventArgs.Empty);
    }
}
