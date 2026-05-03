using Wpf = System.Windows;

namespace CaveAiProForWindows.Services;

/// <summary>Centralizes simple user-facing dialogs so view models stay readable.</summary>
public static class UserErrorReporter
{
    public static void ShowInformation(Wpf.Window? owner, string message, string title)
    {
        Wpf.MessageBox.Show(
            owner,
            message,
            title,
            Wpf.MessageBoxButton.OK,
            Wpf.MessageBoxImage.Information);
    }

    public static void ShowWarning(Wpf.Window? owner, string message, string title)
    {
        Wpf.MessageBox.Show(
            owner,
            message,
            title,
            Wpf.MessageBoxButton.OK,
            Wpf.MessageBoxImage.Warning);
    }
}
