using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Human-readable inventory of Android map symbols for office reports and analytics.</summary>
public static class MapSymbolInventoryFormatter
{
    public static string BuildInventoryText(CaveProjectDocument project)
    {
        var plan = SurveyStationGeometry.ParsePlanMapSymbols(project);
        var section = SurveyStationGeometry.ParseSectionMapSymbols(project);
        if (plan.Count == 0 && section.Count == 0)
            return "Map symbols: none in JSON.";

        var sb = new StringBuilder();
        AppendGroup(sb, "Plan view (viewMode 0)", plan);
        AppendGroup(sb, "Section / profile view (viewMode 1)", section);
        return sb.ToString().TrimEnd();
    }

    private static void AppendGroup(StringBuilder sb, string heading, IReadOnlyList<SurveyStationGeometry.PlanMapSymbol> symbols)
    {
        if (symbols.Count == 0)
            return;

        sb.AppendLine($"{heading}: {symbols.Count} stamp(s)");
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in symbols)
        {
            var label = MapSymbolIconResolver.ExportLabel(sym);
            counts.TryGetValue(label, out var n);
            counts[label] = n + 1;
        }

        foreach (var kv in counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"  · {kv.Key}: {kv.Value}");
    }
}
