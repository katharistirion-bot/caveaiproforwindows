namespace CaveAiProForWindows.Services;

/// <summary>Controls which screen-space labels appear on the pseudo-3D viewport.</summary>
public sealed record Viewport3DAnnotationOptions(
    bool ShowLabels = true,
    bool ShowStationNames = true,
    bool ShowStationZ = true,
    bool ShowLegDetails = true,
    bool ShowEnvironment = true,
    bool ShowDepthSpans = true,
    bool ShowBrackets = true,
    bool ShowMapSymbols = true,
    bool ShowFieldCatalog = true,
    bool ShowStationSnapshots = true,
    bool ShowAiTags = true)
{
    public static Viewport3DAnnotationOptions AllOn { get; } = new();

    public static Viewport3DAnnotationOptions AllOff { get; } = new(
        ShowLabels: false,
        ShowStationNames: false,
        ShowStationZ: false,
        ShowLegDetails: false,
        ShowEnvironment: false,
        ShowDepthSpans: false,
        ShowBrackets: false,
        ShowMapSymbols: false,
        ShowFieldCatalog: false,
        ShowStationSnapshots: false,
        ShowAiTags: false);
}
