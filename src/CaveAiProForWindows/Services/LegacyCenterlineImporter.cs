using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Reads Compass .dat, Walls .srv / .wpj, and Visual Topo .tro centerlines into a CaveAI project.
/// Distances are stored in metres and angles in degrees. The notebook unit is remembered on the project
/// so the next typed shot matches the book.
/// </summary>
public static class LegacyCenterlineImporter
{
    public sealed class Result
    {
        public required CaveProjectDocument Project { get; init; }
        public int TraverseLegs { get; init; }
        public int SplayLegs { get; init; }
        public string FormatName { get; init; } = "Legacy centerline";
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public static Result ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".dat" => ParseCompass(ReadText(path), Path.GetFileNameWithoutExtension(path)),
            ".srv" => ParseWalls(ReadText(path), Path.GetFileNameWithoutExtension(path)),
            ".tro" => ParseVisualTopo(ReadText(path), Path.GetFileNameWithoutExtension(path)),
            ".wpj" => ParseWallsProject(path),
            _ => throw new InvalidOperationException(
                "Choose a Compass .dat, Walls .srv or .wpj, or Visual Topo .tro file."),
        };
    }

    public static Result ParseCompass(string text, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var warnings = new List<string>();
        var shots = new List<ShotRecord>();
        var blocks = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\f');
        string? caveName = null;
        string? date = null;
        string? lengthUnit = null;
        string? angleUnit = null;

        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            string? surveyName = null;
            double declination = 0;
            double compassCorr = 0;
            double clinoCorr = 0;
            double tapeCorr = 0;
            double backAzCorr = 0;
            double backClCorr = 0;
            var tapeFeet = true;
            var azimuthGrads = false;
            var clinoGrads = false;
            var clinoPercent = false;
            var clinoDepth = false;
            var sawFormat = false;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("SURVEY NAME:", StringComparison.OrdinalIgnoreCase))
                {
                    surveyName = AfterLabel(line, "SURVEY NAME:");
                    continue;
                }

                if (line.StartsWith("SURVEY DATE:", StringComparison.OrdinalIgnoreCase))
                {
                    date ??= ParseCompassDate(line);
                    continue;
                }

                if (line.StartsWith("SURVEY TEAM:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.StartsWith("DECLINATION:", StringComparison.OrdinalIgnoreCase))
                {
                    sawFormat = true;
                    ParseCompassDeclination(
                        line,
                        out declination,
                        out compassCorr,
                        out clinoCorr,
                        out tapeCorr,
                        out backAzCorr,
                        out backClCorr,
                        out tapeFeet,
                        out azimuthGrads,
                        out clinoGrads,
                        out clinoPercent,
                        out clinoDepth);
                    lengthUnit ??= tapeFeet ? "ft" : "m";
                    if (azimuthGrads || clinoGrads)
                        angleUnit = "grad";
                    continue;
                }

                if (!sawFormat)
                {
                    if (caveName == null && !line.StartsWith("FROM", StringComparison.OrdinalIgnoreCase))
                        caveName = line.Trim();
                    continue;
                }

                if (line.StartsWith("FROM", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.StartsWith("---", StringComparison.Ordinal) || line.All(c => c == '-' || c == ' '))
                    continue;

                if (!TryParseCompassShot(
                        line,
                        surveyName,
                        declination,
                        compassCorr,
                        clinoCorr,
                        tapeCorr,
                        backAzCorr,
                        backClCorr,
                        tapeFeet,
                        azimuthGrads,
                        clinoGrads,
                        clinoPercent,
                        clinoDepth,
                        warnings,
                        out var shot))
                    continue;
                shots.Add(shot);
            }
        }

        if (shots.Count == 0)
            throw new InvalidOperationException(
                "No Compass shots found. Expected a .dat survey with FROM, TO, length, bearing, and dip.");

        if (lengthUnit == "ft")
            warnings.Insert(0, "Compass tape was feet — stored as metres (×0.3048). The notebook unit stays feet.");
        return Finish(
            shots,
            string.IsNullOrWhiteSpace(caveName) ? fallbackName : caveName,
            date,
            lengthUnit,
            angleUnit,
            "Compass",
            warnings,
            null,
            null);
    }

    public static Result ParseWalls(string text, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var warnings = new List<string>();
        var shots = new List<ShotRecord>();
        var equates = new List<StationEquate>();
        var fixedPositions = new Dictionary<string, PlanStationPositionOverride>(StringComparer.OrdinalIgnoreCase);
        var fixedNames = new List<string>();
        var prefixStack = new List<string>();
        var tapeFeet = true;
        var anglesGrads = false;
        var order = "dav";
        string? sticky = null;
        var sawUnits = false;
        string? name = string.IsNullOrWhiteSpace(fallbackName) ? null : fallbackName.Trim();

        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = StripWallsComment(raw).Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith('#'))
            {
                var tokens = SplitTokens(line);
                var cmd = tokens[0].ToLowerInvariant();
                if (cmd is "#units")
                {
                    sawUnits = true;
                    foreach (var token in tokens.Skip(1))
                    {
                        var t = token.ToLowerInvariant();
                        if (t is "feet" or "ft" or "foot")
                            tapeFeet = true;
                        else if (t is "meters" or "metres" or "meter" or "m")
                            tapeFeet = false;
                        else if (t is "grads" or "grad")
                            anglesGrads = true;
                        else if (t is "degrees" or "deg")
                            anglesGrads = false;
                        else if (t.StartsWith("order=", StringComparison.Ordinal))
                            order = t[6..];
                        else if (t.StartsWith("a=", StringComparison.Ordinal) && t.Contains("grad", StringComparison.Ordinal))
                            anglesGrads = true;
                        else if (t.StartsWith("d=", StringComparison.Ordinal) && (t.Contains("meter", StringComparison.Ordinal) || t.EndsWith("=m", StringComparison.Ordinal)))
                            tapeFeet = false;
                        else if (t.StartsWith("d=", StringComparison.Ordinal) && t.Contains("feet", StringComparison.Ordinal))
                            tapeFeet = true;
                    }
                }
                else if (cmd is "#prefix")
                {
                    if (tokens.Count == 1)
                    {
                        if (prefixStack.Count > 0)
                            prefixStack.RemoveAt(prefixStack.Count - 1);
                    }
                    else
                        prefixStack.Add(tokens[1]);
                }
                else if (cmd is "#flag")
                {
                    sticky = tokens.Count == 1 ? null : WallsFlag(string.Join(' ', tokens.Skip(1)));
                }
                else if (cmd is "#equate" && tokens.Count >= 3)
                {
                    var first = ApplyPrefix(tokens[1], prefixStack);
                    for (var i = 2; i < tokens.Count; i++)
                    {
                        equates.Add(new StationEquate
                        {
                            StationA = first,
                            StationB = ApplyPrefix(tokens[i], prefixStack),
                        });
                    }
                }
                else if (cmd is "#fix" && tokens.Count >= 5 &&
                         TryParseFloat(tokens[2], out var east) &&
                         TryParseFloat(tokens[3], out var north) &&
                         TryParseFloat(tokens[4], out var up))
                {
                    var station = ApplyPrefix(tokens[1], prefixStack);
                    if (tapeFeet)
                    {
                        east = SurveyLegGeometry.ToMeters(east, "ft");
                        north = SurveyLegGeometry.ToMeters(north, "ft");
                        up = SurveyLegGeometry.ToMeters(up, "ft");
                    }

                    fixedPositions[station] = new PlanStationPositionOverride(east, north, up);
                    if (!fixedNames.Contains(station, StringComparer.OrdinalIgnoreCase))
                        fixedNames.Add(station);
                }
                else if (cmd is "#fix")
                    warnings.Add($"Skipped #fix (need station and three local coordinates): {line}");
                else if ((cmd is "#name" or "#title") && tokens.Count >= 2 && name == null)
                    name = string.Join(' ', tokens.Skip(1));

                continue;
            }

            var lrud = ExtractLrud(ref line);
            var fields = SplitTokens(line);
            if (fields.Count < 5)
            {
                warnings.Add($"Skipped short Walls line: {line}");
                continue;
            }

            var from = ApplyPrefix(fields[0], prefixStack);
            var to = ApplyPrefix(fields[1], prefixStack);
            var numbers = new List<float>();
            var comment = new List<string>();
            for (var i = 2; i < fields.Count; i++)
            {
                if (TryParseFloat(fields[i], out var n))
                    numbers.Add(n);
                else
                {
                    comment.AddRange(fields.Skip(i));
                    break;
                }
            }

            if (!TryWallsMeasures(order, numbers, out var tape, out var azimuth, out var clino))
            {
                warnings.Add($"Skipped Walls line without distance, azimuth, and clino: {line}");
                continue;
            }

            if (!sawUnits)
            {
                warnings.Add("No #units before the first shot — Walls default tape is feet, converted to metres.");
                sawUnits = true;
            }

            if (tapeFeet)
                tape = SurveyLegGeometry.ToMeters(tape, "ft");
            if (anglesGrads)
            {
                azimuth = SurveyLegGeometry.ToDegrees(azimuth, "grad");
                clino = SurveyLegGeometry.ToDegrees(clino, "grad");
            }

            var shot = new ShotRecord
            {
                FromStation = from,
                ToStation = IsSplayToken(to) ? "-" : to,
                Distance = tape,
                Azimuth = SurveyLegGeometry.NormalizeAzimuth360(azimuth),
                Clino = clino,
                Notes = comment.Count == 0 ? null : string.Join(' ', comment),
                LegFlags = sticky,
            };
            ApplyLrud(shot, lrud, tapeFeet);
            shots.Add(shot);
        }

        if (shots.Count == 0)
            throw new InvalidOperationException("No Walls shots found. Expected from, to, distance, azimuth, and clino.");

        if (tapeFeet)
            warnings.Insert(0, "Walls tape was feet — stored as metres (×0.3048). The notebook unit stays feet.");
        return Finish(
            shots,
            name,
            null,
            tapeFeet ? "ft" : "m",
            anglesGrads ? "grad" : null,
            "Walls",
            warnings,
            equates,
            fixedPositions,
            fixedNames);
    }

    public static Result ParseVisualTopo(string text, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var warnings = new List<string>();
        var shots = new List<ShotRecord>();
        var fixedPositions = new Dictionary<string, PlanStationPositionOverride>(StringComparer.OrdinalIgnoreCase);
        var fixedNames = new List<string>();
        string? caveName = string.IsNullOrWhiteSpace(fallbackName) ? null : fallbackName.Trim();
        var entrance = "ENT";

        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('*'))
                continue;
            line = line.Replace(',', ' ');
            var fields = SplitTokens(line);
            if (fields.Count == 0)
                continue;
            if (IsVisualTopoHeader(fields[0]))
            {
                if (fields[0].Equals("Entree", StringComparison.OrdinalIgnoreCase) && fields.Count >= 2)
                    caveName ??= string.Join(' ', fields.Skip(1));
                continue;
            }

            if (fields.Count >= 5 &&
                fields[1] == "*" &&
                TryParseFloat(fields[2], out var x) &&
                TryParseFloat(fields[3], out var y) &&
                TryParseFloat(fields[4], out var z) &&
                fields.Count == 5)
            {
                entrance = fields[0];
                fixedPositions[fields[0]] = new PlanStationPositionOverride(x, y, z);
                if (!fixedNames.Contains(fields[0], StringComparer.OrdinalIgnoreCase))
                    fixedNames.Add(fields[0]);
                continue;
            }

            if (fields.Count < 5 ||
                !TryParseFloat(fields[2], out var length) ||
                !TryParseFloat(fields[3], out var azimuth) ||
                !TryParseFloat(fields[4], out var clino))
            {
                if (caveName == null)
                    caveName = line;
                else
                    warnings.Add($"Skipped Visual Topo line: {line}");
                continue;
            }

            var to = fields[0];
            var from = fields[1] == "*" ? entrance : fields[1];
            string? flags = null;
            var comment = new List<string>();
            var numbers = new List<float>();
            for (var i = 5; i < fields.Count; i++)
            {
                if (fields[i].Length == 1 && char.IsLetter(fields[i][0]))
                {
                    flags = VisualTopoCode(fields[i]);
                    comment.AddRange(fields.Skip(i + 1));
                    break;
                }

                if (TryParseFloat(fields[i], out var n))
                    numbers.Add(n);
                else
                {
                    comment.AddRange(fields.Skip(i));
                    break;
                }
            }

            var shot = new ShotRecord
            {
                FromStation = from,
                ToStation = flags != null && SurveyLegFlags.Has(flags, SurveyLegFlags.NoPlot) && to.Length <= 2
                    ? to
                    : to,
                Distance = length,
                Azimuth = SurveyLegGeometry.NormalizeAzimuth360(azimuth),
                Clino = clino,
                Notes = comment.Count == 0 ? null : string.Join(' ', comment),
                LegFlags = flags,
            };
            if (flags != null && flags.Contains("SPLAY", StringComparison.Ordinal))
            {
                shot.Notes = string.IsNullOrWhiteSpace(shot.Notes) ? $"Splay {to}" : shot.Notes;
                shot.ToStation = "-";
                shot.LegFlags = null;
            }

            if (numbers.Count >= 4)
            {
                shot.L = Math.Max(0f, numbers[0]);
                shot.R = Math.Max(0f, numbers[1]);
                shot.U = Math.Max(0f, numbers[2]);
                shot.D = Math.Max(0f, numbers[3]);
            }

            shots.Add(shot);
        }

        if (shots.Count == 0 && fixedPositions.Count == 0)
            throw new InvalidOperationException(
                "No Visual Topo shots found. Expected lines of to, from, length, azimuth, and clino.");

        warnings.Insert(0, "Visual Topo shots are read as to-station then from-station, in metres and degrees.");
        return Finish(shots, caveName, null, "m", null, "Visual Topo", warnings, null, fixedPositions, fixedNames);
    }

    private static Result ParseWallsProject(string path)
    {
        var text = ReadText(path);
        var dir = Path.GetDirectoryName(path) ?? "";
        var refs = Regex.Matches(text, @"[A-Za-z0-9_ .\-\\/]+\.srv", RegexOptions.IgnoreCase)
            .Select(m => m.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (refs.Count == 0)
            throw new InvalidOperationException(
                "This Walls project does not name a .srv survey. Open the .srv file directly.");

        var warnings = new List<string>();
        var shots = new List<ShotRecord>();
        var equates = new List<StationEquate>();
        var fixes = new Dictionary<string, PlanStationPositionOverride>(StringComparer.OrdinalIgnoreCase);
        var fixedNames = new List<string>();
        string? name = Path.GetFileNameWithoutExtension(path);
        string? lengthUnit = null;
        string? angleUnit = null;
        foreach (var rel in refs)
        {
            var full = Path.IsPathRooted(rel) ? rel : Path.Combine(dir, rel);
            if (!File.Exists(full))
            {
                warnings.Add($"Missing survey file named by the project: {rel}");
                continue;
            }

            var part = ParseWalls(ReadText(full), Path.GetFileNameWithoutExtension(full));
            shots.AddRange(part.Project.Shots);
            if (part.Project.StationEquates != null)
                equates.AddRange(part.Project.StationEquates);
            if (part.Project.FixedStationPositions != null)
            {
                foreach (var kv in part.Project.FixedStationPositions)
                    fixes[kv.Key] = kv.Value;
            }

            if (part.Project.FixedStations != null)
                fixedNames.AddRange(part.Project.FixedStations);
            lengthUnit ??= part.Project.LengthUnit;
            angleUnit ??= part.Project.AngleUnit;
            warnings.AddRange(part.Warnings.Where(w => !w.StartsWith("Walls tape", StringComparison.Ordinal)));
        }

        if (shots.Count == 0)
            throw new InvalidOperationException("The Walls project did not contain any readable .srv shots.");
        return Finish(shots, name, null, lengthUnit, angleUnit, "Walls", warnings, equates, fixes, fixedNames);
    }

    private static bool TryParseCompassShot(
        string line,
        string? surveyName,
        double declination,
        double compassCorr,
        double clinoCorr,
        double tapeCorr,
        double backAzCorr,
        double backClCorr,
        bool tapeFeet,
        bool azimuthGrads,
        bool clinoGrads,
        bool clinoPercent,
        bool clinoDepth,
        List<string> warnings,
        out ShotRecord shot)
    {
        shot = null!;
        var fields = SplitTokens(line);
        if (fields.Count < 5)
            return false;
        var from = ApplySurveyPrefix(fields[0], surveyName);
        var to = ApplySurveyPrefix(fields[1], surveyName);
        var numbers = new List<float>();
        string? flagLetters = null;
        var comment = new List<string>();
        for (var i = 2; i < fields.Count; i++)
        {
            var token = fields[i];
            if (token.StartsWith("#|", StringComparison.Ordinal))
            {
                var end = token.IndexOf('#', 2);
                flagLetters = end > 2 ? token[2..end] : token[2..].TrimEnd('#');
                if (end >= 0 && end + 1 < token.Length)
                    comment.Add(token[(end + 1)..]);
                comment.AddRange(fields.Skip(i + 1));
                break;
            }

            if (TryParseFloat(token, out var n))
                numbers.Add(n);
            else
            {
                comment.AddRange(fields.Skip(i));
                break;
            }
        }

        if (numbers.Count < 3)
        {
            warnings.Add($"Skipped Compass line: {line}");
            return false;
        }

        var tape = numbers[0] + (float)tapeCorr;
        var bearing = numbers[1];
        var dip = numbers[2];
        float? backAz = numbers.Count >= 8 ? numbers[7] : null;
        float? backCl = numbers.Count >= 9 ? numbers[8] : null;
        if (bearing <= -900f && backAz is float recovered && recovered > -900f)
            bearing = SurveyLegGeometry.NormalizeAzimuth360(recovered + 180f);
        if (dip <= -900f && backCl is float recoveredCl && recoveredCl > -900f)
            dip = -recoveredCl;

        bearing += (float)(compassCorr + declination);
        dip += (float)clinoCorr;
        if (backAz is float baz && baz > -900f)
            backAz = baz + (float)(backAzCorr + declination);
        else
            backAz = null;
        if (backCl is float bcl && bcl > -900f)
            backCl = bcl + (float)backClCorr;
        else
            backCl = null;

        if (tapeFeet)
            tape = SurveyLegGeometry.ToMeters(tape, "ft");
        if (azimuthGrads)
        {
            bearing = SurveyLegGeometry.ToDegrees(bearing, "grad");
            if (backAz is float gs)
                backAz = SurveyLegGeometry.ToDegrees(gs, "grad");
        }

        if (clinoPercent)
        {
            dip = (float)(Math.Atan(dip / 100.0) * (180.0 / Math.PI));
            if (backCl is float pc)
                backCl = (float)(Math.Atan(pc / 100.0) * (180.0 / Math.PI));
        }
        else if (clinoGrads)
        {
            dip = SurveyLegGeometry.ToDegrees(dip, "grad");
            if (backCl is float gc)
                backCl = SurveyLegGeometry.ToDegrees(gc, "grad");
        }
        else if (clinoDepth && Math.Abs(tape) > 1e-4f)
        {
            var depth = tapeFeet ? SurveyLegGeometry.ToMeters(dip, "ft") : dip;
            var ratio = Math.Clamp(depth / tape, -1.0, 1.0);
            dip = (float)(Math.Asin(ratio) * (180.0 / Math.PI));
            backCl = null;
        }

        shot = new ShotRecord
        {
            FromStation = from,
            ToStation = to,
            Distance = tape,
            Azimuth = SurveyLegGeometry.NormalizeAzimuth360(bearing),
            Clino = dip,
            BacksightAzimuth = backAz is float ba ? SurveyLegGeometry.NormalizeAzimuth360(ba) : null,
            BacksightClino = backCl,
            Notes = comment.Count == 0 ? null : string.Join(' ', comment).Trim(),
            LegFlags = CompassFlags(flagLetters),
        };
        if (numbers.Count >= 7)
        {
            var scale = tapeFeet ? (float)SurveyLegGeometry.MetresPerFoot : 1f;
            shot.L = NonNegative(numbers[3] * scale);
            shot.U = NonNegative(numbers[4] * scale);
            shot.D = NonNegative(numbers[5] * scale);
            shot.R = NonNegative(numbers[6] * scale);
        }

        return true;
    }

    private static void ParseCompassDeclination(
        string line,
        out double declination,
        out double compassCorr,
        out double clinoCorr,
        out double tapeCorr,
        out double backAzCorr,
        out double backClCorr,
        out bool tapeFeet,
        out bool azimuthGrads,
        out bool clinoGrads,
        out bool clinoPercent,
        out bool clinoDepth)
    {
        declination = ReadLabeledNumber(line, "DECLINATION:");
        compassCorr = 0;
        clinoCorr = 0;
        tapeCorr = 0;
        backAzCorr = 0;
        backClCorr = 0;
        tapeFeet = true;
        azimuthGrads = false;
        clinoGrads = false;
        clinoPercent = false;
        clinoDepth = false;
        var format = ReadLabeledWord(line, "FORMAT:");
        if (format.Length >= 2 && format[1] == 'M')
            tapeFeet = false;
        if (format.Length >= 1 && format[0] == 'R')
            azimuthGrads = true;
        if (format.Length >= 4)
        {
            clinoGrads = format[3] == 'R';
            clinoPercent = format[3] == 'G';
            clinoDepth = format[3] == 'W';
        }

        var corr = ReadLabeledNumbers(line, "CORRECTIONS:", "CORRECTIONS2:");
        if (corr.Count >= 1) compassCorr = corr[0];
        if (corr.Count >= 2) clinoCorr = corr[1];
        if (corr.Count >= 3) tapeCorr = corr[2];
        var corr2 = ReadLabeledNumbers(line, "CORRECTIONS2:", null);
        if (corr2.Count >= 1) backAzCorr = corr2[0];
        if (corr2.Count >= 2) backClCorr = corr2[1];
    }

    private static Result Finish(
        List<ShotRecord> shots,
        string? name,
        string? date,
        string? lengthUnit,
        string? angleUnit,
        string formatName,
        List<string> warnings,
        List<StationEquate>? equates,
        Dictionary<string, PlanStationPositionOverride>? fixes,
        List<string>? fixedNames = null)
    {
        var traverse = shots.Count(s => s.IsTraverseLeg);
        var splay = shots.Count(s => !s.IsTraverseLeg && s.Distance > 0);
        var project = new CaveProjectDocument
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Imported {formatName} survey" : name.Trim(),
            Date = date ?? "",
            Shots = shots,
            LengthUnit = lengthUnit,
            AngleUnit = angleUnit,
            StationEquates = equates is { Count: > 0 } ? equates : null,
            FixedStationPositions = fixes is { Count: > 0 } ? fixes : null,
            FixedStations = fixedNames is { Count: > 0 } ? fixedNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : null,
        };
        return new Result
        {
            Project = project,
            TraverseLegs = traverse,
            SplayLegs = splay,
            FormatName = formatName,
            Warnings = warnings,
        };
    }

    private static string? CompassFlags(string? letters)
    {
        if (string.IsNullOrWhiteSpace(letters))
            return null;
        var flags = new List<string>();
        foreach (var ch in letters.ToUpperInvariant())
        {
            switch (ch)
            {
                case 'L': flags.Add(SurveyLegFlags.Surface); break;
                case 'P': flags.Add(SurveyLegFlags.NoPlot); break;
                case 'X': flags.Add(SurveyLegFlags.Exclude); break;
                case 'C': flags.Add(SurveyLegFlags.DontAdjust); break;
            }
        }

        return flags.Count == 0 ? null : string.Join(' ', flags.Distinct());
    }

    private static string? WallsFlag(string raw)
    {
        var t = raw.Trim().ToLowerInvariant();
        if (t is "s" or "surface")
            return SurveyLegFlags.Surface;
        if (t is "d" or "duplicate")
            return SurveyLegFlags.Duplicate;
        if (t is "x" or "exclude" or "excluded")
            return SurveyLegFlags.Exclude;
        return null;
    }

    private static string? VisualTopoCode(string code)
    {
        return code.ToUpperInvariant() switch
        {
            "E" or "X" => SurveyLegFlags.Exclude,
            "S" or "V" => SurveyLegFlags.Surface,
            "I" => "SPLAY",
            _ => null,
        };
    }

    private static bool IsVisualTopoHeader(string token)
    {
        return token.Equals("Version", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Entree", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Entrée", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Club", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Couleur", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Date", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Param", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Type", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Speleo", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryWallsMeasures(string order, List<float> numbers, out float tape, out float azimuth, out float clino)
    {
        tape = azimuth = clino = 0;
        if (numbers.Count < 3)
            return false;
        var key = order.ToLowerInvariant();
        if (key.Length < 3)
            key = "dav";
        var map = new Dictionary<char, float>
        {
            [key[0]] = numbers[0],
            [key[1]] = numbers[1],
            [key[2]] = numbers[2],
        };
        if (!map.TryGetValue('d', out tape) || !map.TryGetValue('a', out azimuth) || !map.TryGetValue('v', out clino))
        {
            tape = numbers[0];
            azimuth = numbers[1];
            clino = numbers[2];
        }

        return true;
    }

    private static void ApplyLrud(ShotRecord shot, float[]? lrud, bool tapeFeet)
    {
        if (lrud == null || lrud.Length < 4)
            return;
        var scale = tapeFeet ? (float)SurveyLegGeometry.MetresPerFoot : 1f;
        shot.L = NonNegative(lrud[0] * scale);
        shot.R = NonNegative(lrud[1] * scale);
        shot.U = NonNegative(lrud[2] * scale);
        shot.D = NonNegative(lrud[3] * scale);
    }

    private static float[]? ExtractLrud(ref string line)
    {
        var match = Regex.Match(line, "<([^>]+)>");
        if (!match.Success)
            return null;
        line = (line[..match.Index] + " " + line[(match.Index + match.Length)..]).Trim();
        var nums = SplitTokens(match.Groups[1].Value.Replace(',', ' '))
            .Select(t => TryParseFloat(t, out var n) ? n : float.NaN)
            .Where(n => !float.IsNaN(n))
            .ToArray();
        return nums.Length >= 4 ? nums : null;
    }

    private static string ApplyPrefix(string station, List<string> prefixStack)
    {
        if (IsSplayToken(station) || prefixStack.Count == 0)
            return station;
        var prefix = string.Concat(prefixStack);
        if (station.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return station;
        return prefix + station;
    }

    private static string ApplySurveyPrefix(string station, string? surveyName)
    {
        if (string.IsNullOrWhiteSpace(surveyName) || IsSplayToken(station))
            return station;
        if (station.Any(char.IsLetter))
            return station;
        if (station.StartsWith(surveyName, StringComparison.OrdinalIgnoreCase))
            return station;
        return surveyName.Trim() + station;
    }

    private static bool IsSplayToken(string station) =>
        station is "-" or "*" or "." or "splay";

    private static string StripWallsComment(string line)
    {
        var i = line.IndexOf(';');
        return i < 0 ? line : line[..i];
    }

    private static string AfterLabel(string line, string label)
    {
        var i = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return "";
        var rest = line[(i + label.Length)..].Trim();
        var space = rest.IndexOf(' ');
        return space < 0 ? rest : rest[..space];
    }

    private static string? ParseCompassDate(string line)
    {
        var rest = line["SURVEY DATE:".Length..].Trim();
        var comment = rest.IndexOf("COMMENT:", StringComparison.OrdinalIgnoreCase);
        if (comment >= 0)
            rest = rest[..comment].Trim();
        var parts = SplitTokens(rest);
        if (parts.Count < 3 ||
            !int.TryParse(parts[0], out var month) ||
            !int.TryParse(parts[1], out var day) ||
            !int.TryParse(parts[2], out var year))
            return null;
        if (year < 100)
            year += year >= 70 ? 1900 : 2000;
        if (month is < 1 or > 12 || day is < 1 or > 31)
            return null;
        return $"{year:0000}-{month:00}-{day:00}";
    }

    private static double ReadLabeledNumber(string line, string label)
    {
        var word = ReadLabeledWord(line, label);
        return TryParseDouble(word, out var n) ? n : 0;
    }

    private static string ReadLabeledWord(string line, string label)
    {
        var i = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return "";
        var rest = line[(i + label.Length)..].Trim();
        var space = rest.IndexOf(' ');
        return space < 0 ? rest : rest[..space];
    }

    private static List<double> ReadLabeledNumbers(string line, string label, string? stopLabel)
    {
        var i = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<double>();
        var rest = line[(i + label.Length)..];
        if (stopLabel != null)
        {
            var stop = rest.IndexOf(stopLabel, StringComparison.OrdinalIgnoreCase);
            if (stop >= 0)
                rest = rest[..stop];
        }

        var nums = new List<double>();
        foreach (var token in SplitTokens(rest))
        {
            if (!TryParseDouble(token, out var n))
                break;
            nums.Add(n);
        }

        return nums;
    }

    private static float NonNegative(float value) => value < 0f ? 0f : value;

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

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }
}
