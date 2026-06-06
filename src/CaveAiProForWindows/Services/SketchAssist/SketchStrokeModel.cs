namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>One polyline stroke in Android survey metres (plan frame: X east, Y north).</summary>
public sealed class SketchStrokeModel
{
    /// <summary>Vertex chain in survey metres.</summary>
    public List<(float X, float Y)> Points { get; init; } = new();

    public bool Closed { get; init; }

    /// <summary>Provenance: <c>designLayer</c>, <c>procedural</c>, etc.</summary>
    public string Source { get; init; } = "designLayer";

    public DesignLayerInkMetadata? Metadata { get; init; }

    public bool IsDrawable => Points.Count >= 2;
}

/// <summary>User-stamped symbol anchor in survey metres (Sketch Editor symbol tool).</summary>
public sealed class SketchSymbolStampModel
{
    public float SurveyX { get; init; }

    public float SurveyY { get; init; }

    public SketchEditorSymbolKind Kind { get; init; }
}
