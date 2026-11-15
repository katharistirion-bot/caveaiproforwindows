using System.IO;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Persistence;

namespace CaveAiProForWindows.Services.Gemini;

/// <summary>
/// Sends the survey centerline to Cloud AI and stores the returned plan image on the project.
/// </summary>
public static class CloudPlanRenderer
{
    public static byte[]? TryExtractInlineImage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (root.TryGetProperty("candidates", out var candidates))
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content) ||
                    !content.TryGetProperty("parts", out var parts))
                    continue;
                foreach (var part in parts.EnumerateArray())
                {
                    var image = ReadImagePart(part);
                    if (image != null)
                        return image;
                }
            }
        }

        return ReadImagePart(root);
    }

    public static async Task<string> RenderAndStoreAsync(
        CaveProjectDocument project,
        string? zipPath,
        CancellationToken cancellationToken = default)
    {
        var png = RenderCenterlinePng(project);
        var body = GeminiProxyClient.BuildChatBody(
            GeminiProxyClient.DefaultModel,
            "You render cave survey plans. Return one image only.",
            "Draw a clean plan of this cave from the centerline image. White background, black passage walls, no text, no legend.");
        var parts = body["contents"]![0]!["parts"]!.AsArray();
        parts.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["inlineData"] = new System.Text.Json.Nodes.JsonObject
            {
                ["mimeType"] = "image/png",
                ["data"] = Convert.ToBase64String(png),
            },
        });
        body["generationConfig"] = new System.Text.Json.Nodes.JsonObject
        {
            ["responseModalities"] = new System.Text.Json.Nodes.JsonArray("IMAGE"),
        };

        var raw = await GeminiProxyClient.PostGenerateContentAsync(body, cancellationToken).ConfigureAwait(true);
        var image = TryExtractInlineImage(raw);
        if (image == null || image.Length < 32)
        {
            var text = GeminiProxyClient.ExtractTextFromGenerateContentResponse(raw);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(text)
                    ? "Cloud AI did not return a plan image."
                    : "Cloud AI did not return a plan image. " + text);
        }

        ProjectAiAssetPersistence.StoreGeneratedMap(project, zipPath, image);
        return "Cloud plan stored. Save the project so the phone receives the image.";
    }

    public static byte[] RenderCenterlinePng(CaveProjectDocument project)
    {
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        const int size = 768;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, size, size));
            if (coords.Count > 0)
            {
                var minX = coords.Values.Min(c => c.X);
                var maxX = coords.Values.Max(c => c.X);
                var minY = coords.Values.Min(c => c.Y);
                var maxY = coords.Values.Max(c => c.Y);
                var span = Math.Max(1f, Math.Max(maxX - minX, maxY - minY));
                var scale = (size - 48) / span;
                Point Map(float x, float y) => new(24 + (x - minX) * scale, size - 24 - (y - minY) * scale);
                var pen = new Pen(Brushes.Black, 3);
                foreach (var shot in project.Shots)
                {
                    if (!shot.IsTraverseLeg)
                        continue;
                    if (!coords.TryGetValue(shot.FromStation, out var a) || !coords.TryGetValue(shot.ToStation, out var b))
                        continue;
                    dc.DrawLine(pen, Map(a.X, a.Y), Map(b.X, b.Y));
                }
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static byte[]? ReadImagePart(JsonElement part)
    {
        foreach (var name in new[] { "inlineData", "inline_data" })
        {
            if (!part.TryGetProperty(name, out var inline))
                continue;
            if (!inline.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String)
                continue;
            try
            {
                return Convert.FromBase64String(data.GetString() ?? "");
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }
}
