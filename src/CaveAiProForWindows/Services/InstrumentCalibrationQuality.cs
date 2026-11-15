using System.Globalization;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>English lines for a DistoX fit stored on <see cref="CaveProjectDocument.SurveyCalibrationProfile"/>.</summary>
public static class InstrumentCalibrationQuality
{
    public static string? FormatLine(SurveyCalibrationProfileSnapshot? profile)
    {
        if (profile?.MeanGravityError is not float gravity || profile.MeanMagneticError is not float magnetic)
            return null;

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("DistoX calibration: gravity error ");
        sb.Append(gravity.ToString("0.###", inv));
        sb.Append(", compass error ");
        sb.Append(magnetic.ToString("0.###", inv));
        sb.Append(". ");
        sb.Append(DescribeFit(profile.FitQuality));
        sb.Append('.');
        if (profile.CalibratedAtUtcMs is long ms && ms > 0)
        {
            var local = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            sb.Append(" Recorded ");
            sb.Append(local.ToString("g", CultureInfo.CurrentCulture));
            sb.Append('.');
        }

        return sb.ToString();
    }

    public static bool IsWeak(SurveyCalibrationProfileSnapshot? profile) =>
        string.Equals(profile?.FitQuality?.Trim(), "weak", StringComparison.OrdinalIgnoreCase);

    private static string DescribeFit(string? token) =>
        token?.Trim().ToLowerInvariant() switch
        {
            "good" => "Good fit",
            "usable" => "Usable fit",
            "weak" => "Weak fit",
            null or "" => "Fit not rated",
            _ => token.Trim(),
        };
}
