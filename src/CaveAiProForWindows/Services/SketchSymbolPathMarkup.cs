namespace CaveAiProForWindows.Services;

/// <summary>
/// Path mini-language for stamped geometry only (~24×24). Palette uses pure XAML in
/// <c>SketchSymbolPaletteGlyphs.xaml</c>.
/// Coordinate pairs use spaces (not commas) so <see cref="System.Windows.Media.Geometry.Parse(string)"/> is
/// predictable on locales such as el-GR.
/// </summary>
public static class SketchSymbolPathMarkup
{
    public const string RockBlock =
        "M 3 17 L 7 9 L 12 7 L 20 10 L 22 17 L 16 21 L 8 21 Z";

    /// <summary>Simple pool silhouette (polygon only — avoids curve parsing quirks).</summary>
    public const string WaterPool =
        "M 2 17 L 4 13 L 8 11 L 12 10 L 17 11 L 21 14 L 22 17 L 20 21 L 12 22 L 4 21 Z";

    public const string StalactiteSpeleothem =
        "M 12 3 L 16 17 L 20 21 L 12 19 L 4 21 L 8 17 Z";

    /// <summary>Wavy curtain — flowstone / drapery.</summary>
    public const string FlowstoneCurtain =
        "M 4 6 L 7 18 L 10 8 L 13 19 L 16 9 L 19 18 L 20 6 L 18 5 L 15 14 L 12 5 L 9 14 L 6 5 Z";

    /// <summary>Low mound — sand / mud / sediment.</summary>
    public const string SandMudFloor =
        "M 3 16 L 6 13 L 10 12 L 15 13 L 21 16 L 21 19 L 3 19 Z";

    /// <summary>Hex marker — shaft / pitch opening.</summary>
    public const string PitOrShaft =
        "M 12 5 L 17 9 L 17 15 L 12 19 L 7 15 L 7 9 Z";

    /// <summary>Rung ladder / rope vertical.</summary>
    public const string FixedAid =
        "M 10 4 L 14 4 L 14 20 L 10 20 Z M 10 7 L 14 7 M 10 11 L 14 11 M 10 15 L 14 15";
}
