using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Survey-space geometry for plan or section export (metres, Android survey frame).</summary>
public sealed class PlanScene
{
    public float MinX { get; init; }
    public float MaxX { get; init; }
    public float MinY { get; init; }
    public float MaxY { get; init; }

    public IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> Stations { get; init; } =
        new Dictionary<string, SurveyStationGeometry.StationPlanCoords>(StringComparer.Ordinal);

    public IReadOnlyList<(float x1, float y1, float x2, float y2)> TraverseSegments { get; init; } =
        Array.Empty<(float, float, float, float)>();

    public IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> WallPolylines { get; init; } =
        Array.Empty<SurveyStationGeometry.PlanVectorPolyline>();

    public IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> VectorPolylines { get; init; } =
        Array.Empty<SurveyStationGeometry.PlanVectorPolyline>();

    public IReadOnlyList<SurveyStationGeometry.PlanMapSymbol> Symbols { get; init; } =
        Array.Empty<SurveyStationGeometry.PlanMapSymbol>();

    /// <summary>Field catalog / rocks / sketch / shot photos tied to a traverse station (resolved at render time).</summary>
    public IReadOnlyList<StationAttachedImageRef> StationAttachedImages { get; init; } =
        Array.Empty<StationAttachedImageRef>();

    /// <summary>LRUD / radial splay segments in the same 2D frame as <see cref="Stations"/>.</summary>
    public IReadOnlyList<(float x1, float y1, float x2, float y2)> SplaySegments { get; init; } =
        Array.Empty<(float, float, float, float)>();

    /// <summary>Field catalog / rock samples pinned on plan (planMapX / planMapY).</summary>
    public IReadOnlyList<FieldCatalogMapPin> FieldCatalogPins { get; init; } =
        Array.Empty<FieldCatalogMapPin>();

    /// <summary>Traverse legs that close onto an existing station.</summary>
    public IReadOnlyList<LoopClosingLegHighlight> LoopClosingLegs { get; init; } =
        Array.Empty<LoopClosingLegHighlight>();

    /// <summary>LRUD ribbon / wall geometry QC highlights.</summary>
    public IReadOnlyList<LrudRibbonQcHighlight> LrudQcHighlights { get; init; } =
        Array.Empty<LrudRibbonQcHighlight>();

    public float SpanX => Math.Max(1e-6f, MaxX - MinX);
    public float SpanY => Math.Max(1e-6f, MaxY - MinY);
}
