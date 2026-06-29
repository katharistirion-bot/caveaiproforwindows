using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Grey-on-black LRUD / sketch structure masks for cloud publish.</summary>
public static class StructureMaskBuilder
{
    public static byte[]? TryBuildGrayscaleLrudMaskPng(
        SketchAssistSession session,
        SketchAssistStructureMaskOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        options ??= SketchAssistStructureMaskOptions.Default;

        var (pxW, pxH) = SketchAssistExportSizing.ComputeExportPixelSize(session.Scene);
        if (!PlanCanvasRenderer.TryComputeSurveyLayout(session.Scene, pxW, pxH, out var exportLayout))
            return null;

        var canvas = new Canvas
        {
            Width = pxW,
            Height = pxH,
            Background = Brushes.Black,
            ClipToBounds = true,
        };

        if (options.IncludeLrudCorridor)
            StructureMaskLrudCorridorRenderer.DrawLrudCorridors(canvas, session, exportLayout);
        else if (options.IncludeTraverseCenterline)
            SketchAssistStructureMaskExporter.DrawTraverseCenterlineForMask(
                canvas, session.Scene, exportLayout, options.LineWidthPx);

        if (options.IncludeUserStrokes)
            SketchAssistStructureMaskExporter.DrawUserStrokesForMask(
                canvas, session.Document, exportLayout, options.LineWidthPx);

        if (options.IncludeUserSymbolStamps)
            SketchAssistStructureMaskExporter.DrawUserSymbolStampsForMask(
                canvas, session.Document, exportLayout, options.SymbolMarkerRadiusPx);

        return RenderCanvasToPng(canvas, pxW, pxH);
    }

    internal static byte[]? RenderCanvasToPng(Canvas canvas, int pxW, int pxH)
    {
        canvas.Measure(new Size(pxW, pxH));
        canvas.Arrange(new Rect(0, 0, pxW, pxH));
        canvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}