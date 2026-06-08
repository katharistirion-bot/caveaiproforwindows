namespace CaveAiProForWindows.Services;

public enum Viewport3DSectionCutAxis
{
    HorizontalZ,
    VerticalX,
    VerticalY,
}

public sealed record Viewport3DSectionCutOptions(
    Viewport3DSectionCutAxis Axis = Viewport3DSectionCutAxis.HorizontalZ,
    double NormalizedPosition = 0.5,
    bool Enabled = false);

/// <summary>Mesh, overlay, and annotation toggles for the pseudo-3D viewport.</summary>
public sealed record Viewport3DDisplayOptions(
    Viewport3DAnnotationOptions? Annotations = null,
    bool ShowSplines = true,
    bool ShowTopographyGrid = false,
    bool ShowDemSurface = false,
    Viewport3DSectionCutOptions? SectionCut = null)
{
    public static Viewport3DDisplayOptions Default { get; } = new();

    public Viewport3DAnnotationOptions ResolvedAnnotations => Annotations ?? Viewport3DAnnotationOptions.AllOn;

    public static Viewport3DDisplayOptions Clean { get; } = new(
        Annotations: new Viewport3DAnnotationOptions(
            ShowLabels: false,
            ShowStationNames: false,
            ShowStationZ: false,
            ShowLegDetails: false,
            ShowEnvironment: false,
            ShowDepthSpans: false,
            ShowBrackets: false),
        ShowSplines: false,
        ShowTopographyGrid: false,
        ShowDemSurface: false);
}
