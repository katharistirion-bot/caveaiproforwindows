using System.Globalization;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Compare active survey project stats vs linked <c>referenceCatalogLink</c> or index entry.</summary>
public static class ReferenceSurveyCompareService
{
    public static bool TryBuildReport(
        CaveProjectDocument project,
        out string report,
        ReferenceCaveIndexEntry? indexEntry = null)
    {
        report = "";
        if (!ReferenceSurveyLinkService.TryGetLink(project, out var link) || link == null)
            return false;

        indexEntry ??= ReferenceCatalogFetchService.TryLoadCachedIndexEntries()
            .FirstOrDefault(e => string.Equals(e.Id, link.Id, StringComparison.OrdinalIgnoreCase));

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("SURVEY vs REFERENCE COMPARE");
        sb.AppendLine(new string('─', 48));
        sb.AppendLine($"Project: {project.Name}");
        sb.AppendLine($"Reference link: {link.Name} ({link.Id})");
        if (!string.IsNullOrWhiteSpace(link.Country))
            sb.AppendLine($"Country: {link.Country}");
        sb.AppendLine();

        sb.AppendLine("[Survey — this PC]");
        if (project.Lat is { } sla && project.Lon is { } slo)
            sb.AppendLine($"  Entrance: {sla.ToString("F5", inv)}, {slo.ToString("F5", inv)}");
        else
            sb.AppendLine("  Entrance: (no GPS on project)");

        var (_, totalTape, zSpan, _) = SurveyPlanHudStats.Compute(project);
        sb.AppendLine($"  Traverse tape sum: {totalTape.ToString("0.##", inv)} m");
        if (zSpan > 0)
            sb.AppendLine($"  Vertical Z span: {zSpan.ToString("0.##", inv)} m");

        sb.AppendLine();
        sb.AppendLine("[Reference catalog]");
        var refLat = indexEntry?.Lat ?? link.Lat;
        var refLon = indexEntry?.Lon ?? link.Lon;
        sb.AppendLine($"  Coords: {refLat.ToString("F5", inv)}, {refLon.ToString("F5", inv)}");
        var refDepth = indexEntry?.DepthM;
        var refLength = indexEntry?.LengthM;
        if (refDepth is > 0)
            sb.AppendLine($"  Catalog depth: {refDepth.Value.ToString("0.#", inv)} m");
        if (refLength is > 0)
            sb.AppendLine($"  Catalog length: {refLength.Value.ToString("0.#", inv)} m");
        if (indexEntry != null)
            sb.AppendLine($"  Badge: {(indexEntry.Rich ? "Surveyed" : "Sparse")}");

        sb.AppendLine();
        sb.AppendLine("[Delta]");

        if (project.Lat is { } pla && project.Lon is { } plo)
        {
            var distM = GeoHaversine.DistanceKm(pla, plo, refLat, refLon) * 1000.0;
            sb.AppendLine($"  Entrance distance: {distM.ToString("0.#", inv)} m ({(distM / 1000.0).ToString("0.###", inv)} km)");
        }
        else
            sb.AppendLine("  Entrance distance: — (survey has no entrance GPS)");

        if (refDepth is > 0 && zSpan > 0)
        {
            var depthDelta = Math.Abs(refDepth.Value - zSpan);
            sb.AppendLine($"  Depth vs Z span delta: {depthDelta.ToString("0.#", inv)} m (catalog depth vs survey Z span)");
        }

        if (refLength is > 0 && totalTape > 0)
        {
            var lenDelta = Math.Abs(refLength.Value - totalTape);
            sb.AppendLine($"  Length vs traverse delta: {lenDelta.ToString("0.#", inv)} m (catalog length vs traverse tape sum)");
        }

        report = sb.ToString().TrimEnd();
        return true;
    }
}
