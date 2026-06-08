using System.Windows;
using CaveAiProForWindows;

namespace CaveAiProForWindows.Services;

/// <summary>Cross-view navigation: jump to a station on Plan / X-Ray / Section.</summary>
public static class SurveyWorkspaceNavigator
{
    private static WeakReference<MainWindow>? _mainWindowRef;

    public static void Register(MainWindow mainWindow) =>
        _mainWindowRef = new WeakReference<MainWindow>(mainWindow);

    public static void JumpToStation(string stationName, string source = "Navigator")
    {
        if (string.IsNullOrWhiteSpace(stationName))
            return;

        SurveyStationSelectionHub.Select(stationName.Trim(), source, requestZoom: true);

        if (_mainWindowRef?.TryGetTarget(out var main) != true || main == null)
            return;

        main.Dispatcher.BeginInvoke(() => main.FocusStationOnWorkspace(stationName.Trim()));
    }

    public static void OpenGeoBioTab()
    {
        if (_mainWindowRef?.TryGetTarget(out var main) != true || main == null)
            return;

        main.Dispatcher.BeginInvoke(() => main.FocusGeoBioTab());
    }

    public static bool TryGetMainWindow(out MainWindow? mainWindow)
    {
        mainWindow = null;
        return _mainWindowRef?.TryGetTarget(out mainWindow) == true;
    }
}
