using System.Windows.Media;

namespace CaveAiProForWindows.Services;

/// <summary>Frozen geometries and inks for cave sketch stamps.</summary>
public static class SketchSymbolDefinitions
{
    public const double ViewboxNominalSize = 24;

    /// <summary>On-canvas approximate width/height of the stamped symbol (DIP).</summary>
    public const double StampDisplaySize = 28;

    private static readonly Brush RockStroke = Brushes.Black;
    private static readonly Brush RockFill = new SolidColorBrush(Color.FromRgb(0x8B, 0x7B, 0x6E));
    private static readonly Brush WaterStroke = Brushes.Black;
    private static readonly Brush WaterFill = new SolidColorBrush(Color.FromArgb(0xBB, 0x3D, 0xA5, 0xD9));
    private static readonly Brush StalStroke = Brushes.Black;
    private static readonly Brush StalFill = new SolidColorBrush(Color.FromRgb(0xC4, 0xAA, 0x7F));
    private static readonly Brush FlowStroke = Brushes.Black;
    private static readonly Brush FlowFill = new SolidColorBrush(Color.FromRgb(0xE8, 0xD4, 0xB0));
    private static readonly Brush SandStroke = Brushes.Black;
    private static readonly Brush SandFill = new SolidColorBrush(Color.FromArgb(0xE0, 0xC2, 0xB2, 0x80));
    private static readonly Brush PitStroke = Brushes.Black;
    private static readonly Brush PitFill = new SolidColorBrush(Color.FromArgb(0x90, 0x30, 0x30, 0x30));
    private static readonly Brush AidStroke = Brushes.Black;
    private static readonly Brush AidFill = new SolidColorBrush(Color.FromArgb(0xC0, 0x8B, 0x7B, 0x6E));
    private static readonly Brush ChokeStroke = Brushes.Black;
    private static readonly Brush ChokeFill = new SolidColorBrush(Color.FromRgb(0x6E, 0x65, 0x5A));
    private static readonly Brush BreakdownStroke = Brushes.Black;
    private static readonly Brush BreakdownFill = new SolidColorBrush(Color.FromRgb(0x9A, 0x8A, 0x7C));
    private static readonly Brush ColumnStroke = Brushes.Black;
    private static readonly Brush ColumnFill = new SolidColorBrush(Color.FromRgb(0xD8, 0xC8, 0xA8));
    private static readonly Brush HelictiteStroke = Brushes.Black;
    private static readonly Brush HelictiteFill = new SolidColorBrush(Color.FromRgb(0xE0, 0xC9, 0x98));

    private static Geometry? _gRock;
    private static Geometry? _gWater;
    private static Geometry? _gStal;
    private static Geometry? _gFlow;
    private static Geometry? _gSand;
    private static Geometry? _gPit;
    private static Geometry? _gAid;
    private static Geometry? _gChoke;
    private static Geometry? _gBreakdown;
    private static Geometry? _gColumn;
    private static Geometry? _gHelictite;
    private static Geometry? _gAven;
    private static Geometry? _gStream;
    private static Geometry? _gMud;
    private static Geometry? _gArchaeology;
    private static Geometry? _gGuano;
    private static Geometry? _gCrack;

    private static readonly Brush AvenStroke = Brushes.Black;
    private static readonly Brush AvenFill = new SolidColorBrush(Color.FromArgb(0x90, 0x30, 0x30, 0x30));
    private static readonly Brush StreamStroke = Brushes.Black;
    private static readonly Brush StreamFill = new SolidColorBrush(Color.FromArgb(0xBB, 0x3D, 0xA5, 0xD9));
    private static readonly Brush MudStroke = Brushes.Black;
    private static readonly Brush MudFill = new SolidColorBrush(Color.FromArgb(0xE0, 0x8B, 0x6F, 0x47));
    private static readonly Brush ArchaeologyStroke = Brushes.Black;
    private static readonly Brush ArchaeologyFill = new SolidColorBrush(Color.FromRgb(0xD8, 0xC8, 0xA8));
    private static readonly Brush GuanoStroke = Brushes.Black;
    private static readonly Brush GuanoFill = new SolidColorBrush(Color.FromRgb(0x6E, 0x5A, 0x42));
    private static readonly Brush CrackStroke = Brushes.Black;
    private static readonly Brush CrackFill = Brushes.Transparent;

    static SketchSymbolDefinitions()
    {
        _gRock = Geometry.Parse(SketchSymbolPathMarkup.RockBlock);
        _gWater = Geometry.Parse(SketchSymbolPathMarkup.WaterPool);
        _gStal = Geometry.Parse(SketchSymbolPathMarkup.StalactiteSpeleothem);
        _gFlow = Geometry.Parse(SketchSymbolPathMarkup.FlowstoneCurtain);
        _gSand = Geometry.Parse(SketchSymbolPathMarkup.SandMudFloor);
        _gPit = Geometry.Parse(SketchSymbolPathMarkup.PitOrShaft);
        _gAid = Geometry.Parse(SketchSymbolPathMarkup.FixedAid);
        _gChoke = Geometry.Parse(SketchSymbolPathMarkup.Choke);
        _gBreakdown = Geometry.Parse(SketchSymbolPathMarkup.BreakdownPile);
        _gColumn = Geometry.Parse(SketchSymbolPathMarkup.ColumnPillar);
        _gHelictite = Geometry.Parse(SketchSymbolPathMarkup.Helictite);
        _gAven = Geometry.Parse(SketchSymbolPathMarkup.AvenShaftUp);
        _gStream = Geometry.Parse(SketchSymbolPathMarkup.SubterraneanStream);
        _gMud = Geometry.Parse(SketchSymbolPathMarkup.MudDeposit);
        _gArchaeology = Geometry.Parse(SketchSymbolPathMarkup.ArchaeologyBones);
        _gGuano = Geometry.Parse(SketchSymbolPathMarkup.BatGuano);
        _gCrack = Geometry.Parse(SketchSymbolPathMarkup.CrackFissure);
        _gRock.Freeze();
        _gWater.Freeze();
        _gStal.Freeze();
        _gFlow.Freeze();
        _gSand.Freeze();
        _gPit.Freeze();
        _gAid.Freeze();
        _gChoke.Freeze();
        _gBreakdown.Freeze();
        _gColumn.Freeze();
        _gHelictite.Freeze();
        _gAven.Freeze();
        _gStream.Freeze();
        _gMud.Freeze();
        _gArchaeology.Freeze();
        _gGuano.Freeze();
        _gCrack.Freeze();
    }


    public static SketchSymbolInk Get(SketchEditorSymbolKind kind) =>
        kind switch
        {
            SketchEditorSymbolKind.RockBlock => new SketchSymbolInk(_gRock!, RockStroke, RockFill),
            SketchEditorSymbolKind.WaterPool => new SketchSymbolInk(_gWater!, WaterStroke, WaterFill),
            SketchEditorSymbolKind.FlowstoneCurtain => new SketchSymbolInk(_gFlow!, FlowStroke, FlowFill),
            SketchEditorSymbolKind.SandMudFloor => new SketchSymbolInk(_gSand!, SandStroke, SandFill),
            SketchEditorSymbolKind.PitOrShaft => new SketchSymbolInk(_gPit!, PitStroke, PitFill),
            SketchEditorSymbolKind.FixedAid => new SketchSymbolInk(_gAid!, AidStroke, AidFill),
            SketchEditorSymbolKind.Choke => new SketchSymbolInk(_gChoke!, ChokeStroke, ChokeFill),
            SketchEditorSymbolKind.BreakdownPile => new SketchSymbolInk(_gBreakdown!, BreakdownStroke, BreakdownFill),
            SketchEditorSymbolKind.ColumnPillar => new SketchSymbolInk(_gColumn!, ColumnStroke, ColumnFill),
            SketchEditorSymbolKind.Helictite => new SketchSymbolInk(_gHelictite!, HelictiteStroke, HelictiteFill),
            SketchEditorSymbolKind.AvenShaftUp => new SketchSymbolInk(_gAven!, AvenStroke, AvenFill),
            SketchEditorSymbolKind.SubterraneanStream => new SketchSymbolInk(_gStream!, StreamStroke, StreamFill),
            SketchEditorSymbolKind.MudDeposit => new SketchSymbolInk(_gMud!, MudStroke, MudFill),
            SketchEditorSymbolKind.ArchaeologyBones => new SketchSymbolInk(_gArchaeology!, ArchaeologyStroke, ArchaeologyFill),
            SketchEditorSymbolKind.BatGuano => new SketchSymbolInk(_gGuano!, GuanoStroke, GuanoFill),
            SketchEditorSymbolKind.CrackFissure => new SketchSymbolInk(_gCrack!, CrackStroke, CrackFill),
            _ => new SketchSymbolInk(_gStal!, StalStroke, StalFill),
        };

    /// <summary>Nominal on-screen footprint when Android omits an explicit world-span — multiplied by JSON <c>scale</c>.</summary>
    public const double DefaultSymbolWorldSpanMetres = 1.15;

}

/// <summary>UIS-aligned labels for the Sketch Editor symbol palette and legend.</summary>
public static class SketchSymbolPaletteCatalog
{
    public sealed record Entry(SketchEditorSymbolKind Kind, string ShortLabel, string UisName, string Tooltip);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new(SketchEditorSymbolKind.RockBlock, "Rk", "Boulder / block (UIS)", "Rock block or boulder (UIS boulder)"),
        new(SketchEditorSymbolKind.WaterPool, "Wp", "Water pool (UIS)", "Standing water pool (UIS pool_water)"),
        new(SketchEditorSymbolKind.StalactiteSpeleothem, "St", "Stalactite (UIS)", "Stalactite / speleothem (UIS stalactite)"),
        new(SketchEditorSymbolKind.FlowstoneCurtain, "Fl", "Curtain / drapery (UIS)", "Flowstone curtain (UIS curtain)"),
        new(SketchEditorSymbolKind.SandMudFloor, "Sd", "Sand floor (UIS)", "Sand or sediment blanket (UIS sand)"),
        new(SketchEditorSymbolKind.PitOrShaft, "Pt", "Pitch / pit (UIS)", "Vertical pitch or pit (UIS pit)"),
        new(SketchEditorSymbolKind.AvenShaftUp, "Av", "Aven / shaft up (UIS)", "Aven or upward shaft (UIS aven)"),
        new(SketchEditorSymbolKind.FixedAid, "Ld", "Fixed rope / ladder (UIS)", "Rope, ladder, or fixed aid (UIS fixed_rope)"),
        new(SketchEditorSymbolKind.Choke, "Ch", "Choke (UIS)", "Narrow choke or constriction (UIS choke)"),
        new(SketchEditorSymbolKind.BreakdownPile, "Br", "Breakdown (UIS)", "Breakdown or boulder pile (UIS breakdown)"),
        new(SketchEditorSymbolKind.ColumnPillar, "Co", "Column (UIS)", "Stalagmite-stalactite column (UIS column)"),
        new(SketchEditorSymbolKind.Helictite, "He", "Helictite (UIS)", "Eccentric helictite (UIS helictite)"),
        new(SketchEditorSymbolKind.SubterraneanStream, "Sr", "Stream (UIS)", "Subterranean stream (UIS stream)"),
        new(SketchEditorSymbolKind.MudDeposit, "Md", "Mud (UIS)", "Mud deposit (UIS mud)"),
        new(SketchEditorSymbolKind.ArchaeologyBones, "Ar", "Archaeology (UIS)", "Bones / archaeological find (UIS bones)"),
        new(SketchEditorSymbolKind.BatGuano, "Bn", "Bat guano (UIS)", "Bat guano accumulation (UIS guano)"),
        new(SketchEditorSymbolKind.CrackFissure, "Cr", "Crack / fissure", "Rock crack or fissure (estimated passage edge)"),
    ];

    public static string GetUisDisplayName(SketchEditorSymbolKind kind) =>
        All.FirstOrDefault(e => e.Kind == kind)?.UisName ?? kind.ToString();

    public static string GetTooltip(SketchEditorSymbolKind kind) =>
        All.FirstOrDefault(e => e.Kind == kind)?.Tooltip ?? kind.ToString();
}

/// <summary>Shared frozen geometry plus stroke/fill brushes for stamping and palette.</summary>
public readonly struct SketchSymbolInk
{
    public Geometry Geometry { get; }
    public Brush Stroke { get; }
    public Brush Fill { get; }

    public SketchSymbolInk(Geometry geometry, Brush stroke, Brush fill)
    {
        Geometry = geometry;
        Stroke = stroke;
        Fill = fill;
    }
}
