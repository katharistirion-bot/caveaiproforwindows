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
            $"# Traverse legs: {project.Shots.Count(s => s.IsNetworkLeg)}, splays: {project.Shots.Count(s => !s.IsTraverseLeg)}");
        sb.AppendLine("# Open in XTherion / therion; verify station names and fix/CS match your workflow.");
        sb.AppendLine();

        sb.AppendLine($"survey {surveyId} -title \"{title}\"");
        sb.AppendLine("  centerline");
        sb.AppendLine("    units metric");
        sb.AppendLine("    data normal from to tape compass clino");

        string? entrance = null;
        if (TryParseLatLon(project.Lat, project.Lon, out var lat, out var lon))
        {
            var first = project.Shots.FirstOrDefault(s => s.IsTraverseLeg);
            entrance = first != null ? SanitizeStation(first.FromStation) : null;
            if (!string.IsNullOrEmpty(entrance) && entrance != "?")
                sb.AppendLine(
                    $"    fix {entrance} {lat.ToString(CultureInfo.InvariantCulture)} {lon.ToString(CultureInfo.InvariantCulture)} {project.Alt.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        AppendEquates(sb, project);
        AppendProjectFixedStations(sb, project, entrance);
        AppendAdjustedStationFixes(sb, project, entrance);

        var flagState = TherionFlagState.None;
        foreach (var s in project.Shots.Where(x => x.IsNetworkLeg))
        {
            flagState = EmitFlagTransition(sb, flagState, DesiredTraverseFlags(s.LegFlags));
            var note = LegFlagComment(s.LegFlags);
            var row =
                $"    {SanitizeStation(s.FromStation)} {SanitizeStation(s.ToStation)} " +
                $"{s.Distance.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"{s.Azimuth.ToString(CultureInfo.InvariantCulture)} {s.Clino.ToString(CultureInfo.InvariantCulture)}";
            sb.AppendLine(string.IsNullOrEmpty(note) ? row : row + $"  # {note}");
        }

        EmitFlagTransition(sb, flagState, TherionFlagState.None);

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

    [Flags]
    private enum TherionFlagState
    {
        None = 0,
        Surface = 1,
        Duplicate = 2,
    }

    private static TherionFlagState DesiredTraverseFlags(string? legFlags)
    {
        var state = TherionFlagState.None;
        if (SurveyLegFlags.Has(legFlags, SurveyLegFlags.Surface))
            state |= TherionFlagState.Surface;
        if (SurveyLegFlags.Has(legFlags, SurveyLegFlags.Duplicate))
            state |= TherionFlagState.Duplicate;
        return state;
    }

    private static string LegFlagComment(string? legFlags)
    {
        var notes = new List<string>();
        if (SurveyLegFlags.IsNoPlot(legFlags))
            notes.Add("NOPLOT");
        if (SurveyLegFlags.IsHeld(legFlags))
            notes.Add("DONTADJUST");
        return notes.Count == 0 ? "" : string.Join(" ", notes);
    }

    private static TherionFlagState EmitFlagTransition(StringBuilder sb, TherionFlagState current, TherionFlagState desired)
    {
        if (current == desired)
            return current;

        void Toggle(TherionFlagState bit, string name)
        {
            var was = (current & bit) != 0;
            var now = (desired & bit) != 0;
            if (was == now)
                return;
            sb.AppendLine(now ? $"    flags {name}" : $"    flags not {name}");
        }

        Toggle(TherionFlagState.Surface, "surface");
        Toggle(TherionFlagState.Duplicate, "duplicate");
        return desired;
    }

    private static void AppendEquates(StringBuilder sb, CaveProjectDocument project)
    {
        if (project.StationEquates is not { Count: > 0 })
            return;

        foreach (var eq in project.StationEquates)
        {
            var a = SanitizeStation(eq.StationA ?? "");
            var b = SanitizeStation(eq.StationB ?? "");
            if (a is "?" or "" || b is "?" or "" || a.Equals(b, StringComparison.OrdinalIgnoreCase))
                continue;
            sb.AppendLine($"    equate {a} {b}");
        }
    }

    private static void AppendProjectFixedStations(StringBuilder sb, CaveProjectDocument project, string? alreadyFixed)
    {
        var positions = project.FixedStationPositions;
        if (positions is not { Count: > 0 })
            return;

        foreach (var kv in positions.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var name = SanitizeStation(kv.Key);
            if (name is "?" or "")
                continue;
            if (alreadyFixed != null && name.Equals(alreadyFixed, StringComparison.OrdinalIgnoreCase))
                continue;
            sb.AppendLine(
                $"    fix {name} " +
                $"{kv.Value.X.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"{kv.Value.Y.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"{kv.Value.Z.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
    }

    private static void AppendAdjustedStationFixes(StringBuilder sb, CaveProjectDocument project, string? alreadyFixed)
    {
        var overrides = project.PlanStationPositionOverrides;
        if (overrides.Count == 0)
            return;

        var fixedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (project.FixedStationPositions != null)
        {
            foreach (var key in project.FixedStationPositions.Keys)
                fixedNames.Add(SanitizeStation(key));
        }

        var wroteHeader = false;
        foreach (var kv in overrides.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var name = SanitizeStation(kv.Key);
            if (name is "?" or "")
                continue;
            if (alreadyFixed != null && name.Equals(alreadyFixed, StringComparison.OrdinalIgnoreCase))
                continue;
            if (fixedNames.Contains(name))
                continue;
            if (!wroteHeader)
            {
                sb.AppendLine("    # Adjusted plan stations (metres east, north, up). Delete these fix lines to recompute from the legs.");
                wroteHeader = true;
            }
            sb.AppendLine(
                $"    fix {name} " +
                $"{kv.Value.X.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"{kv.Value.Y.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"{kv.Value.Z.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
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
