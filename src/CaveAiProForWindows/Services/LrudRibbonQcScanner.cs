using System.Globalization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public enum LrudRibbonQcKind
{
    MissingLrud,
    ZeroWidthPassage,
    DegenerateWall,
}

public sealed record LrudRibbonQcHighlight(
    LrudRibbonQcKind Kind,
    string FromStation,
    string ToStation,
    float MidX,
    float MidY,
    string Label);

/// <summary>Detects LRUD ribbon / wall geometry issues for plan/section QC overlays.</summary>
public static class LrudRibbonQcScanner
{
    private const float LrudEps = 1e-3f;
    private const float MinLegMetres = 1.5f;
    private const float WallSpanMismatchRatio = 2.5f;

    public static IReadOnlyList<LrudRibbonQcHighlight> Scan(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        IReadOnlyList<SurveyStationGeometry.PlanVectorPolyline> wallPolylines,
        float minX,
        float maxX,
        float minY,
        float maxY)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(coords);

        var inv = CultureInfo.InvariantCulture;
        var highlights = new List<LrudRibbonQcHighlight>();

        foreach (var shot in project.Shots.Where(s => s.IsTraverseLeg))
        {
            var from = shot.FromStation.Trim();
            var to = shot.ToStation.Trim();
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                continue;
            if (!coords.TryGetValue(from, out var a) || !coords.TryGetValue(to, out var b))
                continue;

            var (lrL, lrR, _, _) = shot.EffectivePlanLrud();
            var midX = (a.X + b.X) * 0.5f;
            var midY = (a.Y + b.Y) * 0.5f;
            var legLen = shot.Distance;

            if (legLen >= MinLegMetres && lrL < LrudEps && lrR < LrudEps)
            {
                highlights.Add(new LrudRibbonQcHighlight(
                    LrudRibbonQcKind.MissingLrud,
                    from,
                    to,
                    midX,
                    midY,
                    $"missing LRUD ({legLen.ToString("0.#", inv)} m leg)"));
                continue;
            }

            if (legLen >= MinLegMetres && lrL + lrR < LrudEps)
            {
                highlights.Add(new LrudRibbonQcHighlight(
                    LrudRibbonQcKind.ZeroWidthPassage,
                    from,
                    to,
                    midX,
                    midY,
                    "zero-width passage"));
                continue;
            }

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var planLen = Math.Sqrt(dx * (double)dx + dy * (double)dy);
            if (planLen >= MinLegMetres && legLen > LrudEps &&
                (planLen > legLen * WallSpanMismatchRatio || legLen > planLen * WallSpanMismatchRatio))
            {
                highlights.Add(new LrudRibbonQcHighlight(
                    LrudRibbonQcKind.MissingLrud,
                    from,
                    to,
                    midX,
                    midY,
                    $"leg/wall span mismatch ({legLen.ToString("0.#", inv)} m vs {planLen.ToString("0.#", inv)} m)"));
            }
        }

        var cx = (minX + maxX) * 0.5f;
        var cy = (minY + maxY) * 0.5f;
        foreach (var pl in wallPolylines)
        {
            if (pl.Points.Count >= 2)
                continue;
            var (mx, my) = pl.Points.Count == 1 ? pl.Points[0] : (cx, cy);
            highlights.Add(new LrudRibbonQcHighlight(
                LrudRibbonQcKind.DegenerateWall,
                "",
                "",
                mx,
                my,
                $"degenerate wall ({pl.Type}, {pl.Points.Count} pt)"));
        }

        return highlights;
    }
}
