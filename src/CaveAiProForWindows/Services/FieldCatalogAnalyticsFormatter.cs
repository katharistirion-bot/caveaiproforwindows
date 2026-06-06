using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Summary counts for Android field catalog rows (by <see cref="FieldCatalogEntryKind"/>).</summary>
public static class FieldCatalogAnalyticsFormatter
{
    public static string BuildSummaryLine(IReadOnlyList<GeoBioRecord> records)
    {
        var catalog = records.Where(r => r.IsFieldCatalogEntry).ToList();
        if (catalog.Count == 0)
            return "Field catalog: no rows in this project JSON.";

        var sb = new StringBuilder();
        sb.Append($"Field catalog: {catalog.Count} row(s)");
        foreach (var g in catalog
                     .GroupBy(r => r.FieldKind ?? FieldCatalogEntryKind.Unknown)
                     .OrderBy(g => g.Key))
        {
            sb.Append(" · ");
            sb.Append(FieldCatalogEntryKindMapper.DisplayLabel(g.Key));
            sb.Append(' ');
            sb.Append(g.Count());
        }

        var bio = catalog.Count(r => r.FieldKind is FieldCatalogEntryKind.Biota or FieldCatalogEntryKind.Bacteria or FieldCatalogEntryKind.Plant);
        var geo = catalog.Count(r => r.FieldKind == FieldCatalogEntryKind.Mineral);
        if (bio > 0 || geo > 0)
            sb.Append($" — {geo} geology, {bio} biology/microbiology");

        return sb.ToString();
    }
}
