using System;
using System.Windows;

namespace CaveAiProForWindows.Services;

/// <summary>Survey metres ↔ canvas DIP mapping (same fit as plan vector rendering).</summary>
public readonly record struct PlanCanvasSurveyLayout(
    double WMinX,
    double WMaxX,
    double WMinY,
    double WMaxY,
    double OriginX,
    double OriginY,
    double Scale)
{
    public double PxPerMetre => Scale;

    public double WorldSpanX => Math.Max(1e-9, WMaxX - WMinX);

    public double WorldSpanY => Math.Max(1e-9, WMaxY - WMinY);

    /// <inheritdoc cref="CanvasToWorld" />
    public Point WorldToCanvas(float x, float y)
    {
        if (!IsFiniteDouble((double)x) || !IsFiniteDouble((double)y))
            return new Point(0, 0);
        var px = OriginX + ((double)x - WMinX) * Scale;
        var py = OriginY + (WMaxY - (double)y) * Scale;
        if (!IsFiniteDouble(px) || !IsFiniteDouble(py))
            return new Point(0, 0);
        return new Point(px, py);
    }

    public (double Wx, double Wy) CanvasToWorld(double canvasX, double canvasY)
    {
        var wx = WMinX + (canvasX - OriginX) / Scale;
        var wy = WMaxY - (canvasY - OriginY) / Scale;
        return (wx, wy);
    }

    public Point CanvasToWorld(Point canvasPt)
    {
        var (wx, wy) = CanvasToWorld(canvasPt.X, canvasPt.Y);
        return new Point(wx, wy);
    }

    private static bool IsFiniteDouble(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    /// <summary>
    /// Builds the same survey-metres → canvas mapping used by <see cref="Views.PlanCanvasRenderer"/> / offline X-Ray
    /// overlays when only axis-aligned world bounds and canvas size are known.
    /// </summary>
    public static PlanCanvasSurveyLayout FromAxisAlignedBounds(
        double wMinX,
        double wMaxX,
        double wMinY,
        double wMaxY,
        double canvasWidth,
        double canvasHeight,
        double pad = 32)
    {
        var wSpanX = Math.Max(1e-9, wMaxX - wMinX);
        var wSpanY = Math.Max(1e-9, wMaxY - wMinY);
        var usableW = Math.Max(1, canvasWidth - 2 * pad);
        var usableH = Math.Max(1, canvasHeight - 2 * pad);
        var scale = Math.Min(usableW / wSpanX, usableH / wSpanY);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            scale = 1;
        var plotW = wSpanX * scale;
        var plotH = wSpanY * scale;
        var originX = pad + (usableW - plotW) * 0.5;
        var originY = pad + (usableH - plotH) * 0.5;
        if (!IsFiniteDouble(originX) || !IsFiniteDouble(originY))
        {
            originX = pad;
            originY = pad;
        }

        return new PlanCanvasSurveyLayout(wMinX, wMaxX, wMinY, wMaxY, originX, originY, scale);
    }
}
