namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>
/// Renders a stylized cartography image from a structure mask (ControlNet / img2img providers).
/// </summary>
public interface IGenerativeMapRenderer
{
    string ProviderName { get; }

    Task<GenerativeMapRenderResult> RenderAsync(
        GenerativeMapRenderRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class GenerativeMapRenderRequest
{
    /// <summary>White line art on black background (structure mask export).</summary>
    public required byte[] StructureMaskPng { get; init; }

    public required string Prompt { get; init; }

    public string NegativePrompt { get; init; } =
        "longbody, lowres, bad anatomy, blurry, text, watermark, signature, cropped, worst quality, low quality";

    public string AddedPrompt { get; init; } = "best quality, extremely detailed, survey cartography";

    public double GuidanceScale { get; init; } = 9;

    public int Steps { get; init; } = 25;

    public int? Seed { get; init; }

    /// <summary>
    /// Optional override for Replicate <c>image_resolution</c>. When null, derived from the downscaled mask
    /// (longest edge ≤ 1024, multiple of 8).
    /// </summary>
    public int? ImageResolution { get; init; }
}

public sealed class GenerativeMapRenderResult
{
    public required byte[] PngBytes { get; init; }

    public required string ProviderName { get; init; }

    public string? ProviderPredictionId { get; init; }

    /// <summary>Final PNG size aligned to the survey export bounding box (canvas overlay).</summary>
    public int PixelWidth { get; init; }

    public int PixelHeight { get; init; }

    /// <summary>Dimensions sent to Replicate after GPU-safe downscale.</summary>
    public int ApiInputWidth { get; init; }

    public int ApiInputHeight { get; init; }
}
