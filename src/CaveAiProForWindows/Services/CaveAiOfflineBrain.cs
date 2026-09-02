using System.Globalization;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurveyAnalysis;

namespace CaveAiProForWindows.Services;

/// <summary>Rule-based offline Q&amp;A from loaded project — mirrors Android <c>LocalCaveAiBrain</c> v4 intents.</summary>
public static class CaveAiOfflineBrain
{
    public static string Answer(CaveProjectDocument? project, string? query, IEnumerable<KnownCaveRecord>? library = null)
    {
        if (project == null)
            return "Open a CaveAI Pro backup and select a project first.";

        var q = Normalize(query);
        if (q.Length == 0)
            return "Ask about depth, traverse length, shot count, return distance, pending geo samples, site type, last leg, volume, clino, loop closure, trip report, Survex/Therion import, or next step (English only).";

        var shots = project.Shots;
        var mains = shots.Where(s => s.IsTraverseLeg).ToList();
        var splays = shots.Count - mains.Count;
        var traverseM = mains.Sum(s => (double)s.Distance);

        if (IsGreetingQuery(q))
            return $"Hello. I am your on-device assistant for \"{project.Name}\" — {shots.Count} shots, {mains.Count} main legs, about {traverseM.ToString("0.#", CultureInfo.InvariantCulture)} m traverse. Ask about depth, loops, or say \"summary\".";

        if (IsThanksQuery(q))
            return "You are welcome. Keep station names consistent, watch battery, and have a safe trip.";

        if (IsTripNarrativeQuery(q))
            return TripEndNarrativeOffline.BuildOfflineTripNarrativeChatAnswer(project);

        if (IsSurveyQcQuery(q))
            return SurveyLoopQc.SurveyLoopQcSummary.BuildCopilotAnswer(project);

        if (IsOverviewStatusQuery(q))
        {
            var overviewDepth = ComputeMaxDepthBelowEntranceMeters(project);
            return $"Project \"{project.Name}\": {shots.Count} shots ({mains.Count} mains, {splays} splays), traverse ~{traverseM.ToString("0.#", CultureInfo.InvariantCulture)} m, max depth ~{overviewDepth.ToString("0.#", CultureInfo.InvariantCulture)} m.";
        }

        var maxDepth = ComputeMaxDepthBelowEntranceMeters(project);
        var pendingGeo = CountPendingGeoSamples(project);
        var entranceLocked = project.Lat.HasValue && project.Lon.HasValue;
        var trailingSplays = TrailingSplayCount(shots);
        var volumeM3 = ComputeRoughVolumeM3(project);
        var lastMain = mains.Count > 0 ? mains[^1] : null;

        if (IsNextActionQuery(q))
            return BuildNextActionAnswer(project, shots, mains, splays, traverseM, pendingGeo, entranceLocked, trailingSplays, library);

        if (IsLastLegQuery(q))
        {
            if (lastMain != null)
                return $"Last main leg: {lastMain.FromStation}→{lastMain.ToStation}, {lastMain.Distance.ToString("0.#", CultureInfo.InvariantCulture)} m, clino {lastMain.Clino.ToString("0.#", CultureInfo.InvariantCulture)}°.";
            return "No main legs recorded yet.";
        }

        if (IsVolumeQuery(q))
        {
            if (volumeM3 > 0.5)
                return $"Rough passage volume from LRUD × legs ~{volumeM3.ToString("0.#", CultureInfo.InvariantCulture)} m³ (indicative).";
            return "Add LRUD on mains to build a volume estimate — not enough yet.";
        }

        if (IsClinoQuery(q))
        {
            if (lastMain != null)
                return $"Latest main clino is {lastMain.Clino.ToString("0.#", CultureInfo.InvariantCulture)}° ({lastMain.FromStation}→{lastMain.ToStation}).";
            return "Log main legs to record clino per leg; none yet.";
        }

        if (IsLoopMisclosureQuery(q))
            return BuildLoopMisclosureAnswer(project);

        if (ContainsAny(q, "survex", "therion", "import svx", "import .svx", "import th", "import .th") ||
            (q.Contains("import") && ContainsAny(q, "survex", "therion", "svx", ".th")))
        {
            return "File → Import Survex (.svx) or Import Therion (.th) to bring office surveys into CaveAI Pro for Windows. "
                + "Use Loop closure assistant for Compass / WLS plan overrides after import. Export Survex/Therion/DXF remains available from the export menus.";
        }

        if (ContainsAny(q, "loop closure assistant", "compass rule", "wls", "weighted least") ||
            (q.Contains("adjust") && ContainsAny(q, "loop", "closure", "misclosure")))
        {
            return "Open Loop closure assistant for multi-loop Compass rule or weighted least-squares (WLS) plan overrides. "
                + "Raw shots stay unchanged. The web Survey QC → Loop closure panel offers the same office adjust.";
        }

        if (ContainsAny(q, "depth", "deep", "vertical"))
            return $"Max depth span vs entrance ~{maxDepth.ToString("0.#", CultureInfo.InvariantCulture)} m.";

        if (ContainsAny(q, "traverse", "length") || (q.Contains("distance") && q.Contains("survey")))
            return $"Total traverse ~{traverseM.ToString("0.#", CultureInfo.InvariantCulture)} m across {mains.Count} main leg(s).";

        if (IsEntranceDistanceQuery(q))
        {
            var dist = ComputeReturnDistanceMeters(project, mains);
            if (dist > 1)
                return $"Horizontal-ish distance toward entrance ~{dist.ToString("0.#", CultureInfo.InvariantCulture)} m.";
            return "Log main legs from a fixed entrance and lock GPS at the entrance to build return distance.";
        }

        if ((q.Contains("how many") || q.Contains("count")) &&
            ContainsAny(q, "shot", "leg", "splay", "station"))
            return $"This project has {shots.Count} shots ({mains.Count} mains, {splays} splays).";

        if (IsPendingGeoQuery(q))
        {
            if (pendingGeo > 0)
                return $"{pendingGeo} field sample(s) pending geo/bio sync.";
            return "No pending field samples — geo/bio sync is up to date.";
        }

        if (IsSiteTypeQuery(q))
            return SiteIdentity.SummarizeActiveProject(project, library);

        if (ContainsAny(q, "shot", "leg", "station") && !ContainsAny(q, "depth", "traverse"))
            return $"Shots: {shots.Count} total ({mains.Count} mains, {splays} splays).";

        if (ContainsAny(q, "name", "project"))
        {
            var siteLabel = SurveySiteTypeResolver.GetMapLabel(project, library);
            var siteToken = SurveySiteType.CanonicalToken(SurveySiteTypeResolver.Resolve(project, library));
            return $"Project: {project.Name.Trim()} · Site type: {siteLabel} ({siteToken}).";
        }

        var siteLabelFallback = SurveySiteTypeResolver.GetMapLabel(project, library);
        return
            $"Project \"{project.Name}\" — {siteLabelFallback}. {shots.Count} shots, ~{traverseM.ToString("0.#", CultureInfo.InvariantCulture)} m traverse, max depth ~{maxDepth.ToString("0.#", CultureInfo.InvariantCulture)} m. " +
            "Ask about depth, traverse, shots, return distance, pending geo, site type, last leg, volume, clino, loop closure, Survex/Therion import, trip report, or next step.";
    }

    private static string BuildNextActionAnswer(
        CaveProjectDocument project,
        IReadOnlyList<ShotRecord> shots,
        List<ShotRecord> mains,
        int splays,
        double traverseM,
        int pendingGeo,
        bool entranceLocked,
        int trailingSplays,
        IEnumerable<KnownCaveRecord>? library)
    {
        if (pendingGeo > 0)
            return $"Next: open the **Geo/Bio** tab — {pendingGeo} field sample(s) pending geo/bio sync.";

        if (entranceLocked && shots.Count == 0)
            return "Next: open the **HUD** tab. Entrance GPS is locked — set From/To and log your first main leg.";

        if (!entranceLocked && shots.Count == 0)
            return "Next: lock entrance GPS at the mouth (toolbar GPS icon), then open **HUD** to log your first main leg.";

        if (trailingSplays >= 3 && mains.Count > 0)
            return $"Next: tie in a **main leg** — {trailingSplays} trailing splays; mains carry traverse and return distance.";

        var dist = ComputeReturnDistanceMeters(project, mains);
        if (dist > 1)
            return $"Next: continue surveying on **HUD** — {shots.Count} shots, {mains.Count} mains, ~{traverseM.ToString("0.#", CultureInfo.InvariantCulture)} m traverse (~{dist.ToString("0.#", CultureInfo.InvariantCulture)} m from entrance).";

        return $"Next: continue surveying on **HUD** — {shots.Count} shots ({mains.Count} mains, {splays} splays), ~{traverseM.ToString("0.#", CultureInfo.InvariantCulture)} m traverse.";
    }

    internal static int CountPendingGeoSamples(CaveProjectDocument project)
    {
        if (project.Rocks is not { ValueKind: JsonValueKind.Array } rocks)
            return 0;

        var count = 0;
        foreach (var rock in rocks.EnumerateArray())
        {
            var imageUri = GetJsonString(rock, "imageUri");
            var isAnalyzed = rock.TryGetProperty("isAnalyzed", out var analyzed) &&
                             analyzed.ValueKind == JsonValueKind.True;
            if (!string.IsNullOrWhiteSpace(imageUri) && !isAnalyzed)
                count++;
        }
        return count;
    }

    private static double ComputeReturnDistanceMeters(CaveProjectDocument project, List<ShotRecord> mains)
    {
        if (mains.Count == 0)
            return 0;

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        if (coords.Count == 0)
            return 0;

        var entranceStation = mains[0].FromStation.Trim();
        var currentStation = mains[^1].ToStation.Trim();
        if (!coords.TryGetValue(entranceStation, out var entrance) ||
            !coords.TryGetValue(currentStation, out var current))
            return 0;

        var dx = entrance.X - current.X;
        var dy = entrance.Y - current.Y;
        var dz = entrance.Z - current.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static int TrailingSplayCount(IReadOnlyList<ShotRecord> shots)
    {
        var n = 0;
        for (var i = shots.Count - 1; i >= 0; i--)
        {
            if (!shots[i].IsTraverseLeg)
                n++;
            else
                break;
        }
        return n;
    }

    private static double ComputeMaxDepthBelowEntranceMeters(CaveProjectDocument project)
    {
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        if (coords.Count == 0)
            return 0;
        var entranceAlt = project.Alt;
        var maxBelow = 0.0;
        foreach (var (_, c) in coords)
        {
            var below = entranceAlt - c.Z;
            if (below > maxBelow)
                maxBelow = below;
        }
        return maxBelow;
    }

    private static bool IsNextActionQuery(string q) =>
        ContainsAny(q, "next", "now", "should") &&
        ContainsAny(q, "do", "step", "action", "focus", "priority");

    private static bool IsEntranceDistanceQuery(string q) =>
        ContainsAny(q, "return", "exit", "egress") ||
        q.Contains("distance to entrance") ||
        q.Contains("how far to entrance") ||
        q.Contains("how far to the entrance") ||
        (q.Contains("entrance") && ContainsAny(q, "distance", "far", "back", "out"));

    private static bool IsPendingGeoQuery(string q) =>
        ContainsAny(q, "sample", "rock") ||
        (q.Contains("photo") && q.Contains("geo")) ||
        (q.Contains("pending") && ContainsAny(q, "geo", "bio", "sync", "field"));

    private static bool IsSiteTypeQuery(string q) =>
        q.Contains("site type") ||
        q.Contains("kind of site") ||
        (ContainsAny(q, "what", "which") && ContainsAny(q, "site", "cave", "mine", "pothole", "spring", "project", "here")) ||
        (ContainsAny(q, "kind", "type") && ContainsAny(q, "site", "cave", "mine", "pothole", "spring"));

    private static bool IsLastLegQuery(string q) =>
        (q.Contains("last") && ContainsAny(q, "leg", "shot", "main")) ||
        q.Contains("latest main");

    private static bool IsVolumeQuery(string q) =>
        q.Contains("volume") ||
        q.Contains("how big") ||
        q.Contains("how large") ||
        q.Contains("passage size");

    private static bool IsClinoQuery(string q) =>
        (ContainsAny(q, "clino", "inclination", "slope", "grade") || q.Contains("latest clino")) &&
        !ContainsAny(q, "rope", "rig", "descend", "srt", "harness");

    private static bool IsLoopMisclosureQuery(string q) =>
        ContainsAny(q, "loop", "closure", "misclosure") ||
        q.Contains("loop closure") ||
        q.Contains("loop quality");

    private static bool IsSurveyQcQuery(string q) =>
        q.Contains("survey qc", StringComparison.Ordinal) ||
        q.Contains("qc summary", StringComparison.Ordinal) ||
        q.Contains("survey quality", StringComparison.Ordinal) ||
        q.Contains("quality control", StringComparison.Ordinal) ||
        ContainsAny(q, "remeasure", "re-measure", "reshoot", "re-shoot") ||
        q.Contains("what should i fix", StringComparison.Ordinal) ||
        q.Contains("next qc", StringComparison.Ordinal);

    private static bool IsTripNarrativeQuery(string q) =>
        (ContainsAny(q, "narrative", "report", "summary") &&
         ContainsAny(q, "trip", "expedition", "mission", "survey")) ||
        q.Contains("trip report") ||
        q.Contains("expedition report") ||
        q.Contains("mission writer");

    private static bool IsGreetingQuery(string q)
    {
        if (q.Length > 48) return false;
        return q is "hi" or "hey" or "yo" or "hello" ||
            q.StartsWith("hi ", StringComparison.Ordinal) ||
            q.StartsWith("hello ", StringComparison.Ordinal) ||
            q.StartsWith("hey ", StringComparison.Ordinal) ||
            q.StartsWith("good morning", StringComparison.Ordinal) ||
            q.StartsWith("good evening", StringComparison.Ordinal);
    }

    private static bool IsThanksQuery(string q) =>
        q.Length <= 40 && (q.Contains("thank") || q.Contains("thanks") || q.Contains("thx"));

    private static bool IsOverviewStatusQuery(string q) =>
        !IsTripNarrativeQuery(q) && (
        q.Contains("summary") || q.Contains("overview") || q.Contains("status") ||
        q.Contains("what do i have") || q.Contains("how many shots") ||
        (q.Contains("all") && q.Contains("good")));

    private static string BuildLoopMisclosureAnswer(CaveProjectDocument project)
    {
        var loops = SurveyLoopQc.SurveyLoopQcAnalyzer.AnalyzeLoops(project);
        if (loops.Count == 0)
            return "No traverse loops detected yet — log closing shots to known stations to measure misclosure.";

        return SurveyLoopQc.SurveyLoopQcSummary.BuildMisclosureSummary(loops);
    }

    private static double ComputeRoughVolumeM3(CaveProjectDocument project)
    {
        double vol = 0;
        foreach (var s in project.Shots.Where(s => s.IsTraverseLeg))
        {
            var (l, r, u, d) = s.EffectivePlanLrud();
            vol += (l + r) * (u + d) * s.Distance;
        }
        return vol;
    }

    private static string? GetJsonString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static string Normalize(string? raw) =>
        (raw ?? "").Trim().ToLowerInvariant();

    private static bool ContainsAny(string haystack, params string[] words) =>
        words.Any(w => haystack.Contains(w, StringComparison.Ordinal));

    /// <summary>Short status lines for publication sheet, GEO/BIO header, and Survey QC (no chat UI).</summary>
    public static IReadOnlyList<string> BuildStatusHints(CaveProjectDocument? project, int maxHints = 3)
    {
        if (project == null)
            return ["Open a CaveAI backup and select a project."];

        var hints = new List<string>();
        var pendingGeo = CountPendingGeoSamples(project);
        if (pendingGeo > 0)
            hints.Add($"{pendingGeo} field sample(s) pending geo/bio — open GEO & BIO tab.");

        var loops = SurveyLoopQc.SurveyLoopQcAnalyzer.AnalyzeLoops(project);
        if (loops.Count > 0)
        {
            var worst = loops.OrderByDescending(l => l.MisclosureMeters).First();
            var inv = CultureInfo.InvariantCulture;
            hints.Add(
                $"Loop closure: worst |Δ|={worst.MisclosureMeters.ToString("0.##", inv)} m ({SurveyLoopQc.SurveyLoopQcSummary.SeverityLabel(worst.MisclosureMeters, worst.PathLengthMeters)}) — Tools → Loop closure assistant.");
        }

        var next = Answer(project, "what should I do next").Replace("**", "", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(next))
            hints.Add(next);

        if (hints.Count == 0)
            hints.Add(Answer(project, "summary"));

        return hints.Take(Math.Max(1, maxHints)).ToList();
    }

    public static string FormatStatusHintPanel(CaveProjectDocument? project) =>
        string.Join(" · ", BuildStatusHints(project));
}
