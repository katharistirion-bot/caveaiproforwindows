using System.Globalization;

namespace CaveAiProForWindows.Services;

/// <summary>Map scale denominator (1:N) from survey layout pixels-per-metre.</summary>
public static class CartographicScaleCalculator
{
    private const double WpfDpi = 96.0;
    private const double MmPerInch = 25.4;

    /// <summary>Scale denominator for WPF logical DIP (96 dpi) output.</summary>
    public static int ComputeScaleDenominator(double pxPerMetre)
    {
        if (pxPerMetre <= 1e-9 || double.IsNaN(pxPerMetre) || double.IsInfinity(pxPerMetre))
            return 0;

        // 1 px map = (25.4/96) mm; ground metres per px = 1/pxPerMetre.
        var raw = 1000.0 * WpfDpi / (pxPerMetre * MmPerInch);
        return NiceRoundDenominator(raw);
    }

    public static string FormatScaleLabel(double pxPerMetre)
    {
        var n = ComputeScaleDenominator(pxPerMetre);
        return n > 0 ? string.Format(CultureInfo.InvariantCulture, "1:{0}", n) : "Scale n/a";
    }

    public static string FormatScaleLabelFromSpans(double spanMetres, double outputPixels)
    {
        if (spanMetres <= 1e-9 || outputPixels <= 1e-9)
            return "Scale n/a";
        return FormatScaleLabel(outputPixels / spanMetres);
    }

    public static int NiceRoundDenominator(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw))
            return 0;
        var p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var m = raw / p;
        double nice = m switch
        {
            <= 1.5 => 1,
            <= 3.5 => 2,
            <= 7.5 => 5,
            _ => 10,
        };
        return (int)Math.Round(nice * p);
    }
}
