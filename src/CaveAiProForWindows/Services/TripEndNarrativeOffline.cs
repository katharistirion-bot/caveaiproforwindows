using System.Globalization;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurveyAnalysis;

namespace CaveAiProForWindows.Services;

/// <summary>Offline expedition report — mirrors Android TripEndNarrativeOffline.kt.</summary>
public static class TripEndNarrativeOffline
{
    internal const int TripEndNarrativeMaxChars = 12_000;

    public static string BuildOfflineTripEndNarrative(CaveProjectDocument project)
    {
        var shots = project.Shots;
        var mains = shots.Where(s => s.IsTraverseLeg).ToList();
        var splays = shots.Count - mains.Count;
        var traverseM = mains.Sum(s => (double)s.Distance);
        var maxDepth = ComputeMaxDepthBelowEntranceMeters(project);
        var loops = SurveyLoopClosureAdjuster.DetectLoops(project);
        var pendingGeo = CaveAiOfflineBrain.CountPendingGeoSamples(project);
        var analyzedGeo = CountAnalyzedGeoSamples(project);
        var inv = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        sb.AppendLine("Expedition Overview");
        sb.AppendLine();
        sb.Append("This report summarizes the \"")
            .Append(string.IsNullOrWhiteSpace(project.Name) ? "Unnamed project" : project.Name.Trim())
            .Append("\" field survey");
        if (!string.IsNullOrWhiteSpace(project.Date))
            sb.Append(" on ").Append(project.Date.Trim());
        sb.Append(". Entrance reference altitude ~")
            .Append(project.Alt.ToString("0", inv))
            .Append(" m");
        if (project.Lat is { } lat && project.Lon is { } lon && (Math.Abs(lat) > 1e-9 || Math.Abs(lon) > 1e-9))
        {
            sb.Append(" at ")
                .Append(lat.ToString("0.00000", inv))
                .Append(", ")
                .Append(lon.ToString("0.00000", inv));
        }
        sb.AppendLine(".");
        sb.AppendLine();

        sb.AppendLine("Survey Statistics");
        sb.AppendLine();
        if (mains.Count == 0)
        {
            sb.AppendLine("No traverse legs were logged yet — add main shots from the HUD to build statistics.");
            sb.AppendLine();
        }
        else
        {
            sb.Append("The traverse comprises ")
                .Append(mains.Count)
                .Append(" main leg(s) and ")
                .Append(splays)
                .Append(" splay shot(s), totaling ~")
                .Append(traverseM.ToString("0.#", inv))
                .Append(" m of surveyed passage. Maximum depth below entrance is ~")
                .Append(maxDepth.ToString("0.#", inv))
                .Append(" m. Main line runs ")
                .Append(mains[0].FromStation)
                .Append(" -> ")
                .Append(mains[^1].ToStation)
                .Append(". ");
            if (loops.Count > 0)
                sb.Append(BuildLoopMisclosureSummary(loops));
            else
                sb.Append("No closing loops detected — log redundant legs to known stations to measure misclosure.");
            sb.AppendLine();
            sb.AppendLine();
        }

        var rocksCount = project.RocksCount;
        var catalogCount = project.FieldCatalogEntryCount;
        if (rocksCount > 0 || catalogCount > 0)
        {
            sb.AppendLine("Geology & Field Catalog");
            sb.AppendLine();
            sb.Append("Geo/bio field samples: ")
                .Append(rocksCount)
                .Append(" total (")
                .Append(analyzedGeo)
                .Append(" analyzed, ")
                .Append(pendingGeo)
                .Append(" pending sync). ");
            if (catalogCount > 0 && project.FieldCatalogEntries is { ValueKind: JsonValueKind.Array } catalog)
            {
                var byKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in catalog.EnumerateArray())
                {
                    var kind = entry.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                        ? k.GetString() ?? "UNKNOWN"
                        : "UNKNOWN";
                    byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
                }
                sb.Append("Field catalog entries: ")
                    .Append(string.Join(", ", byKind.Select(kv => $"{kv.Key} {kv.Value}")))
                    .Append(". ");
            }
            sb.AppendLine();
            sb.AppendLine();
        }

        var noted = shots.Where(s => !string.IsNullOrWhiteSpace(s.Notes)).TakeLast(8).ToList();
        if (noted.Count > 0)
        {
            sb.AppendLine("Field Notes");
            sb.AppendLine();
            foreach (var s in noted)
            {
                var note = (s.Notes ?? "").Trim();
                if (note.Length > 200) note = note[..200];
                note = note.Replace('\n', ' ');
                sb.Append("- ")
                    .Append(s.FromStation)
                    .Append("->")
                    .Append(s.ToStation)
                    .Append(": ")
                    .AppendLine(note);
            }
            sb.AppendLine();
        }

        sb.AppendLine("Safety & Data Quality");
        sb.AppendLine();
        sb.Append("Figures are derived from the on-device survey file only. ")
            .AppendLine("Cross-check depths, loop closure, and geo/bio identifications against original field notes before publication.");

        var text = sb.ToString();
        return text.Length <= TripEndNarrativeMaxChars ? text : text[..TripEndNarrativeMaxChars];
    }

    public static string BuildOfflineTripNarrativeChatAnswer(CaveProjectDocument project)
    {
        var full = BuildOfflineTripEndNarrative(project);
        if (full.Length <= 900) return full;
        return full[..880].TrimEnd() + "... Open **Generate AI Report** in the project list for the full expedition summary.";
    }

    private static int CountAnalyzedGeoSamples(CaveProjectDocument project)
    {
        if (project.Rocks is not { ValueKind: JsonValueKind.Array } rocks)
            return 0;
        var count = 0;
        foreach (var rock in rocks.EnumerateArray())
        {
            if (rock.TryGetProperty("isAnalyzed", out var analyzed) && analyzed.ValueKind == JsonValueKind.True)
                count++;
        }
        return count;
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

    private static string BuildLoopMisclosureSummary(IReadOnlyList<SurveyLoopDescriptor> loops)
    {
        if (loops.Count == 0)
            return "No traverse loops detected yet — log closing shots to known stations to measure misclosure.";

        var worst = loops.OrderByDescending(l => l.MisclosureMeters).First();
        var inv = CultureInfo.InvariantCulture;
        var severity = LoopClosureSeverityClassifier.SeverityCaption(
            LoopClosureSeverityClassifier.Classify(worst.MisclosureMeters));
        var headline = $"{loops.Count} loop{(loops.Count == 1 ? "" : "s")} — worst |Delta|={worst.MisclosureMeters.ToString("0.##", inv)} m ({severity})";
        var ppm = worst.TotalLegLength > 0
            ? (worst.MisclosureMeters / worst.TotalLegLength) * 1_000_000
            : 0;
        var cycle = string.Join("->", worst.Stations);
        var sb = new StringBuilder(headline);
        sb.Append(". Worst cycle: ").Append(cycle);
        sb.Append("; path ~").Append(worst.TotalLegLength.ToString("0.#", inv)).Append(" m");
        if (ppm > 0)
            sb.Append(", ~").Append(ppm.ToString("0", inv)).Append(" ppm");
        sb.Append('.');
        if (loops.Count > 1)
        {
            var total = loops.Sum(l => l.MisclosureMeters);
            sb.Append(" Total |Delta| across ").Append(loops.Count).Append(" loop(s): ~")
                .Append(total.ToString("0.##", inv)).Append(" m.");
        }
        return sb.ToString();
    }
}