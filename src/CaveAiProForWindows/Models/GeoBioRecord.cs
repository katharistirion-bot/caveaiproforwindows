using System.Collections.Generic;

namespace CaveAiProForWindows.Models;

/// <summary>
/// One unified scientific record — a geological sample (rock / mineral / formation) or a biological observation
/// (organism / flora / fauna / bacteria / fungi). Built from Android <c>rocks</c>, <c>fieldCatalogEntries</c>,
/// or <c>geoBioRecords</c>. The <see cref="Category"/> drives placement in the GEO &amp; BIO tab.
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

    /// <summary>Origin in JSON (e.g. <c>fieldCatalogEntries[3]</c>).</summary>
    public string SourceLabel { get; }

    public string? Station { get; }

    public string? CaveAiAnalysisText { get; }

    public IReadOnlyList<string> ImageReferences { get; }

    public string DetailsSummary { get; }

    public string? CoordinatesSummary { get; }

    /// <summary>Android field-catalog row type when sourced from <c>fieldCatalogEntries</c>.</summary>
    public FieldCatalogEntryKind? FieldKind { get; init; }

    public string? ScientificName { get; init; }

    /// <summary>Taxonomic or thematic group (e.g. Arachnida, Chiroptera, mineral crust).</summary>
    public string? TaxonomicGroup { get; init; }

    public string? Abundance { get; init; }

    public string? LifeStage { get; init; }

    public string? Microhabitat { get; init; }

    public string? LocationDetail { get; init; }

    public string? BehaviorNotes { get; init; }

    public string? IdConfidence { get; init; }

    public string? Substrate { get; init; }

    public string? ShortNote { get; init; }

    public string? RecordedAt { get; init; }

    public string? EntryId { get; init; }

    public bool IsFieldCatalogEntry => SourceLabel.StartsWith("fieldCatalogEntries", StringComparison.Ordinal);
}

/// <summary>High-level type used to split records across the Geology / Biology UI tabs.</summary>
public enum GeoBioCategory
{
    Rock,
    Organism,
    Mixed,
    Other,
}
