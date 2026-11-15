using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Notebook units and leg vectors. Stored shots are metres and degrees.
/// Azimuth 0 is north, 90 is east, matching Android <c>calculateCaveCoordinates</c>.
/// </summary>
public static class SurveyLegGeometry
{
    public const double MetresPerFoot = 0.3048;
    public const double DegreesPerGrad = 0.9;

    public static string NormalizeLengthUnit(string? raw)
    {
        var u = raw?.Trim().ToLowerInvariant() ?? "";
        return u is "ft" or "feet" or "foot" ? "ft" : "m";
    }

    public static string NormalizeAngleUnit(string? raw)
    {
        var u = raw?.Trim().ToLowerInvariant() ?? "";
        return u is "grad" or "grads" or "gon" ? "grad" : "deg";
    }

    public static float ToMeters(float value, string? lengthUnit) =>
        NormalizeLengthUnit(lengthUnit) == "ft" ? value * (float)MetresPerFoot : value;

    public static float ToDegrees(float value, string? angleUnit) =>
        NormalizeAngleUnit(angleUnit) == "grad" ? value * (float)DegreesPerGrad : value;

    public static float NormalizeAzimuth360(float deg)
    {
        if (float.IsNaN(deg) || float.IsInfinity(deg))
            return 0f;
        var a = deg % 360f;
        if (a < 0f)
            a += 360f;
        return a;
    }

    public static float CircularMeanDeg(float a, float b)
    {
        var ar = a * (Math.PI / 180.0);
        var br = b * (Math.PI / 180.0);
        var x = Math.Cos(ar) + Math.Cos(br);
        var y = Math.Sin(ar) + Math.Sin(br);
        if (Math.Abs(x) < 1e-9 && Math.Abs(y) < 1e-9)
            return NormalizeAzimuth360(a);
        return NormalizeAzimuth360((float)(Math.Atan2(y, x) * (180.0 / Math.PI)));
    }

    public static float ResolvedAzimuthDeg(ShotRecord shot)
    {
        var back = shot.BacksightAzimuth;
        if (back is not float bs || float.IsNaN(bs) || bs <= -900f)
            return shot.Azimuth;
        return CircularMeanDeg(shot.Azimuth, NormalizeAzimuth360(bs + 180f));
    }

    public static float ResolvedClinoDeg(ShotRecord shot)
    {
        var back = shot.BacksightClino;
        if (back is not float bs || float.IsNaN(bs) || bs <= -900f)
            return shot.Clino;
        return (shot.Clino + (-bs)) / 2f;
    }

    public static (double Dx, double Dy, double Dz) Delta(ShotRecord shot)
    {
        var az = ResolvedAzimuthDeg(shot) * (Math.PI / 180.0);
        var cl = ResolvedClinoDeg(shot) * (Math.PI / 180.0);
        var h = shot.Distance * Math.Cos(cl);
        return (h * Math.Sin(az), h * Math.Cos(az), shot.Distance * Math.Sin(cl));
    }
}
