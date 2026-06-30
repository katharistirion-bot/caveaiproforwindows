using System.Windows;

namespace CaveAiProForWindows.Services;

public static class PublicLibraryEntryPicker
{
    public enum Choice { Cancel, NativeCatalog, WebMap, ExternalBrowser }

    public static Choice Prompt(Window? owner)
    {
        var result = MessageBox.Show(
            owner,
            "Choose how to open the Public Library:\n\n" +
            "Yes = Native reference catalog (offline cache, favorites, field trip import)\n" +
            "No = Web map in app (WebView2, download backups)\n" +
            "Cancel = close without opening",
            "Public Library",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => Choice.NativeCatalog,
            MessageBoxResult.No => Choice.WebMap,
            _ => Choice.Cancel,
        };
    }
}