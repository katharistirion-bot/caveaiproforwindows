using System.Globalization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Shared traverse-leg label lines for 2D maps and 3D screen chips.</summary>
public static class SurveyLegLabelFormatter
{
    public static (string Primary, string Secondary, string? Lrud, string? Tertiary) FormatLegLabel(
        ShotRecord shot,
        SurveyStationGeometry.StationPlanCoords a,
        SurveyStationGeometry.StationPlanCoords b,
        TraverseChainageResult? chainage,
        bool includeEndpointNames = true)
    {
        var inv = CultureInfo.InvariantCulture;
        var from = (shot.FromStation ?? "").Trim();
        var to = (shot.ToStation ?? "").Trim();

        string primary;
        if (includeEndpointNames && from.Length > 0 && to.Length > 0)
            primary = from + " \u2192 " + to + "  ·  " + shot.Distance.ToString("0.##", inv) + " m";
        else
            primary = shot.Distance.ToString("0.##", inv) + " m";

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var horiz = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        var secondary = SurveyMapAnnotationText.FormatLegAngles(shot.Azimuth, shot.Clino, inv);
        var dz = b.Z - a.Z;
        if (Math.Abs(dz) > 0.005)
            secondary += "  " + SurveyMapAnnotationText.FormatDeltaZ(dz, inv);
        if (Math.Abs(shot.Depth) > 1e-5f)
            secondary += "  d " + shot.Depth.ToString("0.##", inv);
        if (horiz > 0.05 && Math.Abs(dz) > 0.005)
        {
            var grade = 100.0 * dz / horiz;
            if (Math.Abs(grade) >= 1.0)
                secondary += "  " + grade.ToString("+0.#;-0.#;0", inv) + "%";
        }

        if (horiz > 0.05 && Math.Abs(horiz - shot.Distance) > 0.08)
            secondary += "  H " + horiz.ToString("0.##", inv) + " m";

        var (L, R, U, D) = shot.EffectivePlanLrud();
        string? lrud = L + R + U + D > 0.02f
            ? SurveyMapAnnotationText.FormatLrud(L, R, U, D, inv)
            : null;

        string? tertiary = null;
        if (chainage != null)
        {
            chainage.TryGetChainage(from, out var s0);
            chainage.TryGetChainage(to, out var s1);
            if (s0 > 0 || s1 > 0)
                tertiary = "S " + s0.ToString("0.##", inv) + "\u2192" + s1.ToString("0.##", inv) + " m";
        }

        return (primary, secondary, lrud, tertiary);
    }
}
