namespace CaveAiProForWindows.Services;

/// <summary>Native WPF vector rendering style (Android-style advanced views).</summary>
public enum SurveyVisualizationMode
{
    /// <summary>Default plan or section appearance.</summary>
    Standard = 0,

    Plan2Tone = 1,
    PlanXRay = 2,
    LongProfile = 3,
    Pseudo3D = 4,

    SectionNight = 10,
    SectionXRay = 11,
}

public static class SurveyVisualizationModeExtensions
{
    public static bool UsesDarkSurveyCanvas(this SurveyVisualizationMode m) =>
        m is SurveyVisualizationMode.Plan2Tone
            or SurveyVisualizationMode.PlanXRay
            or SurveyVisualizationMode.SectionNight
            or SurveyVisualizationMode.SectionXRay
            or SurveyVisualizationMode.LongProfile
            or SurveyVisualizationMode.Pseudo3D;

    /// <summary>Skip global raster underlays and station photo overlays (vector-only emphasis).</summary>
    public static bool SuppressRasterUnderlays(this SurveyVisualizationMode m) =>
        m.UsesDarkSurveyCanvas();

    public static bool ShowSplayXRayGeometry(this SurveyVisualizationMode m) =>
        m is SurveyVisualizationMode.PlanXRay or SurveyVisualizationMode.SectionXRay;
}
