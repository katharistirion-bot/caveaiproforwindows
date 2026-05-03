namespace CaveAiProForWindows.Models;

/// <summary>One cave (project) row for the registry grid — from loaded <c>data.json</c> / backup.</summary>
public sealed class CaveRegistryRow
{
    public CaveRegistryRow(
        string caveName,
        string date,
        string? lat,
        string? lon,
        double altM,
        int traverseLegs,
        int totalShots,
        int rocksCount,
        int fieldCatalogCount,
        string? libraryLinkId,
        string? coverUri,
        string? sourceFile)
    {
        CaveName = caveName;
        Date = date;
        Lat = lat ?? "";
        Lon = lon ?? "";
        AltM = altM;
        TraverseLegs = traverseLegs;
        TotalShots = totalShots;
        RocksCount = rocksCount;
        FieldCatalogCount = fieldCatalogCount;
        LibraryLinkId = libraryLinkId ?? "";
        CoverUri = coverUri ?? "";
        SourceFile = sourceFile ?? "";
    }

    public string CaveName { get; }
    public string Date { get; }
    public string Lat { get; }
    public string Lon { get; }
    public double AltM { get; }
    public int TraverseLegs { get; }
    public int TotalShots { get; }
    public int RocksCount { get; }
    public int FieldCatalogCount { get; }
    public string LibraryLinkId { get; }

    /// <summary>Optional cover image (https URL or local path) for library-style cards.</summary>
    public string CoverUri { get; }

    /// <summary>Backup / JSON filename when several files were opened together.</summary>
    public string SourceFile { get; }
}
