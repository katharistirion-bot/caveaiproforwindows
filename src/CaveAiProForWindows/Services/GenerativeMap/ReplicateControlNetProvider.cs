using System.IO;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Services.GenerativeMap;
using CaveAiProForWindows.Services.Secrets;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>
/// ControlNet scribble img2img via Replicate (<c>jagilley/controlnet-scribble</c>).
/// </summary>
public sealed class ReplicateControlNetProvider : IGenerativeMapRenderer
{
    /// <summary>Latest stable version hash for jagilley/controlnet-scribble.</summary>
    public const string DefaultModelVersion =
        "435061a1b5a4c1e26740464bf786efdfa9cb3a3ac488595a2de23e143fdb0117";

    private readonly ReplicateApiClient _client;
    private readonly string _modelVersion;

    public ReplicateControlNetProvider(ReplicateApiClient? client = null, string? modelVersion = null)
    {
        _client = client ?? new ReplicateApiClient(ReplicateApiTokenStore.TryRead);
        _modelVersion = string.IsNullOrWhiteSpace(modelVersion) ? DefaultModelVersion : modelVersion!;
    }

    public string ProviderName => "Replicate ControlNet Scribble";

    public async Task<GenerativeMapRenderResult> RenderAsync(
        GenerativeMapRenderRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StructureMaskPng.Length == 0)
            throw new ArgumentException("Structure mask PNG is empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt is required.", nameof(request));

        progress?.Report("Preparing structure mask for ControlNet…");
        var scribbleInput = StructureMaskControlNetPreprocessor.PrepareScribbleInput(request.StructureMaskPng)
            ?? throw new InvalidOperationException("Could not preprocess the structure mask PNG.");

        var dataUri = "data:image/png;base64," + Convert.ToBase64String(scribbleInput);
        var input = new Dictionary<string, object?>
        {
            ["image"] = dataUri,
            ["prompt"] = request.Prompt.Trim(),
            ["a_prompt"] = request.AddedPrompt,
            ["n_prompt"] = request.NegativePrompt,
            ["scale"] = request.GuidanceScale,
            ["ddim_steps"] = request.Steps,
            ["image_resolution"] = request.ImageResolution.ToString(),
            ["num_samples"] = "1",
        };
        if (request.Seed is int seed)
            input["seed"] = seed;

        progress?.Report("Submitting Replicate ControlNet job…");
        var created = await _client.CreatePredictionAsync(_modelVersion, input, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(created.Id))
            throw new InvalidOperationException("Replicate did not return a prediction id.");

        progress?.Report("Waiting for Replicate GPU…");
        var finished = await _client.WaitForPredictionAsync(created.Id, progress, cancellationToken)
            .ConfigureAwait(false);

        var outputUrl = ReplicateApiClient.TryGetFirstOutputUrl(finished)
            ?? throw new InvalidOperationException("Replicate prediction succeeded but returned no output URL.");

        progress?.Report("Downloading generated map…");
        var pngBytes = await ReplicateApiClient.DownloadBytesAsync(outputUrl, cancellationToken)
            .ConfigureAwait(false);
        if (pngBytes.Length < 64)
            throw new InvalidOperationException("Downloaded image was unexpectedly small.");

        var (w, h) = TryReadPngDimensions(pngBytes);
        progress?.Report("Generative render complete.");

        return new GenerativeMapRenderResult
        {
            PngBytes = pngBytes,
            ProviderName = ProviderName,
            ProviderPredictionId = finished.Id,
            PixelWidth = w,
            PixelHeight = h,
        };
    }

    private static (int Width, int Height) TryReadPngDimensions(byte[] png)
    {
        try
        {
            using var ms = new MemoryStream(png);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            return (img.PixelWidth, img.PixelHeight);
        }
        catch
        {
            return (0, 0);
        }
    }
}
