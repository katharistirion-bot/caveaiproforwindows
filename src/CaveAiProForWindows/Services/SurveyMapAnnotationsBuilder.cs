using System.Globalization;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Builds leg labels, depth spans, brackets, and station environment overlays from Android survey JSON.</summary>
public static class SurveyMapAnnotationsBuilder
{
    private const float LegLabelPerpMetres = 0.48f;

    public static SurveyMapAnnotations Build(
        CaveProjectDocument? project,
        PlanScene scene,
        int viewMode,
        bool legDetails,
        bool stationEnvironment)
    {
        if (project == null)
            return new SurveyMapAnnotations();

        var legs = legDetails ? BuildLegLabels(project, scene) : Array.Empty<SurveyLegMapLabel>();
        var env = stationEnvironment ? BuildStationEnvironment(project, scene) : Array.Empty<SurveyStationEnvMapLabel>();
        var spans = BuildDepthSpans(project, viewMode);
        var brackets = BuildBrackets(project, viewMode);

        return new SurveyMapAnnotations
        {
            LegLabels = legs,
            StationEnvironment = env,
            DepthSpans = spans,
            Brackets = brackets,
        };
    }

    private static IReadOnlyList<SurveyLegMapLabel> BuildLegLabels(CaveProjectDocument project, PlanScene scene)
    {
        var inv = CultureInfo.InvariantCulture;
        var coords = scene.Stations;
        var list = new List<SurveyLegMapLabel>();
        var index = 0;

        foreach (var shot in project.Shots.Where(s => s.IsTraverseLeg))
        {
            if (!coords.TryGetValue(shot.FromStation.Trim(), out var a) ||
                !coords.TryGetValue(shot.ToStation.Trim(), out var b))
                continue;

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-5f)
                continue;

            var midX = (a.X + b.X) * 0.5f;
            var midY = (a.Y + b.Y) * 0.5f;
            var side = (index & 1) == 0 ? 1f : -1f;
            index++;
            var nx = -dy / len * LegLabelPerpMetres * side;
            var ny = dx / len * LegLabelPerpMetres * side;

            var primary = shot.Distance.ToString("0.##", inv) + " m";
            var secondary = SurveyMapAnnotationText.FormatLegAngles(shot.Azimuth, shot.Clino, inv);

            var dz = b.Z - a.Z;
            if (Math.Abs(dz) > 0.005)
                secondary += "  " + SurveyMapAnnotationText.FormatDeltaZ(dz, inv);

            if (Math.Abs(shot.Depth) > 1e-5f)
                secondary += "  d " + shot.Depth.ToString("0.##", inv);

            var (L, R, U, D) = shot.EffectivePlanLrud();
            string? lrud = null;
            if (L + R + U + D > 0.02f)
                lrud = SurveyMapAnnotationText.FormatLrud(L, R, U, D, inv);

            list.Add(new SurveyLegMapLabel(midX, midY, nx, ny, primary, secondary, lrud));
        }

        return list;
    }

    private static IReadOnlyList<SurveyStationEnvMapLabel> BuildStationEnvironment(
        CaveProjectDocument project,
        PlanScene scene)
    {
        var inv = CultureInfo.InvariantCulture;
        var list = new List<SurveyStationEnvMapLabel>();

        foreach (var kv in scene.Stations)
        {
            var name = kv.Key;
            var coord = kv.Value;
            var lines = new List<string>();

            var outgoing = project.Shots
                .Where(s => NamesEq(s.FromStation, name) && s.IsTraverseLeg)
                .ToList();
            var incoming = project.Shots
                .Where(s => NamesEq(s.ToStation, name) && s.IsTraverseLeg)
                .ToList();

            var best = outgoing.LastOrDefault() ?? incoming.LastOrDefault();
            if (best != null)
            {
                var compact = ShotEnvironment.TryFormatCompactMapLine(best);
                if (!string.IsNullOrWhiteSpace(compact))
                    lines.Add(compact);
            }

            if (lines.Count == 0)
                continue;

            list.Add(new SurveyStationEnvMapLabel(coord.X, coord.Y, name, lines));
        }

        return list;
    }

    private static IReadOnlyList<SurveyDepthSpanMapLabel> BuildDepthSpans(CaveProjectDocument project, int viewMode)
    {
        var inv = CultureInfo.InvariantCulture;
        var list = new List<SurveyDepthSpanMapLabel>();
        if (project.DepthSpanAnnotations.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in project.DepthSpanAnnotations.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (!TryReadInt(el, "viewMode", out var vm))
                vm = 0;
            if (vm != viewMode)
                continue;

            var x1 = ReadFloat(el, "x1");
            var y1 = ReadFloat(el, "y1");
            var x2 = ReadFloat(el, "x2");
            var y2 = ReadFloat(el, "y2");
            var depth = ReadFloat(el, "depthMeters");
            if (depth <= 0 && TryReadFloat(el, "depthSpanM", out var alt))
                depth = alt;

            var label = depth > 0
                ? depth.ToString("0.##", inv) + " m"
                : "—";
            list.Add(new SurveyDepthSpanMapLabel(x1, y1, x2, y2, depth, label));
        }

        return list;
    }

    private static IReadOnlyList<SurveyBracketMapLabel> BuildBrackets(CaveProjectDocument project, int viewMode)
    {
        var list = new List<SurveyBracketMapLabel>();
        if (project.Brackets.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in project.Brackets.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (TryReadInt(el, "viewMode", out var vm) && vm != viewMode)
                continue;

            var x = ReadFloat(el, "x");
            var y = ReadFloat(el, "y");
            var desc = ReadString(el, "description") ?? "";
            var temp = ReadString(el, "temperature") ?? "";
            var text = JoinNonEmpty(" · ", desc.Trim(), temp.Trim());
            if (string.IsNullOrWhiteSpace(text))
                text = "Bracket";
            list.Add(new SurveyBracketMapLabel(x, y, text));
        }

        return list;
    }

    private static bool NamesEq(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static float ReadFloat(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.TryGetSingle(out var f) ? f : 0f;

    private static bool TryReadFloat(JsonElement el, string name, out float value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        return p.TryGetSingle(out value);
    }

    private static bool TryReadInt(JsonElement el, string name, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        if (p.TryGetInt32(out value))
            return true;
        if (p.TryGetDouble(out var d))
        {
            value = (int)d;
            return true;
        }

        return false;
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string JoinNonEmpty(string sep, params string[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return kept.Length == 0 ? "" : string.Join(sep, kept);
    }
}
