namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>ControlNet input sizing — longest edge capped at 1024 px, both dimensions multiples of 8.</summary>
public static class ControlNetMaskDimensions
{
    public const int MaxEdgePixels = 1024;

    public const int DimensionStep = 8;

    /// <summary>
    /// Computes API width/height from a source mask. When the source exceeds <see cref="MaxEdgePixels"/>,
    /// the longest edge becomes exactly 1024 px; aspect ratio is preserved; both dimensions are multiples of 8.
    /// </summary>
    public static (int Width, int Height) ComputeApiDimensions(
        int sourceWidth,
        int sourceHeight,
        int maxEdgePixels = MaxEdgePixels)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return (DimensionStep, DimensionStep);

        var maxDim = Math.Max(sourceWidth, sourceHeight);
        if (maxDim <= maxEdgePixels)
        {
            return (
                Math.Max(DimensionStep, SnapToMultipleOf8(sourceWidth)),
                Math.Max(DimensionStep, SnapToMultipleOf8(sourceHeight)));
        }

        if (sourceWidth >= sourceHeight)
        {
            var width = maxEdgePixels;
            var height = SnapToMultipleOf8(
                Math.Max(DimensionStep, (int)Math.Round(sourceHeight * maxEdgePixels / (double)sourceWidth)));
            return (width, height);
        }

        var h = maxEdgePixels;
        var w = SnapToMultipleOf8(
            Math.Max(DimensionStep, (int)Math.Round(sourceWidth * maxEdgePixels / (double)sourceHeight)));
        return (w, h);
    }

    /// <summary>Rounds to the nearest multiple of <see cref="DimensionStep"/> (minimum 8).</summary>
    public static int SnapToMultipleOf8(int value) =>
        Math.Max(DimensionStep, ((value + DimensionStep / 2) / DimensionStep) * DimensionStep);

    public static bool IsMultipleOf8(int value) => value > 0 && value % DimensionStep == 0;
}
