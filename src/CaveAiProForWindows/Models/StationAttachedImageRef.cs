namespace CaveAiProForWindows.Models;

/// <summary>
/// A photo or sketch bitmap reference tied to a traverse station (field catalog, rocks, sketch objects, shot photos).
/// Resolved to pixels in <see cref="Views.PlanCanvasRenderer"/> using the same path rules as map underlays.
/// </summary>
public sealed class StationAttachedImageRef
{
    public StationAttachedImageRef(
        string stationName,
        string uriOrPath,
        string category,
        double? rotationDegrees,
        double? scaleHint)
    {
        StationName = stationName;
        UriOrPath = uriOrPath;
        Category = category;
        RotationDegrees = rotationDegrees;
        ScaleHint = scaleHint;
    }

    public string StationName { get; }
    public string UriOrPath { get; }
    public string Category { get; }
    public double? RotationDegrees { get; }
    /// <summary>Optional JSON scale multiplier (uniform); when null, renderer picks a default from px/m.</summary>
    public double? ScaleHint { get; }
}
