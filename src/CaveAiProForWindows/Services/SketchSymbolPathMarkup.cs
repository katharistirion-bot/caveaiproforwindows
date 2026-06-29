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

    /// <summary>Pinch / hourglass — choke point.</summary>
    public const string Choke =
        "M 4 5 L 20 5 L 17 12 L 20 19 L 4 19 L 7 12 Z";

    /// <summary>Scattered blocks — breakdown pile.</summary>
    public const string BreakdownPile =
        "M 3 19 L 20 19 M 5 19 L 7 15 L 10 19 M 9 18 L 12 13 L 15 18 M 14 17 L 17 14 L 19 17";

    /// <summary>Floor-to-ceiling column.</summary>
    public const string ColumnPillar =
        "M 9 3 L 15 3 L 16 7 L 15 21 L 9 21 L 8 7 Z M 7 21 L 17 21";

    /// <summary>Curved eccentric formation.</summary>
    public const string Helictite =
        "M 12 3 L 15 8 L 11 12 L 17 16 L 10 20 L 14 14 L 9 10 L 13 6 Z";

    /// <summary>Upward shaft / aven (inverted pit).</summary>
    public const string AvenShaftUp =
        "M 7 19 L 12 5 L 17 19 L 14 19 L 12 11 L 10 19 Z";

    /// <summary>Flowing stream with arrowhead.</summary>
    public const string SubterraneanStream =
        "M 3 12 L 18 12 M 14 9 L 18 12 L 14 15";

    /// <summary>Mud dots (UIS mud).</summary>
    public const string MudDeposit =
        "M 5 16 L 9 16 M 7 14 L 7 18 M 12 15 L 16 15 M 14 13 L 14 17 M 17 17 L 21 17 M 19 15 L 19 19";

    /// <summary>Crossed bones — archaeology.</summary>
    public const string ArchaeologyBones =
        "M 5 19 L 19 5 M 8 16 L 11 13 M 13 11 L 16 8 M 8 8 L 11 11 M 13 13 L 16 16";

    /// <summary>Guano scatter.</summary>
    public const string BatGuano =
        "M 4 14 L 8 14 M 6 12 L 6 16 M 11 16 L 15 16 M 13 14 L 13 18 M 16 12 L 20 12 M 18 10 L 18 14";

    /// <summary>Crack / fissure zigzag.</summary>
    public const string CrackFissure =
        "M 12 4 L 9 10 L 15 14 L 10 20";
}
