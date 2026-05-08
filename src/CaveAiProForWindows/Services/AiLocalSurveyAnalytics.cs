using System.Globalization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Offline-only analytics for <see cref="Views.AiAnalyticsView"/> (LRUD volume, shot QC variance, traverse endpoints).</summary>
public static class AiLocalSurveyAnalytics
{
    /// <summary>Standard deviation threshold in degrees (variance threshold is this squared).</summary>
    public const float CompassClinoStdThresholdDeg = 2f;

    public static IReadOnlyList<AiAnalyticsMetricRow> BuildResultRows(string toolTag, CaveProjectDocument? project)
    {
        var inv = CultureInfo.InvariantCulture;
        if (project == null)
        {
            return new[]
            {
                new AiAnalyticsMetricRow { Metric = "Status", Value = "No cave project loaded — open a backup and select a project." },
            };
        }

        return toolTag switch
        {
            "Volume" => BuildVolumeRows(project, inv),
            "Lead" => BuildLeadRows(project, inv),
            _ => BuildQcRows(project, inv),
        };
    }

    private static IReadOnlyList<AiAnalyticsMetricRow> BuildVolumeRows(CaveProjectDocument project, CultureInfo inv)
    {
        double sumM3 = 0;
        var used = 0;
        var skipped = 0;
        foreach (var s in project.Shots)
        {
            if (!s.IsTraverseLeg)
                continue;
            var d = (double)s.Distance;
            if (d <= 0)
            {
                skipped++;
                continue;
            }

            var (l, r, u, dd) = s.EffectivePlanLrud();
            var w = (double)l + (double)r;
            var h = (double)u + (double)dd;
            if (w <= 0 || h <= 0)
            {
                skipped++;
                continue;
            }

            // Simplified rectangular prism along the leg: cross-section (L+R)×(U+D), length = shot distance (m).
            sumM3 += w * h * d;
            used++;
        }

        return new[]
        {
            new AiAnalyticsMetricRow
            {
                Metric = "Total passage estimate (LRUD × leg)",
                Value = $"{sumM3.ToString("N1", inv)} m³",
            },
            new AiAnalyticsMetricRow { Metric = "Traverse legs included", Value = used.ToString(inv) },
            new AiAnalyticsMetricRow { Metric = "Legs skipped (zero length or zero LRUD)", Value = skipped.ToString(inv) },
            new AiAnalyticsMetricRow
            {
                Metric = "Method",
                Value = "Per-leg prism volume = (L+R)×(U+D)×distance using EffectivePlanLrud (classic LRUD or Splay Watch radials).",
            },
        };
    }

    private static IReadOnlyList<AiAnalyticsMetricRow> BuildQcRows(CaveProjectDocument project, CultureInfo inv)
    {
        var critical = 0;
        var withAnySensor = 0;
        foreach (var s in project.Shots)
        {
            if (!s.IsTraverseLeg)
                continue;

            var compassStd = EffectiveStdDeg(s.CompassStdDeg, s.CompassSampleVarianceDeg2);
            var clinoStd = EffectiveStdDeg(s.ClinoStdDeg, s.ClinoSampleVarianceDeg2);
            if (compassStd == null && clinoStd == null)
                continue;
            withAnySensor++;

            var badCompass = compassStd.HasValue && compassStd.Value > CompassClinoStdThresholdDeg;
            var badClino = clinoStd.HasValue && clinoStd.Value > CompassClinoStdThresholdDeg;
            if (badCompass || badClino)
                critical++;
        }

        return new[]
        {
            new AiAnalyticsMetricRow
            {
                Metric = "Critical Measurement Warnings",
                Value = critical.ToString(inv),
            },
            new AiAnalyticsMetricRow
            {
                Metric = "Traverse legs with compass/clino QC fields",
                Value = withAnySensor.ToString(inv),
            },
            new AiAnalyticsMetricRow
            {
                Metric = "Threshold",
                Value =
                    $"Standard deviation > {CompassClinoStdThresholdDeg.ToString(inv)}° (compassStdDeg/clinoStdDeg, else √(compass/clino sample variance in deg²)).",
            },
        };
    }

    private static float? EffectiveStdDeg(float? explicitStdDeg, float? sampleVarianceDeg2)
    {
        if (explicitStdDeg is > 0)
            return explicitStdDeg;
        if (sampleVarianceDeg2 is > 0)
            return (float)Math.Sqrt((double)sampleVarianceDeg2.Value);
        return null;
    }

    private static IReadOnlyList<AiAnalyticsMetricRow> BuildLeadRows(CaveProjectDocument project, CultureInfo inv)
    {
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void AddEdge(string a, string b)
        {
            a = a.Trim();
            b = b.Trim();
            if (a.Length == 0 || b.Length == 0)
                return;
            if (!adj.TryGetValue(a, out var setA))
            {
                setA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adj[a] = setA;
            }

            if (!adj.TryGetValue(b, out var setB))
            {
                setB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adj[b] = setB;
            }

            setA.Add(b);
            setB.Add(a);
        }

        foreach (var s in project.Shots)
        {
            if (!s.IsTraverseLeg)
                continue;
            if (s.Distance <= 0)
                continue;
            AddEdge(s.FromStation, s.ToStation);
        }

        var leads = new List<string>();
        foreach (var kv in adj)
        {
            if (kv.Value.Count != 1)
                continue;
            if (IsMarkedClosedStation(kv.Key, project))
                continue;
            leads.Add(kv.Key);
        }

        leads.Sort(StringComparer.OrdinalIgnoreCase);
        var namesJoined = leads.Count == 0
            ? "—"
            : string.Join(", ", leads);

        const int maxLen = 2000;
        if (namesJoined.Length > maxLen)
            namesJoined = namesJoined[..(maxLen - 3)] + "...";

        return new[]
        {
            new AiAnalyticsMetricRow { Metric = "Open endpoint leads (degree-1)", Value = leads.Count.ToString(inv) },
            new AiAnalyticsMetricRow { Metric = "Station names", Value = namesJoined },
            new AiAnalyticsMetricRow
            {
                Metric = "Method",
                Value = "Undirected traverse graph from legs; degree-1 stations, excluding any station with an incident traverse shot Symbol \"x\" (Therion-style closed).",
            },
        };
    }

    private static bool IsMarkedClosedStation(string station, CaveProjectDocument project)
    {
        foreach (var s in project.Shots)
        {
            if (!s.IsTraverseLeg)
                continue;
            var from = s.FromStation.Trim();
            var to = s.ToStation.Trim();
            if (!string.Equals(from, station, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(to, station, StringComparison.OrdinalIgnoreCase))
                continue;

            if ((s.Symbol ?? "").Trim().Equals("x", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
