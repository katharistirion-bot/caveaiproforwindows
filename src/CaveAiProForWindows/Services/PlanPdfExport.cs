using System.IO;
using SkiaSharp;

namespace CaveAiProForWindows.Services;

/// <summary>Embeds a PNG bitmap (e.g. plan canvas capture) into a single-page PDF.</summary>
public static class PlanPdfExport
{
    /// <summary>96 DPI WPF bitmap → PDF points (72 pt/inch).</summary>
    private const double PointsPerPixel = 72.0 / 96.0;

    public static void SavePngBytesAsPdf(ReadOnlySpan<byte> pngBytes, string pdfPath)
    {
        using var bmp = SKBitmap.Decode(pngBytes);
        if (bmp == null)
            throw new InvalidOperationException("Could not decode PNG for PDF export.");

        var pw = (float)(bmp.Width * PointsPerPixel);
        var ph = (float)(bmp.Height * PointsPerPixel);

        using var stream = File.Create(pdfPath);
        using var document = SKDocument.CreatePdf(stream);
        var canvas = document.BeginPage(pw, ph);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(bmp, new SKRect(0, 0, pw, ph), paint);
        document.EndPage();
        document.Close();
    }
}
