using System.Globalization;
using System.IO;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Imports Survex centerline <c>.svx</c> into a CaveAI project (round-trip with <see cref="SurvexExporter"/>).
/// Supports <c>*data normal from to tape compass clino</c>, <c>*equate</c>, <c>*fix</c>, and sticky <c>*flags</c>.
/// </summary>
public static class SurvexImporter
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
        var name = string.IsNullOrWhiteSpace(fallbackName) ? "Imported Survex survey" : fallbackName.Trim();
        var date = "";
        double? lat = null;
        double? lon = null;
        var alt = 0.0;
        var tapeMetres = true;
        string[]? columns = null;
        var inData = false;
        var csLongLat = false;
        var flagSurface = false;
        var flagDuplicate = false;
        var flagSplay = false;
        var traverse = 0;
        var splays = 0;

        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var commentFlags = ParseCommentLegFlags(rawLine);
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('*'))
            {
                var tokens = SplitTokens(line);
                if (tokens.Count == 0)
                    continue;
                var cmd = tokens[0].ToLowerInvariant();
                switch (cmd)
                {
                    case "*begin":
                        inData = false;
                        columns = null;
                        if (tokens.Count >= 2 && name == "Imported Survex survey")
                            name = HumanizeSurveyId(tokens[1]);
                        break;
                    case "*end":
                        inData = false;
                        columns = null;
                        break;
                    case "*title":
                        inData = false;
                        columns = null;
                        var title = line.Length > 6 ? line[6..].Trim().Trim('"') : "";
                        if (!string.IsNullOrWhiteSpace(title))
                            name = title;
                        break;
                    case "*date":
                        inData = false;
                        columns = null;
                        if (tokens.Count >= 2)
                            date = tokens[1];
                        break;
                    case "*cs":
                        if (tokens.Count >= 2 && tokens[1].Equals("long-lat", StringComparison.OrdinalIgnoreCase))
                            csLongLat = true;
                        else
                            csLongLat = false;
                        break;
                    case "*units":
                        if (tokens.Count >= 3 && tokens[1].Equals("tape", StringComparison.OrdinalIgnoreCase))
                        {
                            var unit = tokens[2].ToLowerInvariant();
                            if (unit.StartsWith("feet", StringComparison.Ordinal) || unit is "ft" or "foot")
                            {
                                tapeMetres = false;
                                warnings.Add("Tape units are feet — converted to metres on import (×0.3048).");
                            }
                            else if (!unit.StartsWith("metre", StringComparison.Ordinal) && unit is not "m" and not "meters")
                                warnings.Add($"Unrecognized tape unit '{tokens[2]}' — assumed metres.");
                        }
                        break;
                    case "*data":
                        columns = null;
                        inData = false;
                        if (tokens.Count >= 2 && tokens[1].Equals("normal", StringComparison.OrdinalIgnoreCase))
                        {
                            columns = tokens.Skip(2).Select(t => t.ToLowerInvariant()).ToArray();
                            inData = columns.Length >= 5
                                && columns[0] == "from"
                                && columns[1] == "to"
                                && columns[2] == "tape"
                                && columns[3] == "compass"
                                && columns[4] == "clino";
                            if (!inData)
                                warnings.Add($"Skipped unsupported *data order: {string.Join(' ', columns ?? Array.Empty<string>())}");
                        }
                        break;
                    case "*flags":
                        ApplySurvexFlags(tokens.Skip(1), ref flagSurface, ref flagDuplicate, ref flagSplay);
                        break;
                    case "*equate":
                        if (tokens.Count >= 3)
                        {
                            var root = tokens[1].Trim();
                            for (var i = 2; i < tokens.Count; i++)
                            {
                                var b = tokens[i].Trim();
                                if (root.Length == 0 || b.Length == 0 || root.Equals(b, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                equates.Add(new StationEquate { StationA = root, StationB = b });
                            }
                        }
                        break;
                    case "*fix":
                    {
                        var idx = 1;
                        if (tokens.Count > idx && tokens[idx].Equals("reference", StringComparison.OrdinalIgnoreCase))
                            idx++;
                        if (tokens.Count < idx + 4)
                            break;
                        var st = tokens[idx];
                        if (!TryParseFloat(tokens[idx + 1], out var fx)
                            || !TryParseFloat(tokens[idx + 2], out var fy)
                            || !TryParseFloat(tokens[idx + 3], out var fz))
                            break;
                        if (csLongLat)
                        {
                            // Survex long-lat *fix is longitude latitude altitude.
                            lon = fx;
                            lat = fy;
                            alt = fz;
                        }
                        else
                        {
                            fixedPositions[st] = new PlanStationPositionOverride(fx, fy, fz);
                            if (!fixedNames.Any(n => n.Equals(st, StringComparison.OrdinalIgnoreCase)))
                                fixedNames.Add(st);
                        }
                        break;
                    }
                    default:
                        // Unknown starred command — leave data state as-is for sticky flags compatibility.
                        break;
                }
                continue;
            }

            if (!inData || columns == null)
                continue;

            var fields = SplitTokens(line);
            if (fields.Count < 5)
            {
                warnings.Add($"Skipped short data line: {line}");
                continue;
            }

            if (!TryParseFloat(fields[2], out var tape)
                || !TryParseFloat(fields[3], out var compass)
                || !TryParseFloat(fields[4], out var clino))
            {
                warnings.Add($"Skipped unreadable data line: {line}");
                continue;
            }

            if (!tapeMetres)
                tape *= 0.3048f;

            var from = fields[0];
            var to = fields[1];
            if (flagSplay && !ShotRecord.IsSplayDestination(to))
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

            if (columns.Length >= 9
                && fields.Count >= 9
                && TryParseFloat(fields[5], out var left)
                && TryParseFloat(fields[6], out var right)
                && TryParseFloat(fields[7], out var up)
                && TryParseFloat(fields[8], out var down))
            {
                shot.L = left;
                shot.R = right;
                shot.U = up;
                shot.D = down;
            }

            shots.Add(shot);
            if (shot.IsTraverseLeg)
                traverse++;
            else if (shot.Distance > 0)
                splays++;
        }

        if (shots.Count == 0)
            throw new InvalidOperationException(
                "No Survex centerline legs found. Expected *data normal from to tape compass clino blocks.");

        // Deduplicate equate pairs from the multi-name and pairwise paths.
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
        var fallback = Path.GetFileNameWithoutExtension(path);
        return Parse(text, fallback);
    }

    private static void ApplySurvexFlags(
        IEnumerable<string> tokens,
        ref bool surface,
        ref bool duplicate,
        ref bool splay)
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
                case "splay":
                    splay = !not;
                    break;
            }
            not = false;
        }
    }

    private static string? ParseCommentLegFlags(string rawLine)
    {
        var i = rawLine.IndexOf(';');
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
        var i = line.IndexOf(';');
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

    private static float NormalizeAzimuth(float deg)
    {
        var a = deg % 360f;
        if (a < 0)
            a += 360f;
        return a;
    }

    private static string HumanizeSurveyId(string id)
    {
        var s = id.Replace('_', ' ').Trim();
        return string.IsNullOrEmpty(s) ? "Imported Survex survey" : s;
    }
}
