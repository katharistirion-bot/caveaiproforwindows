namespace CaveAiProForWindows.Services;

/// <summary>
/// Same survey-bearing conventions as Android <see cref="SurveyCalculations"/> in CaveAI Pro
/// (plan: <c>atan2(dx, dy)</c>, 0° = +Y).
/// </summary>
public static class SurveyCalculations
{
    /// <summary>Compass ° in plan frame: 0° = +Y, 90° = +X (clockwise from survey “north”).</summary>
    public static double PlanBearingDegFromDelta(double dx, double dy)
    {
        if (dx * dx + dy * dy < 1e-12)
            return double.NaN;
        var deg = Math.Atan2(dx, dy) * (180.0 / Math.PI);
        if (deg < 0)
            deg += 360.0;
        return deg;
    }

    /// <summary>Smallest signed rotation from <paramref name="fromHeadingDeg"/> to <paramref name="toHeadingDeg"/> in (-180, 180].</summary>
    public static float RelativeBearingDeg(float fromHeadingDeg, float toHeadingDeg)
    {
        var d = toHeadingDeg - fromHeadingDeg;
        while (d > 180f)
            d -= 360f;
        while (d <= -180f)
            d += 360f;
        return d;
    }
}
