using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Models;

/// <summary>One map / overlay / raster URI or path from project JSON for the Maps tab.</summary>
public sealed class MapAssetRow
{
    public MapAssetRow(string projectName, string category, string uriOrPath, string? sourceFile)
    {
        ProjectName = projectName;
        Category = category;
        UriOrPath = uriOrPath;
        SourceFile = sourceFile ?? "";
        MapTypeLabel = MapAssetKindClassifier.GetMapTypeLabel(projectName, category, uriOrPath);
        DetailLabel = MapAssetKindClassifier.GetDetailLabel(projectName, category, uriOrPath);
    }

    public string ProjectName { get; }
    public string Category { get; }
    public string UriOrPath { get; }
    public string SourceFile { get; }

    /// <summary>Short cartography-oriented label (library / LiDAR / mesh / sketch / photo, etc.).</summary>
    public string MapTypeLabel { get; }

    /// <summary>Unique per-row description (which Android map surface / list index where applicable).</summary>
    public string DetailLabel { get; }
}
