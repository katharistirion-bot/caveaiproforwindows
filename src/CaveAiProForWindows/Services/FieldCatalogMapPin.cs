namespace CaveAiProForWindows.Services;

/// <summary>Geo/bio / field catalog entry pinned on the plan map (Android planMapX / planMapY).</summary>
public sealed record FieldCatalogMapPin(
    float X,
    float Y,
    string Title,
    string CategoryLabel,
    string? ScientificName);
