using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Minimal Survex <c>.svx</c> centerline from traverse legs (tape m, compass °, clino ° — same as CaveAI Pro shots).</summary>
public static class SurvexExporter
{
    public static SurvexExportOptions ResolveDefaultOptions(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var firstTraverse = project.Shots.FirstOrDefault(s => s.IsTraverseLeg);
        var fixStation = firstTraverse != null ? SanitizeStation(firstTraverse.FromStation) : "entrance";
        var declination = project.SurveyCalibrationProfile?.MagneticDeclinationAppliedDeg;

        return new SurvexExportOptions
        {
            FixStation = fixStation,
            FixEasting = 0,
            FixNorthing = 0,
            FixElevation = project.Alt,
            DeclinationDeg = declination,
            IncludeSplays = true,
            ProvisionalFix = true,
            IncludeLrudPassage = true,
            IncludeEntranceGpsCs = project.Lat is { } && project.Lon is { },
        };
    }

    public static byte[] BuildSvxUtf8Bom(CaveProjectDocument project, SurvexExportOptions? options = null)
    {
        options ??= ResolveDefaultOptions(project);
        var surveyId = SanitizeSurveyId(project.Name);
        var sb = new StringBuilder();
        sb.AppendLine("*encoding utf-8");
        sb.AppendLine($"*begin {surveyId}");
        sb.AppendLine($"*title {EscapeTitle(project.Name)}");
        if (!string.IsNullOrWhiteSpace(project.Date))
            sb.AppendLine($"*date {project.Date}");

        sb.AppendLine();
        if (options.IncludeEntranceGpsCs && project.Lat is { } gpsLat && project.Lon is { } gpsLon)
        {
            sb.AppendLine("; --- Entrance GPS (WGS84) — optional surface anchor ---");
            sb.AppendLine("; Link local survey to surface coordinates with *calibrate before merging datasets.");
            sb.AppendLine("*cs long-lat");
            sb.AppendLine(string.Join("\t",
                "*fix",
                "entrance-gps",
                gpsLon.ToString("0.######", CultureInfo.InvariantCulture),
                gpsLat.ToString("0.######", CultureInfo.InvariantCulture),
                project.Alt.ToString("0.###", CultureInfo.InvariantCulture)));
            sb.AppendLine("*cs");
            sb.AppendLine();
        }
        else if (options.ProvisionalFix)
        {
            sb.AppendLine("; --- IMPORTANT: provisional *fix (below) ---");
            sb.AppendLine("; Survex needs a coordinate anchor. Replace with a real *fix / *cs before merging with surface data.");
            sb.AppendLine($"; Android project entrance alt (JSON alt): {project.Alt.ToString("0.###", CultureInfo.InvariantCulture)} m");
            if (project.Lat is { } lat && project.Lon is { } lon)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"; Entrance GPS (informational): lat {lat:0.######} lon {lon:0.######}");
            }
        }

        if (options.DeclinationDeg is { } dec)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"; Magnetic declination applied in CaveAI Pro: {dec:0.###}°");
            sb.AppendLine(CultureInfo.InvariantCulture, $"*declination {dec:0.###}");
        }

        var anchor = SanitizeStation(options.FixStation);
        sb.AppendLine(string.Join(" ",
            "*fix",
            anchor,
            options.FixEasting.ToString("0.###", CultureInfo.InvariantCulture),
            options.FixNorthing.ToString("0.###", CultureInfo.InvariantCulture),
            options.FixElevation.ToString("0.###", CultureInfo.InvariantCulture)));

        sb.AppendLine("*units tape metres");
        sb.AppendLine("*units compass degrees");
        sb.AppendLine("*units clino degrees");
        sb.AppendLine();
        sb.AppendLine("; Traverse legs (toStation != \"-\").");
        sb.AppendLine("*data normal from to tape compass clino");

        foreach (var s in project.Shots.Where(x => x.IsTraverseLeg))
        {
            var from = SanitizeStation(s.FromStation);
            var to = SanitizeStation(s.ToStation);
            sb.AppendLine(string.Join("\t",
                from,
                to,
                s.Distance.ToString("0.###", CultureInfo.InvariantCulture),
                s.Azimuth.ToString(CultureInfo.InvariantCulture),
                s.Clino.ToString(CultureInfo.InvariantCulture)));
        }

        var splays = project.Shots.Where(x => !x.IsTraverseLeg && x.Distance > 0).ToList();
        if (options.IncludeSplays && splays.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"; Splay shots ({splays.Count}) — toStation \"-\" in CaveAI Pro.");
            sb.AppendLine("*data normal from to tape compass clino");
            foreach (var s in splays)
            {
                sb.AppendLine(string.Join("\t",
                    SanitizeStation(s.FromStation),
                    "-",
                    s.Distance.ToString("0.###", CultureInfo.InvariantCulture),
                    s.Azimuth.ToString(CultureInfo.InvariantCulture),
                    s.Clino.ToString(CultureInfo.InvariantCulture)));
            }

        }

        if (options.IncludeLrudPassage)
            AppendLrudPassageBlock(sb, project);

        sb.AppendLine();
        sb.AppendLine($"*end {surveyId}");

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var outBytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, outBytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, outBytes, preamble.Length, body.Length);
        return outBytes;
    }

    private static string SanitizeSurveyId(string name)
    {
        var s = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_]+", "_", RegexOptions.None);
        if (string.IsNullOrEmpty(s)) s = "cave";
        if (char.IsDigit(s[0])) s = "s_" + s;
        return s.ToLowerInvariant();
    }

    private static string SanitizeStation(string raw)
    {
        var s = raw.Trim().Replace(' ', '_');
        s = Regex.Replace(s, @"[^a-zA-Z0-9._-]", "_");
        return string.IsNullOrEmpty(s) ? "?" : s;
    }

    private static string EscapeTitle(string name)
    {
        return name.Replace("\"", "''", StringComparison.Ordinal);
    }

    private static void AppendLrudPassageBlock(StringBuilder sb, CaveProjectDocument project)
    {
        var walks = SurveyLrudWallGeometry.GetOrderedTraverseWalks(project.Shots);
        if (walks.Count == 0)
            return;

        var stationRows = new List<(string Station, float L, float R, float U, float D)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var walk in walks)
        {
            foreach (var (from, _, shot) in walk)
            {
                if (!seen.Add(from))
                    continue;
                var (l, r, u, d) = shot.EffectivePlanLrud();
                if (l <= 0 && r <= 0 && u <= 0 && d <= 0)
                    continue;
                stationRows.Add((from, l, r, u, d));
            }

            var last = walk[^1];
            if (seen.Add(last.wt))
            {
                var (l, r, u, d) = last.sh.EffectivePlanLrud();
                if (l > 0 || r > 0 || u > 0 || d > 0)
                    stationRows.Add((last.wt, l, r, u, d));
            }
        }

        if (stationRows.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"; LRUD passage walls ({stationRows.Count} stations) — Survex *data passage from CaveAI Pro LRUD.");
        sb.AppendLine("*units left right up down metres");
        sb.AppendLine("*data passage station left right up down");
        foreach (var (station, l, r, u, d) in stationRows)
        {
            sb.AppendLine(string.Join("\t",
                SanitizeStation(station),
                l.ToString("0.###", CultureInfo.InvariantCulture),
                r.ToString("0.###", CultureInfo.InvariantCulture),
                u.ToString("0.###", CultureInfo.InvariantCulture),
                d.ToString("0.###", CultureInfo.InvariantCulture)));
        }
    }
}
