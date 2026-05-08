using System.Windows;
using System.Windows.Media;

namespace CaveAiProForWindows.Services;

/// <summary>Shared zoom step for plan / section / sketch map surfaces (mouse wheel and toolbar).</summary>
public static class MapZoomInteractions
{
    public const double StepFactor = 1.12;

    public const double MinScale = 0.12;

    public const double MaxScale = 12.0;

    public static double ClampScale(double scale) => Math.Clamp(scale, MinScale, MaxScale);

    public static double ScaleAfterStep(double currentScale, bool zoomIn) =>
        ClampScale(currentScale * (zoomIn ? StepFactor : 1.0 / StepFactor));

    /// <summary>
    /// Uniform scale with <see cref="ScaleTransform.CenterX"/> / <see cref="ScaleTransform.CenterY"/> in map-local
    /// coordinates, then <see cref="TranslateTransform"/> so the map point under <paramref name="mapLocalFocus"/>
    /// stays fixed (CAD / GIS zoom-to-cursor).
    /// </summary>
    public static bool TryApplyZoomStep(
        ScaleTransform zoomScale,
        TranslateTransform zoomPan,
        bool zoomIn,
        Point mapLocalFocus)
    {
        var oldScale = zoomScale.ScaleX;
        var newScale = ScaleAfterStep(oldScale, zoomIn);
        return TryApplyZoomToScale(zoomScale, zoomPan, oldScale, newScale, mapLocalFocus);
    }

    public static bool TryApplyZoomToScale(
        ScaleTransform zoomScale,
        TranslateTransform zoomPan,
        double oldScale,
        double newScale,
        Point mapLocalFocus)
    {
        newScale = ClampScale(newScale);
        if (Math.Abs(newScale - oldScale) < 1e-9)
            return false;

        var cx = zoomScale.CenterX;
        var cy = zoomScale.CenterY;
        zoomPan.X += (mapLocalFocus.X - cx) * (oldScale - newScale);
        zoomPan.Y += (mapLocalFocus.Y - cy) * (oldScale - newScale);
        zoomScale.ScaleX = newScale;
        zoomScale.ScaleY = newScale;
        return true;
    }
}
