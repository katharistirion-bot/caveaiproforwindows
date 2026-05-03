namespace CaveAiProForWindows.Models;

/// <summary>One geo/bio sample or field-catalog line from Android JSON (<c>rocks</c> / <c>fieldCatalogEntries</c>).</summary>
public sealed class BioMineralCatalogRow
{
    public BioMineralCatalogRow(
        string caveName,
        string source,
        string category,
        string title,
        string species,
        string mineral,
        string photoUri,
        string coordinatesSummary,
        string details)
    {
        CaveName = caveName;
        Source = source;
        Category = category;
        Title = title;
        Species = species;
        Mineral = mineral;
        PhotoUri = photoUri;
        CoordinatesSummary = coordinatesSummary;
        Details = details;
    }

    public string CaveName { get; }
    /// <summary><c>rocks</c> or <c>fieldCatalog</c>.</summary>
    public string Source { get; }
    /// <summary>Heuristic category label (biotic / mineral / other).</summary>
    public string Category { get; }
    public string Title { get; }
    public string Species { get; }
    public string Mineral { get; }
    public string PhotoUri { get; }
    public string CoordinatesSummary { get; }
    public string Details { get; }
}
