using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Minimal Survex <c>.svx</c> centerline from traverse legs (tape m, compass °, clino ° — same as CaveAI Pro shots).</summary>
public static class SurvexExporter
{
    public static byte[] BuildSvxUtf8Bom(CaveProjectDocument project)
    {
        var surveyId = SanitizeSurveyId(project.Name);
        var sb = new StringBuilder();
        sb.AppendLine("*encoding utf-8");
        sb.AppendLine($"*begin {surveyId}");
        sb.AppendLine($"*title {EscapeTitle(project.Name)}");
        if (!string.IsNullOrWhiteSpace(project.Date))
            sb.AppendLine($"*date {project.Date}");
        sb.AppendLine();
        sb.AppendLine("; Traverse legs from CAVE AI PRO (toStation != \"-\"). Tape m, compass °, clino °.");
        sb.AppendLine("; --- IMPORTANT: temporary *fix (below) ---");
        sb.AppendLine("; Survex needs a coordinate anchor. This export uses *fix <first-from-station> 0 0 0 so the network is");
        sb.AppendLine("; consistent with the PC plan view. It is NOT tied to GPS or national grid.");
        sb.AppendLine("; Before merging with surface data or publishing: replace with a real *fix / *cs / *declination as you use in your workflow.");
        sb.AppendLine($"; Android project entrance alt (JSON alt): {project.Alt.ToString("0.###", CultureInfo.InvariantCulture)} m (informational only — not written as *fix).");
        var firstTraverse = project.Shots.FirstOrDefault(s => s.IsTraverseLeg);
        if (firstTraverse != null)
        {
            var anchor = SanitizeStation(firstTraverse.FromStation);
            sb.AppendLine($"*fix {anchor} 0 0 0");
        }

        // Explicit units guarantee Survex / Cavern interprets the columns the same way the Android edition recorded
        // them, regardless of any *calibrate or non-default site settings the user may add later.
        sb.AppendLine("*units tape metres");
        sb.AppendLine("*units compass degrees");
        sb.AppendLine("*units clino degrees");
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
}
