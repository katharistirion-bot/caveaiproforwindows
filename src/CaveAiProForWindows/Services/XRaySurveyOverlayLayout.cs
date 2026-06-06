using System.Windows;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Builds a <see cref="PlanCanvasSurveyLayout"/> aligned to an X-Ray geo or best-fit canvas mapping so generative AI
/// overlays and Android map symbols land on the same pixel grid as the traverse.
/// </summary>
public static class XRaySurveyOverlayLayout
{
    /// <summary>
    /// Derives survey-metres → canvas layout from traverse bounds mapped through <paramref name="worldToCanvas"/>.
    /// </summary>
    public static PlanCanvasSurveyLayout? BuildFromSurveyBounds(
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        IEnumerable<SurveyStationGeometry.PlanMapSymbol> symbols,
        Func<float, float, Point> worldToCanvas,
        float padMetres = 2f)
    {
        ArgumentNullException.ThrowIfNull(coords);
        ArgumentNullException.ThrowIfNull(worldToCanvas);
        if (coords.Count == 0)
            return null;

        var minX = coords.Values.Min(s => s.X);
        var maxX = coords.Values.Max(s => s.X);
        var minY = coords.Values.Min(s => s.Y);
        var maxY = coords.Values.Max(s => s.Y);

        foreach (var sym in symbols)
        {
            minX = Math.Min(minX, sym.X);
            maxX = Math.Max(maxX, sym.X);
            minY = Math.Min(minY, sym.Y);
            maxY = Math.Max(maxY, sym.Y);
        }

        if (maxX - minX < 1e-6f)
        {
            minX -= 0.5f;
            maxX += 0.5f;
        }

        if (maxY - minY < 1e-6f)
        {
            minY -= 0.5f;
            maxY += 0.5f;
        }

        minX -= padMetres;
        maxX += padMetres;
        minY -= padMetres;
        maxY += padMetres;

        var nw = worldToCanvas(minX, maxY);
        var se = worldToCanvas(maxX, minY);
        var spanX = Math.Max(1e-6f, maxX - minX);
        var spanY = Math.Max(1e-6f, maxY - minY);
        var scaleX = (se.X - nw.X) / spanX;
        var scaleY = (se.Y - nw.Y) / spanY;
        var scale = (Math.Abs(scaleX) + Math.Abs(scaleY)) * 0.5;
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            scale = 1;

        return new PlanCanvasSurveyLayout(minX, maxX, minY, maxY, nw.X, nw.Y, scale);
    }
}
