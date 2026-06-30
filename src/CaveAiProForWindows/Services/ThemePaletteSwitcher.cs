using System.Diagnostics;
using System.Windows;

namespace CaveAiProForWindows.Services;

/// <summary>Merges <c>ThemePalette.Light.xaml</c> or <c>ThemePalette.Dark.xaml</c> based on persisted settings.</summary>
public static class ThemePaletteSwitcher
{
    private static readonly Uri LightUri = new("pack://application:,,,/ThemePalette.Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkUri = new("pack://application:,,,/ThemePalette.Dark.xaml", UriKind.Absolute);

    public static bool IsDark { get; private set; }

    public static event EventHandler? PaletteChanged;

    public static void ApplyInitial()
    {
        var dark = AppUiSettingsStore.LoadOrDefault().UseDarkTheme;
        Apply(dark);
    }

    public static void ApplyFromSettings()
    {
        Apply(AppUiSettingsStore.LoadOrDefault().UseDarkTheme);
    }

    public static void SetDarkTheme(bool dark) => SetDark(dark);

    public static void SetDark(bool dark)
    {
        var all = AppUiSettingsStore.LoadOrDefault();
        if (all.UseDarkTheme == dark)
        {
            Apply(dark);
            return;
        }

        all.UseDarkTheme = dark;
        AppUiSettingsStore.Save(all);
        Apply(dark);
    }

    private static void Apply(bool dark)
    {
        IsDark = dark;
        SurveyCanvasTheme.SetDark(dark);

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

            m.Insert(0, new ResourceDictionary { Source = dark ? DarkUri : LightUri });
        }
        catch (Exception ex)
        {
            Trace.TraceError("ThemePaletteSwitcher.Apply: {0}", ex);
        }

        PaletteChanged?.Invoke(null, EventArgs.Empty);
    }
}
