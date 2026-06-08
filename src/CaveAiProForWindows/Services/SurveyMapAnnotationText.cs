using System.Globalization;

namespace CaveAiProForWindows.Services;

/// <summary>Shared leg / LRUD label formatting for 2D and 3D map overlays.</summary>
public static class SurveyMapAnnotationText
{
    public static string FormatLegAngles(float azimuth, float clino, CultureInfo inv)
    {
        var cl = clino switch
        {
            > 0.45f => "\u2197" + clino.ToString("0.#", inv) + "\u00b0",
            < -0.45f => "\u2198" + Math.Abs(clino).ToString("0.#", inv) + "\u00b0",
            _ => "\u2192",
        };
        return azimuth.ToString("0", inv) + "\u00b0 " + cl;
    }

    public static string FormatDeltaZ(double dz, CultureInfo inv)
    {
        var arrow = dz > 0 ? "\u2191" : "\u2193";
        return arrow + Math.Abs(dz).ToString("0.##", inv) + " m";
    }

    public static string FormatLrud(float l, float r, float u, float d, CultureInfo inv) =>
        "L" + l.ToString("0.#", inv) +
        " R" + r.ToString("0.#", inv) +
        " U" + u.ToString("0.#", inv) +
        " D" + d.ToString("0.#", inv);
}
