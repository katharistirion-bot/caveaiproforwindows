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

    private static Geometry? _gRock;
    private static Geometry? _gWater;
    private static Geometry? _gStal;
    private static Geometry? _gFlow;
    private static Geometry? _gSand;
    private static Geometry? _gPit;
    private static Geometry? _gAid;

    static SketchSymbolDefinitions()
    {
        _gRock = Geometry.Parse(SketchSymbolPathMarkup.RockBlock);
        _gWater = Geometry.Parse(SketchSymbolPathMarkup.WaterPool);
        _gStal = Geometry.Parse(SketchSymbolPathMarkup.StalactiteSpeleothem);
        _gFlow = Geometry.Parse(SketchSymbolPathMarkup.FlowstoneCurtain);
        _gSand = Geometry.Parse(SketchSymbolPathMarkup.SandMudFloor);
        _gPit = Geometry.Parse(SketchSymbolPathMarkup.PitOrShaft);
        _gAid = Geometry.Parse(SketchSymbolPathMarkup.FixedAid);
        _gRock.Freeze();
        _gWater.Freeze();
        _gStal.Freeze();
        _gFlow.Freeze();
        _gSand.Freeze();
        _gPit.Freeze();
        _gAid.Freeze();
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
            _ => new SketchSymbolInk(_gStal!, StalStroke, StalFill),
        };

    /// <summary>Nominal on-screen footprint when Android omits an explicit world-span — multiplied by JSON <c>scale</c>.</summary>
    public const double DefaultSymbolWorldSpanMetres = 1.15;

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
