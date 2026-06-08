namespace CaveAiProForWindows.Services;

/// <summary>LRUD tube tessellation preset for the 3D viewport.</summary>
public enum TubeMeshQuality
{
    Standard,
    High,
}

public static class TubeMeshQualityResolver
{
    public const int StandardEllipseSegments = 20;
    public const int HighEllipseSegments = 24;
    public const float StandardCenterlineSampleM = 0.35f;
    public const float HighCenterlineSampleM = 0.22f;

    public static int EllipseSegments(TubeMeshQuality quality) =>
        quality == TubeMeshQuality.High ? HighEllipseSegments : StandardEllipseSegments;

    public static float CenterlineSampleM(TubeMeshQuality quality) =>
        quality == TubeMeshQuality.High ? HighCenterlineSampleM : StandardCenterlineSampleM;

    /// <summary>Explicit persisted value wins; otherwise Rich cartographic maps to High.</summary>
    public static TubeMeshQuality Resolve(string? persistedTubeQuality, string? cartographicIntensity)
    {
        if (!string.IsNullOrWhiteSpace(persistedTubeQuality) &&
            Enum.TryParse<TubeMeshQuality>(persistedTubeQuality, ignoreCase: true, out var explicitQuality))
            return explicitQuality;

        return CartographicIntensityParser.Parse(cartographicIntensity) == CartographicIntensity.Rich
            ? TubeMeshQuality.High
            : TubeMeshQuality.Standard;
    }
}

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
    Viewport3DSectionCutOptions? SectionCut = null,
    TubeMeshQuality TubeQuality = TubeMeshQuality.Standard)
{
    public static Viewport3DDisplayOptions Default { get; } = new();

    public int EllipseSegments => TubeMeshQualityResolver.EllipseSegments(TubeQuality);

    public float CenterlineSampleM => TubeMeshQualityResolver.CenterlineSampleM(TubeQuality);

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

    /// <summary>High-quality competitive presentation: LRUD tube, splays, minimal labels.</summary>
    public static Viewport3DDisplayOptions Competitive { get; } = new(
        Annotations: new Viewport3DAnnotationOptions(
            ShowLabels: true,
            ShowStationNames: true,
            ShowStationZ: false,
            ShowLegDetails: false,
            ShowEnvironment: false,
            ShowDepthSpans: false,
            ShowBrackets: false,
            ShowMapSymbols: false,
            ShowFieldCatalog: false,
            ShowStationSnapshots: false,
            ShowAiTags: false),
        ShowSplines: true,
        ShowTopographyGrid: false,
        ShowDemSurface: false,
        TubeQuality: TubeMeshQuality.High);
}
