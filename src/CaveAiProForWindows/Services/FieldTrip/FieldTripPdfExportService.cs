using System.Globalization;
using System.IO;
using System.Text;
using CaveAiProForWindows.Models;
using SkiaSharp;

namespace CaveAiProForWindows.Services.FieldTrip;

/// <summary>Simple PDF itinerary export for field trips (text layout via SkiaSharp PDF).</summary>
public static class FieldTripPdfExportService
{
    public static void ExportPdf(FieldTripDocument trip, string pdfPath)
    {
        using var stream = File.Create(pdfPath);
        using var document = SKDocument.CreatePdf(stream);
        const float pageW = 595;
        const float pageH = 842;
        const float margin = 48;
        var canvas = document.BeginPage(pageW, pageH);

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 20,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        };
        using var bodyPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 11,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI"),
        };
        using var mutedPaint = new SKPaint
        {
            Color = new SKColor(80, 80, 80),
            TextSize = 10,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI"),
        };

        var y = margin;
        canvas.DrawText(trip.Name, margin, y, titlePaint);
        y += 28;

        canvas.DrawText($"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}", margin, y, mutedPaint);
        y += 22;

        if (!string.IsNullOrWhiteSpace(trip.Notes))
        {
            foreach (var line in Wrap(trip.Notes, 90))
            {
                canvas.DrawText(line, margin, y, bodyPaint);
                y += 16;
            }

            y += 8;
        }

        canvas.DrawText($"Stops ({trip.Stops.Count})", margin, y, titlePaint);
        y += 24;

        var idx = 1;
        foreach (var stop in trip.Stops)
        {
            if (y > pageH - margin - 40)
            {
                document.EndPage();
                canvas = document.BeginPage(pageW, pageH);
                y = margin;
            }

            var header = $"{idx}. {stop.Name}";
            canvas.DrawText(header, margin, y, bodyPaint);
            y += 16;

            var details = new StringBuilder();
            if (stop.Lat is double lat && stop.Lon is double lon)
                details.Append($"{lat:F5}, {lon:F5}");
            if (stop.DepthM is > 0)
                details.Append(details.Length > 0 ? $" · depth {stop.DepthM:0.#} m" : $"depth {stop.DepthM:0.#} m");
            if (!string.IsNullOrWhiteSpace(stop.ReferenceId))
                details.Append($" · ref {stop.ReferenceId}");

            if (details.Length > 0)
            {
                canvas.DrawText(details.ToString(), margin + 12, y, mutedPaint);
                y += 14;
            }

            if (!string.IsNullOrWhiteSpace(stop.Notes))
            {
                foreach (var line in Wrap(stop.Notes, 85))
                {
                    canvas.DrawText(line, margin + 12, y, mutedPaint);
                    y += 14;
                }
            }

            y += 10;
            idx++;
        }

        document.EndPage();
        document.Close();
    }

    private static IEnumerable<string> Wrap(string text, int maxChars)
    {
        foreach (var para in text.Split('\n'))
        {
            var line = para.Trim();
            while (line.Length > maxChars)
            {
                var breakAt = line.LastIndexOf(' ', Math.Min(maxChars, line.Length - 1));
                if (breakAt <= 0) breakAt = maxChars;
                yield return line[..breakAt].Trim();
                line = line[breakAt..].Trim();
            }

            if (line.Length > 0)
                yield return line;
        }
    }
}
