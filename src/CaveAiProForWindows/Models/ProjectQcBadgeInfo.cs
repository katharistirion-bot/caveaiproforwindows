namespace CaveAiProForWindows.Models;

/// <summary>Lightweight QC summary for project list rows (from <see cref="Services.TraverseQcStats"/>).</summary>
public sealed class ProjectQcBadgeInfo
{
    public static ProjectQcBadgeInfo Empty { get; } = new();

    public string Glyph { get; init; } = "";
    public string Summary { get; init; } = "";
    public string AccentBrushHex { get; init; } = "#9AA0A8";
}
