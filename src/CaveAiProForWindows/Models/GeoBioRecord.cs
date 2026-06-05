using System.Collections.Generic;

namespace CaveAiProForWindows.Models;

/// <summary>
/// One unified scientific record — a geological sample (rock / mineral / formation) or a biological observation
/// (organism / flora / fauna). Built from Android <c>rocks</c>, <c>fieldCatalogEntries</c>, or the newer
/// <c>geoBioRecords</c> array. The <see cref="Category"/> drives placement in the GEO &amp; BIO tab.
/// </summary>
public sealed class GeoBioRecord
{
    public GeoBioRecord(
        GeoBioCategory category,
        string title,
        string sourceLabel,
        string? station,
        string? caveAiAnalysisText,
        IReadOnlyList<string> imageReferences,
        string detailsSummary,
        string? coordinatesSummary)
    {
        Category = category;
        Title = string.IsNullOrWhiteSpace(title) ? "(unnamed)" : title.Trim();
        SourceLabel = sourceLabel;
        Station = station;
        CaveAiAnalysisText = caveAiAnalysisText;
        ImageReferences = imageReferences;
        DetailsSummary = detailsSummary;
        CoordinatesSummary = coordinatesSummary;
    }

    public GeoBioCategory Category { get; }

    public string Title { get; }

    /// <summary>Origin in JSON for diagnostics (e.g. <c>rocks[3]</c>, <c>fieldCatalogEntries[1]</c>, <c>geoBioRecords[7]</c>).</summary>
    public string SourceLabel { get; }

    public string? Station { get; }

    /// <summary>Free-form Cave AI analysis text (full, untruncated for the GEO &amp; BIO panel).</summary>
    public string? CaveAiAnalysisText { get; }

    /// <summary>Path / URI references — resolve through <see cref="Services.MapAssetOpener.TryEnsureLocalFilePath"/>.</summary>
    public IReadOnlyList<string> ImageReferences { get; }

    /// <summary>Compact rendering of the JSON object's other fields (used as fallback caption).</summary>
    public string DetailsSummary { get; }

    public string? CoordinatesSummary { get; }
}

/// <summary>High-level type used to split records across the Geology / Biology UI tabs.</summary>
public enum GeoBioCategory
{
    Rock,
    Organism,
    Mixed,
    Other,
}
