using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CaveAiProForWindows.Views;
using Wpf = System.Windows;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Helper that shows non-modal success messages. Routes to the active window's <see cref="SnackbarHost"/>
/// when present; falls back to <see cref="MessageBox"/> otherwise so headless / unit-test paths still work.
/// </summary>
public static class SnackbarService
{
    public static void Show(string message, string? actionLabel = null, Action? onAction = null, int durationMs = 4000)
    {
        var owner = Wpf.Application.Current?.MainWindow;
        Show(owner, message, actionLabel, onAction, durationMs);
    }

    public static void Show(Window? owner, string message, string? actionLabel = null, Action? onAction = null,
        int durationMs = 4000)
    {
        var host = TryFindHost(owner);
        if (host != null)
        {
            host.Show(message, actionLabel, onAction, durationMs);
            return;
        }

        try
        {
            Wpf.MessageBox.Show(owner, message, "CAVE AI PRO", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            /* MessageBox can throw if there's no UI thread — best-effort only */
        }
    }

    /// <summary>Convenience: confirm a successful file export with a "Reveal" action that opens the folder.</summary>
    public static void ShowFileSaved(string savedFilePath, string? prefix = null)
    {
        var name = Path.GetFileName(savedFilePath);
        var msg = string.IsNullOrEmpty(prefix) ? $"Saved {name}" : $"{prefix} {name}";
        Show(msg, "Reveal", () => RevealInExplorer(savedFilePath));
    }

    public static void RevealInExplorer(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;
        try
        {
            if (File.Exists(filePath))
                Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
            else if (Directory.Exists(filePath))
                Process.Start("explorer.exe", "\"" + filePath + "\"");
        }
        catch
        {
            /* explorer launch is purely cosmetic */
        }
    }

    private static SnackbarHost? TryFindHost(Window? owner)
    {
        var win = owner ?? Wpf.Application.Current?.MainWindow;
        if (win == null)
            return null;
        return FindDescendant<SnackbarHost>(win);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T direct)
            return direct;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            var deep = FindDescendant<T>(child);
            if (deep != null)
                return deep;
        }

        return null;
    }
}
