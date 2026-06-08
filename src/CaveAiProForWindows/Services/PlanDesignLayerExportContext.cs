using System.Windows.Controls;

namespace CaveAiProForWindows.Services;

/// <summary>On-screen design-layer canvas to composite onto high-resolution plan exports.</summary>
public sealed record PlanDesignLayerExportContext(Canvas Layer, double CanvasWidth, double CanvasHeight);
