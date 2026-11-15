using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Imports Therion centerline <c>.th</c> into a CaveAI project (round-trip with <see cref="TherionExporter"/>).
/// Supports <c>data normal from to tape compass clino</c>, <c>equate</c>, <c>fix</c>, and sticky <c>flags</c>.
/// </summary>
public static class TherionImporter
{
    public sealed class Result
    {
        public required CaveProjectDocument Project { get; init; }
        public int TraverseLegs { get; init; }
        public int SplayLegs { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public static Result Parse(string text, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var warnings = new List<string>();
        var shots = new List<ShotRecord>();
        var equates = new List<StationEquate>();
        var fixedPositions = new Dictionary<string, PlanStationPositionOverride>(StringComparer.OrdinalIgnoreCase);
        var fixedNames = new List<string>();
        var name = string.IsNullOrWhiteSpace(fallbackName) ? "Imported Therion survey" : fallbackName.Trim();
        var date = "";
        double? lat = null;
        double? lon = null;
        var alt = 0.0;
        var inCenterline = false;
        var expectingData = false;
        var flagSurface = false;
        var flagDuplicate = false;
        var traverse = 0;
        var splays = 0;

        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmedRaw = rawLine.Trim();
            if (trimmedRaw.StartsWith("# Date:", StringComparison.OrdinalIgnoreCase)
                || trimmedRaw.StartsWith("# Date ", StringComparison.OrdinalIgnoreCase))
            {
                var colon = trimmedRaw.IndexOf(':');
                var d = colon >= 0 ? trimmedRaw[(colon + 1)..].Trim() : trimmedRaw[6..].Trim();
                if (!string.IsNullOrWhiteSpace(d))
                    date = d;
            }

            var commentFlags = ParseCommentLegFlags(rawLine);
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            var lower = line.ToLowerInvariant();
            if (lower.StartsWith("survey ", StringComparison.Ordinal))
            {
                var titleMatch = Regex.Match(line, "-title\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
                if (titleMatch.Success && !string.IsNullOrWhiteSpace(titleMatch.Groups[1].Value))
                    name = titleMatch.Groups[1].Value.Trim();
                else if (name == "Imported Therion survey")
                {
                    var parts = SplitTokens(line);
                    if (parts.Count >= 2)
                        name = HumanizeId(parts[1]);
                }
                continue;
            }

            if (lower.StartsWith("centerline", StringComparison.Ordinal))
            {
                inCenterline = true;
                expectingData = false;
                flagSurface = false;
                flagDuplicate = false;
                continue;
            }

            if (lower.StartsWith("endcenterline", StringComparison.Ordinal) || lower == "endcentreline")
            {
                inCenterline = false;
                expectingData = false;
                flagSurface = false;
                flagDuplicate = false;
                continue;
            }

            if (!inCenterline)
                continue;

            var tokens = SplitTokens(line);
            if (tokens.Count == 0)
                continue;
            var cmd = tokens[0].ToLowerInvariant();

            if (cmd == "data" && tokens.Count >= 6
                && tokens[1].Equals("normal", StringComparison.OrdinalIgnoreCase)
                && tokens[2].Equals("from", StringComparison.OrdinalIgnoreCase)
                && tokens[3].Equals("to", StringComparison.OrdinalIgnoreCase)
                && tokens[4].Equals("tape", StringComparison.OrdinalIgnoreCase)
                && tokens[5].Equals("compass", StringComparison.OrdinalIgnoreCase))
            {
                expectingData = true;
                continue;
            }

            if (cmd == "flags")
            {
                ApplyTherionFlags(tokens.Skip(1), ref flagSurface, ref flagDuplicate);
                continue;
            }

            if (cmd == "equate" && tokens.Count >= 3)
            {
                var root = tokens[1].Trim();
                for (var i = 2; i < tokens.Count; i++)
                {
                    var b = tokens[i].Trim();
                    if (root.Length == 0 || b.Length == 0 || root.Equals(b, StringComparison.OrdinalIgnoreCase))
                        continue;
                    equates.Add(new StationEquate { StationA = root, StationB = b });
                }
                continue;
            }

            if (cmd is "units" or "cs" or "mark" or "station" or "extend" or "date" or "team" or "explo-date")
                continue;

            if (cmd == "fix" && tokens.Count >= 5
                && TryParseDouble(tokens[2], out var fx)
                && TryParseDouble(tokens[3], out var fy)
                && TryParseDouble(tokens[4], out var fz))
            {
                var st = tokens[1];
                if (LooksLikeGeographicFix(fx, fy) && lat is null && lon is null)
                {
                    // CaveAI Therion export writes fix entrance lat lon alt.
                    lat = fx;
                    lon = fy;
                    alt = fz;
                }
                else
                {
                    fixedPositions[st] = new PlanStationPositionOverride((float)fx, (float)fy, (float)fz);
                    if (!fixedNames.Any(n => n.Equals(st, StringComparison.OrdinalIgnoreCase)))
                        fixedNames.Add(st);
                }
                continue;
            }

            if (!expectingData || tokens.Count < 5)
                continue;

            var from = tokens[0];
            var to = tokens[1];
            if (!TryParseFloat(tokens[2], out var tape)
                || !TryParseFloat(tokens[3], out var compass)
                || !TryParseFloat(tokens[4], out var clino))
            {
                warnings.Add($"Skipped unreadable centerline leg: {line}");
                continue;
            }

            if (to == "." || to == "-" || to.Equals("splay", StringComparison.OrdinalIgnoreCase))
                to = "-";

            var flagParts = new List<string>();
            if (flagSurface) flagParts.Add(SurveyLegFlags.Surface);
            if (flagDuplicate) flagParts.Add(SurveyLegFlags.Duplicate);
            if (!string.IsNullOrEmpty(commentFlags))
            {
                foreach (var part in commentFlags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!flagParts.Any(f => f.Equals(part, StringComparison.OrdinalIgnoreCase)))
                        flagParts.Add(part.ToUpperInvariant());
                }
            }

            var shot = new ShotRecord
            {
                FromStation = from,
                ToStation = to,
                Distance = tape,
                Azimuth = NormalizeAzimuth(compass),
                Clino = clino,
                LegFlags = flagParts.Count > 0 ? string.Join(' ', flagParts) : null,
            };
            shots.Add(shot);
            if (shot.IsTraverseLeg)
                traverse++;
            else if (shot.Distance > 0)
                splays++;
        }

        if (shots.Count == 0)
            throw new InvalidOperationException(
                "No Therion centerline legs found. Expected centerline / data normal from to tape compass clino.");

        var uniqueEquates = new List<StationEquate>();
        foreach (var eq in equates)
        {
            if (uniqueEquates.Any(e =>
                    (e.StationA.Equals(eq.StationA, StringComparison.OrdinalIgnoreCase) && e.StationB.Equals(eq.StationB, StringComparison.OrdinalIgnoreCase))
                    || (e.StationA.Equals(eq.StationB, StringComparison.OrdinalIgnoreCase) && e.StationB.Equals(eq.StationA, StringComparison.OrdinalIgnoreCase))))
                continue;
            uniqueEquates.Add(eq);
        }

        var project = new CaveProjectDocument
        {
            Name = name,
            Date = date,
            Lat = lat,
            Lon = lon,
            Alt = alt,
            Shots = shots,
            StationEquates = uniqueEquates.Count > 0 ? uniqueEquates : null,
            FixedStations = fixedNames.Count > 0 ? fixedNames : null,
            FixedStationPositions = fixedPositions.Count > 0 ? fixedPositions : null,
        };
        return new Result
        {
            Project = project,
            TraverseLegs = traverse,
            SplayLegs = splays,
            Warnings = warnings,
        };
    }

    public static Result ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        var text = DecodeUtf8PossiblyBom(bytes);
        return Parse(text, Path.GetFileNameWithoutExtension(path));
    }

    private static bool LooksLikeGeographicFix(double a, double b)
    {
        if (Math.Abs(a) > 90.0 || Math.Abs(b) > 180.0)
            return false;
        if (Math.Abs(a) < 0.01 && Math.Abs(b) < 0.01)
            return false;
        // Integer-ish small values are treated as plan metres (CaveAI fixed XYZ), not WGS84.
        var aInt = Math.Abs(a - Math.Round(a)) < 1e-6;
        var bInt = Math.Abs(b - Math.Round(b)) < 1e-6;
        if (aInt && bInt && Math.Abs(a) < 80.0 && Math.Abs(b) < 80.0)
            return false;
        return true;
    }

    private static void ApplyTherionFlags(IEnumerable<string> tokens, ref bool surface, ref bool duplicate)
    {
        var not = false;
        foreach (var raw in tokens)
        {
            var t = raw.ToLowerInvariant();
            if (t == "not")
            {
                not = true;
                continue;
            }
            switch (t)
            {
                case "surface":
                    surface = !not;
                    break;
                case "duplicate":
                    duplicate = !not;
                    break;
            }
            not = false;
        }
    }

    private static string? ParseCommentLegFlags(string rawLine)
    {
        var i = rawLine.IndexOf('#');
        if (i < 0) return null;
        var comment = rawLine[(i + 1)..];
        var parts = new List<string>();
        foreach (var tok in comment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var u = tok.Trim().ToUpperInvariant();
            if (u is "NOPLOT" or "DONTADJUST" or "EXCLUDE" or "SURFACE" or "DUPLICATE")
                parts.Add(u);
        }
        return parts.Count > 0 ? string.Join(' ', parts) : null;
    }

    private static string DecodeUtf8PossiblyBom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string StripComment(string line)
    {
        var i = line.IndexOf('#');
        return i < 0 ? line : line[..i];
    }

    private static List<string> SplitTokens(string line)
    {
        var parts = new List<string>();
        foreach (var chunk in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            parts.Add(chunk);
        return parts;
    }

    private static bool TryParseFloat(string raw, out float value) =>
        float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDouble(string raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static float NormalizeAzimuth(float deg)
    {
        var a = deg % 360f;
        if (a < 0) a += 360f;
        return a;
    }

    private static string HumanizeId(string id) =>
        id.Replace('_', ' ').Trim() is { Length: > 0 } s ? s : "Imported Therion survey";
}
