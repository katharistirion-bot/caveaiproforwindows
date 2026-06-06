namespace CaveAiProForWindows.Services;

/// <summary>Cross-view station selection (X-Ray ↔ Plan ↔ Section).</summary>
public static class SurveyStationSelectionHub
{
    public static event EventHandler<SurveyStationSelectionEventArgs>? StationSelected;

    public static event EventHandler<SurveyStationSelectionEventArgs>? SelectionCleared;

    public static void Select(string stationName, string source, bool requestZoom = false)
    {
        if (string.IsNullOrWhiteSpace(stationName))
            return;

        StationSelected?.Invoke(
            null,
            new SurveyStationSelectionEventArgs(stationName.Trim(), source, requestZoom));
    }

    public static void ClearSelection(string source)
    {
        SelectionCleared?.Invoke(
            null,
            new SurveyStationSelectionEventArgs("", source, requestZoom: false));
    }

    public static void JumpTo(string stationName, string source) =>
        Select(stationName, source, requestZoom: true);
}

public sealed class SurveyStationSelectionEventArgs : EventArgs
{
    public SurveyStationSelectionEventArgs(string stationName, string source, bool requestZoom)
    {
        StationName = stationName;
        Source = source;
        RequestZoom = requestZoom;
    }

    public string StationName { get; }

    public string Source { get; }

    public bool RequestZoom { get; }
}
