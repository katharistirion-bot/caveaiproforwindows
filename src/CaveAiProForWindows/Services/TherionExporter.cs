using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Therion <c>.th</c> centerline (tape m, compass °, clino °) — same conventions as Android <c>TherionMapExport.kt</c> / CaveAI shots.
/// </summary>
public static class TherionExporter
{
    public static byte[] BuildCenterlineThUtf8Bom(CaveProjectDocument project)
    {
        var surveyId = SanitizeSurveyId(project.Name);
        var title = EscapeTitle(project.Name);
        var sb = new StringBuilder();
        sb.AppendLine("encoding utf-8");
        sb.AppendLine();
        sb.AppendLine("# Cave AI Pro → Therion (centerline only; no .th2 map in this export)");
        sb.AppendLine($"# Project: {title}");
        if (!string.IsNullOrWhiteSpace(project.Date))
            sb.AppendLine($"# Date: {project.Date}");
        sb.AppendLine(
            $"# Traverse legs: {project.Shots.Count(s => s.IsTraverseLeg)}, splays: {project.Shots.Count(s => !s.IsTraverseLeg)}");
        sb.AppendLine("# Open in XTherion / therion; verify station names and fix/CS match your workflow.");
        sb.AppendLine();

        sb.AppendLine($"survey {surveyId} -title \"{title}\"");
        sb.AppendLine("  centerline");
        sb.AppendLine("    units metric");
        sb.AppendLine("    data normal from to tape compass clino");

        if (TryParseLatLon(project.Lat, project.Lon, out var lat, out var lon))
        {
            var first = project.Shots.FirstOrDefault(s => s.IsTraverseLeg);
            var entrance = first != null ? SanitizeStation(first.FromStation) : null;
            if (!string.IsNullOrEmpty(entrance) && entrance != "?")
                sb.AppendLine(
                    $"    fix {entrance} {lat.ToString(CultureInfo.InvariantCulture)} {lon.ToString(CultureInfo.InvariantCulture)} {project.Alt.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        foreach (var s in project.Shots.Where(x => x.IsTraverseLeg))
        {
            sb.AppendLine(
                $"    {SanitizeStation(s.FromStation)} {SanitizeStation(s.ToStation)} " +
                $"{s.Distance.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"{s.Azimuth.ToString(CultureInfo.InvariantCulture)} {s.Clino.ToString(CultureInfo.InvariantCulture)}");
        }

        sb.AppendLine("  endcenterline");

        var splays = project.Shots.Where(x => !x.IsTraverseLeg).ToList();
        if (splays.Count > 0)
        {
            sb.AppendLine("  centerline");
            sb.AppendLine("    data normal from to tape compass clino");
            foreach (var s in splays)
            {
                sb.AppendLine(
                    $"    {SanitizeStation(s.FromStation)} . " +
                    $"{s.Distance.ToString("0.###", CultureInfo.InvariantCulture)} " +
                    $"{s.Azimuth.ToString(CultureInfo.InvariantCulture)} {s.Clino.ToString(CultureInfo.InvariantCulture)}");
            }

            sb.AppendLine("  endcenterline");
        }

        sb.AppendLine("endsurvey");

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var outBytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, outBytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, outBytes, preamble.Length, body.Length);
        return outBytes;
    }

    private static bool TryParseLatLon(double? latVal, double? lonVal, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        if (latVal is not { } la || lonVal is not { } lo)
            return false;
        if (Math.Abs(la) < 1e-12 && Math.Abs(lo) < 1e-12)
            return false;
        lat = la;
        lon = lo;
        return true;
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

    private static string EscapeTitle(string name) =>
        name.Replace("\"", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
