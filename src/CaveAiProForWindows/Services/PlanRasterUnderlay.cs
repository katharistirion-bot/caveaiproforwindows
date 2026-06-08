using System.Windows.Media.Imaging;

namespace CaveAiProForWindows.Services;

/// <summary>One decoded raster for Plan/Section underlay (may share the survey frame with vectors via <see cref="WorldExtentMetres"/>).</summary>
public sealed class PlanRasterUnderlay
{
    public required BitmapSource Bitmap { get; init; }

    /// <summary>Absolute path that was decoded (for debug / tooltips).</summary>
    public string ResolvedPath { get; init; } = "";

    public string Category { get; init; } = "";

    /// <summary>
    /// Optional axis-aligned extent in the same survey metres as <see cref="PlanScene"/> (plan X/Y or section axes).
    /// When null, the image is stretched to the same padded fit box as the vector scene (legacy behaviour).
    /// </summary>
    public PlanRasterWorldExtentMetres? WorldExtentMetres { get; init; }

    /// <summary>When set, used instead of the auto multi-layer opacity formula in <see cref="Views.PlanCanvasRenderer"/>.</summary>
    public double? OpacityOverride { get; init; }
}

/// <summary>Survey-frame rectangle in metres for placing a raster underlay.</summary>
public sealed class PlanRasterWorldExtentMetres
{
    public double MinX { get; init; }
    public double MaxX { get; init; }
    public double MinY { get; init; }
    public double MaxY { get; init; }
}
