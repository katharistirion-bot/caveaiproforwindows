using System.Printing;

namespace CaveAiProForWindows.Services;

/// <summary>User-selected paper size and orientation for <see cref="PrintLayoutService"/>.</summary>
public sealed class PrintLayoutOptions
{
    public PrintPaperSize PaperSize { get; init; } = PrintPaperSize.A4;

    public PageOrientation Orientation { get; init; } = PageOrientation.Landscape;

    /// <summary>When true, scale the map uniformly to fit the printable map frame; otherwise use 1:1 pixel size (centered, clipped if needed).</summary>
    public bool FitMapToPage { get; init; } = true;
}
