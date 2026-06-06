using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Survey label density preset shared by Plan / Section / Sketch maps and exports.</summary>
public enum SurveyDetailDensity
{
    Minimal,
    Standard,
    Full,
}

public static class SurveyDetailDensityParser
{
    public static SurveyDetailDensity Parse(string? s) =>
        Enum.TryParse<SurveyDetailDensity>(s?.Trim(), true, out var v) ? v : SurveyDetailDensity.Full;

    public static string ToPersistedString(SurveyDetailDensity v) => v.ToString();
}

public static class SurveyDetailDensityMapper
{
    public static void ApplyToMapTab(MapTabPersistedState tab, SurveyDetailDensity density)
    {
        switch (density)
        {
            case SurveyDetailDensity.Minimal:
                tab.StationNames = true;
                tab.StationZ = false;
                tab.LegSurveyDetails = false;
                tab.StationEnvironment = false;
                tab.DepthSpanAnnotations = false;
                tab.BracketMarkers = false;
                tab.LoopClosureHighlights = false;
                break;
            case SurveyDetailDensity.Standard:
                tab.StationNames = true;
                tab.StationZ = true;
                tab.LegSurveyDetails = true;
                tab.StationEnvironment = false;
                tab.DepthSpanAnnotations = true;
                tab.BracketMarkers = false;
                tab.LoopClosureHighlights = true;
                break;
            default:
                tab.StationNames = true;
                tab.StationZ = true;
                tab.LegSurveyDetails = true;
                tab.StationEnvironment = true;
                tab.DepthSpanAnnotations = true;
                tab.BracketMarkers = true;
                tab.LoopClosureHighlights = true;
                break;
        }
    }

    public static PlanCanvasDrawOptions ToDrawOptions(
        SurveyDetailDensity density,
        SurveyCanvasKind kind,
        SurveyVisualizationMode visualization,
        CartographicIntensity intensity,
        bool overlay = true,
        SurveyMapPickHighlight? pick = null)
    {
        var tab = new MapTabPersistedState();
        ApplyToMapTab(tab, density);
        return PlanCanvasDrawOptionsFactory.FromMapTab(tab, kind, visualization, intensity, overlay, pick);
    }
}
