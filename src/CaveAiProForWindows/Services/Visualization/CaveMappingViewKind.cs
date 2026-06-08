namespace CaveAiProForWindows.Services.Visualization;

/// <summary>
/// Original CaveAI Pro desktop map products — not tied to any third-party speleo CAD layout.
/// </summary>
public enum CaveMappingViewKind
{
    /// <summary>Plan (X,Y metres): centreline, LRUD passage ribbons, vectors, splays.</summary>
    PlanDrafting2D = 0,

    /// <summary>Extended elevation (developed distance × station Z): Android section / profile semantics.</summary>
    ExtendedProfile2D = 1,

    /// <summary>Long-profile chainage × Z (Android viewMode 3).</summary>
    LongProfile2D = 2,

    /// <summary>LRUD tube mesh + optional topography surface grid in WPF <see cref="System.Windows.Media.Media3D.Viewport3D"/>.</summary>
    Topography3D = 3,
}
