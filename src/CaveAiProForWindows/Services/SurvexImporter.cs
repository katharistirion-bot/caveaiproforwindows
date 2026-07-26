using System.Globalization;
using System.IO;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Imports Survex centerline <c>.svx</c> into a CaveAI project (round-trip with <see cref="SurvexExporter"/>).
/// Supports <c>*data normal from to tape compass clino</c> (+ optional LRUD columns). Not a full Survex parser.
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
        var name = string.IsNullOrWhiteSpace(fallbackName) ? "Imported Survex survey" : fallbackName.Trim();
        var date = "";
        var tapeMetres = true;
        string[]? columns = null;
        var inData = false;
        var traverse = 0;
        var splays = 0;

        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('*'))
            {
                inData = false;
                columns = null;
                var tokens = SplitTokens(line);
                if (tokens.Count == 0)
                    continue;
                var cmd = tokens[0].ToLowerInvariant();
                switch (cmd)
                {
                    case "*begin":
                        if (tokens.Count >= 2 && name == "Imported Survex survey")
                            name = HumanizeSurveyId(tokens[1]);
                        break;
                    case "*title":
                        var title = line.Length > 6 ? line[6..].Trim().Trim('"') : "";
                        if (!string.IsNullOrWhiteSpace(title))
                            name = title;
                        break;
                    case "*date":
                        if (tokens.Count >= 2)
                            date = tokens[1];
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
                    default:
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
            var shot = new ShotRecord
            {
                FromStation = from,
                ToStation = to,
                Distance = tape,
                Azimuth = NormalizeAzimuth(compass),
                Clino = clino,
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

        var project = new CaveProjectDocument
        {
            Name = name,
            Date = date,
            Shots = shots,
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