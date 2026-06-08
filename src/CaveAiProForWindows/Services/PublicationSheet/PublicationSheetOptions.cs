using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.PublicationSheet;

public enum PublicationSheetElevationSource
{
    LongProfile,
    Section,
}

public sealed class PublicationSheetOptions
{
    public PublicationSheetElevationSource ElevationSource { get; init; } = PublicationSheetElevationSource.LongProfile;

    public bool Include3DOverview { get; init; } = true;

    public MapExportQuality Quality { get; init; } = MapExportQuality.Standard;

    public int SheetWidth { get; init; } = PublicationSheetComposer.DefaultSheetWidthDip;

    public int SheetHeight { get; init; } = PublicationSheetComposer.DefaultSheetHeightDip;

    public PublicationSheetOptions WithQuality(MapExportQuality quality) =>
        new()
        {
            ElevationSource = ElevationSource,
            Include3DOverview = Include3DOverview,
            Quality = quality,
            SheetWidth = SheetWidth,
            SheetHeight = SheetHeight,
        };
}

public sealed class PublicationSheetStats
{
    public double SurveyLengthMetres { get; init; }

    public double VerticalSpanMetres { get; init; }

    public int StationCount { get; init; }

    public int TraverseLegCount { get; init; }

    public static PublicationSheetStats? TryFromProject(CaveProjectDocument? project)
    {
        if (project == null)
            return null;

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        if (coords.Count == 0)
            return null;

        var trav = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        var sumTape = trav.Sum(s => (double)s.Distance);
        var spanZ = coords.Values.Max(c => c.Z) - coords.Values.Min(c => c.Z);
        return new PublicationSheetStats
        {
            SurveyLengthMetres = sumTape,
            VerticalSpanMetres = spanZ,
            StationCount = coords.Count,
            TraverseLegCount = trav.Count,
        };
    }
}

public sealed class PublicationSheetResult
{
    public PublicationSheetResult(byte[]? pngBytes, string? errorMessage = null)
    {
        PngBytes = pngBytes;
        ErrorMessage = errorMessage;
    }

    public byte[]? PngBytes { get; }

    public string? ErrorMessage { get; }

    public bool IsSuccess => PngBytes is { Length: > 0 };
}
