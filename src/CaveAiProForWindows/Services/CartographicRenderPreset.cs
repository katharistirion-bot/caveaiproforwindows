namespace CaveAiProForWindows.Services;

/// <summary>Visual rendering preset for survey maps — field work vs print/export.</summary>
public enum CartographicRenderPreset
{
    /// <summary>On-screen field view: themed canvas, shadows, sketch grid.</summary>
    Field,

    /// <summary>Print/export: clean light background, stronger strokes, no shadows.</summary>
    Print,
}
