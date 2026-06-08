namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>Built-in style presets for ControlNet generative map rendering.</summary>
public sealed record GenerativeMapPromptPreset(
    string Id,
    string DisplayName,
    string Prompt,
    string NegativePrompt,
    double GuidanceScale = 9);

public static class GenerativeMapPromptPresets
{
    public static IReadOnlyList<GenerativeMapPromptPreset> All { get; } =
    [
        new(
            "photoreal",
            "Photorealistic survey",
            "photorealistic cave survey map, top-down view, rock textures, underground river, detailed speleology",
            "blurry, low quality, text, watermark, cartoon"),
        new(
            "pencil",
            "Pencil sketch",
            "hand-drawn pencil sketch cave map, speleological survey, cross-hatching, paper texture, top-down plan",
            "photo, color, blurry, watermark"),
        new(
            "ink-therion",
            "Ink (Therion-like)",
            "clean black ink line art cave survey map, Therion style, top-down plan, wall lines, station dots, speleology",
            "color, photo, blurry, shading, watermark"),
        new(
            "watercolor",
            "Watercolor",
            "watercolor painting cave map, soft earth tones, top-down speleological plan, artistic survey illustration",
            "photo, harsh lines, text, watermark"),
        new(
            "line-clean",
            "Clean line art",
            "minimal clean line art cave survey map, white background, precise wall outlines, top-down plan view",
            "photo, texture noise, blurry, text"),
    ];

    public static GenerativeMapPromptPreset? TryGet(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(p => string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
}
