using System.Globalization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.SurveyAnalysis;

/// <summary>
/// Robust local outlier scan (median + MAD) over traverse legs and LRUD dimensions.
/// 100% offline — no external APIs.
/// </summary>
public static class SurveyAnomalyScanner
{
    /// <summary>MAD multiplier — values beyond median ± k·MAD are flagged.</summary>
    public const double DefaultMadMultiplier = 3.5;

    public static IReadOnlyList<SurveyAnomalyFinding> Scan(
        CaveProjectDocument project,
        double madMultiplier = DefaultMadMultiplier)
    {
        ArgumentNullException.ThrowIfNull(project);
        var trav = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        if (trav.Count == 0)
            return Array.Empty<SurveyAnomalyFinding>();

        var findings = new List<SurveyAnomalyFinding>();
        ScanSeries(trav, s => s.Distance, SurveyAnomalyKind.LegDistance, "m", madMultiplier, findings,
            legLabel: s => $"{s.FromStation}→{s.ToStation}");
        ScanSeries(trav, s => NormalizeAzimuth(s.Azimuth), SurveyAnomalyKind.Azimuth, "°", madMultiplier, findings,
            legLabel: s => $"{s.FromStation}→{s.ToStation}");
        ScanSeries(trav, s => s.Clino, SurveyAnomalyKind.Clino, "°", madMultiplier, findings,
            legLabel: s => $"{s.FromStation}→{s.ToStation}");

        foreach (var shot in trav.Where(s => s.L > 0 || s.R > 0 || s.U > 0 || s.D > 0))
        {
            var leg = $"{shot.FromStation}→{shot.ToStation}";
            TryFlagLrud(shot.L, SurveyAnomalyKind.LrudLeft, leg, "L", madMultiplier, findings, trav);
            TryFlagLrud(shot.R, SurveyAnomalyKind.LrudRight, leg, "R", madMultiplier, findings, trav);
            TryFlagLrud(shot.U, SurveyAnomalyKind.LrudUp, leg, "U", madMultiplier, findings, trav);
            TryFlagLrud(shot.D, SurveyAnomalyKind.LrudDown, leg, "D", madMultiplier, findings, trav);
        }

        foreach (var loop in SurveyLoopClosureAdjuster.DetectLoops(project))
        {
            if (loop.MisclosureMeters <= 0.05)
                continue;
            var z = loop.MisclosureMeters / Math.Max(0.15, loop.MeanLegLength * 0.02);
            findings.Add(new SurveyAnomalyFinding(
                SurveyAnomalyKind.LoopMisclosure,
                loop.ClosingLeg,
                loop.MisclosureMeters,
                0,
                loop.MeanLegLength,
                z,
                $"Loop misclosure {loop.MisclosureMeters.ToString("0.###", CultureInfo.InvariantCulture)} m " +
                $"({loop.StationCount} stations, {loop.TotalLegLength.ToString("0.##", CultureInfo.InvariantCulture)} m perimeter)",
                loop.MisclosureMeters > 1.0 ? SurveyAnomalySeverity.Critical
                    : loop.MisclosureMeters > 0.35 ? SurveyAnomalySeverity.Warning
                    : SurveyAnomalySeverity.Info));
        }

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => Math.Abs(f.ZScore))
            .ToList();
    }

    private static void ScanSeries(
        IReadOnlyList<ShotRecord> legs,
        Func<ShotRecord, double> selector,
        SurveyAnomalyKind kind,
        string unit,
        double madMultiplier,
        List<SurveyAnomalyFinding> sink,
        Func<ShotRecord, string> legLabel)
    {
        var values = legs.Select(selector).Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        if (values.Count < 4)
            return;

        var (median, mad) = RobustMedianAndMad(values);
        if (mad < 1e-9)
        {
            var spread = values.Max() - values.Min();
            if (spread < 1e-9)
                return;

            foreach (var leg in legs)
            {
                var v = selector(leg);
                if (Math.Abs(v - median) < spread * 0.4)
                    continue;

                sink.Add(new SurveyAnomalyFinding(
                    kind,
                    legLabel(leg),
                    v,
                    median,
                    spread,
                    Math.Abs(v - median) / spread,
                    $"{kind} {v.ToString("0.##", CultureInfo.InvariantCulture)}{unit} " +
                    $"(median {median.ToString("0.##", CultureInfo.InvariantCulture)}{unit}, spread {spread.ToString("0.##", CultureInfo.InvariantCulture)})",
                    Math.Abs(v - median) > spread * 0.75
                        ? SurveyAnomalySeverity.Critical
                        : SurveyAnomalySeverity.Warning));
            }

            return;
        }

        foreach (var leg in legs)
        {
            var v = selector(leg);
            var z = Math.Abs(v - median) / (1.4826 * mad);
            if (z < madMultiplier)
                continue;

            sink.Add(new SurveyAnomalyFinding(
                kind,
                legLabel(leg),
                v,
                median,
                1.4826 * mad,
                z,
                $"{kind} {v.ToString("0.##", CultureInfo.InvariantCulture)}{unit} " +
                $"(median {median.ToString("0.##", CultureInfo.InvariantCulture)}{unit}, robust sd≈{(1.4826 * mad).ToString("0.##", CultureInfo.InvariantCulture)})",
                z > madMultiplier * 1.4 ? SurveyAnomalySeverity.Critical : SurveyAnomalySeverity.Warning));
        }
    }

    private static void TryFlagLrud(
        float value,
        SurveyAnomalyKind kind,
        string leg,
        string dim,
        double madMultiplier,
        List<SurveyAnomalyFinding> sink,
        IReadOnlyList<ShotRecord> trav)
    {
        if (value <= 0)
            return;

        var series = trav
            .Select(s => kind switch
            {
                SurveyAnomalyKind.LrudLeft => (double)s.L,
                SurveyAnomalyKind.LrudRight => (double)s.R,
                SurveyAnomalyKind.LrudUp => (double)s.U,
                _ => (double)s.D,
            })
            .Where(v => v > 0)
            .ToList();

        if (series.Count < 4)
            return;

        var (median, mad) = RobustMedianAndMad(series);
        if (mad < 1e-9)
            return;

        var z = Math.Abs(value - median) / (1.4826 * mad);
        if (z < madMultiplier)
            return;

        sink.Add(new SurveyAnomalyFinding(
            kind,
            leg,
            value,
            median,
            1.4826 * mad,
            z,
            $"LRUD {dim}={value.ToString("0.##", CultureInfo.InvariantCulture)} m " +
            $"(median {median.ToString("0.##", CultureInfo.InvariantCulture)} m)",
            z > madMultiplier * 1.4 ? SurveyAnomalySeverity.Critical : SurveyAnomalySeverity.Warning));
    }

    internal static (double median, double mad) RobustMedianAndMad(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var median = sorted[sorted.Count / 2];
        if (sorted.Count % 2 == 0)
            median = (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) * 0.5;

        var deviations = sorted.Select(v => Math.Abs(v - median)).OrderBy(v => v).ToList();
        var mad = deviations[deviations.Count / 2];
        if (deviations.Count % 2 == 0)
            mad = (deviations[deviations.Count / 2 - 1] + deviations[deviations.Count / 2]) * 0.5;

        return (median, mad);
    }

    private static double NormalizeAzimuth(float az)
    {
        var a = az % 360f;
        if (a < 0)
            a += 360f;
        return a;
    }
}
