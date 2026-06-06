using System.IO;
using System.Windows.Media.Imaging;
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
        var prepared = StructureMaskControlNetPreprocessor.PrepareScribbleInput(request.StructureMaskPng)
            ?? throw new InvalidOperationException("Could not preprocess the structure mask PNG.");

        var apiResolution = request.ImageResolution ?? Math.Max(prepared.ApiWidth, prepared.ApiHeight);
        progress?.Report(
            $"ControlNet input {prepared.ApiWidth}×{prepared.ApiHeight} px " +
            $"(survey export {prepared.SourceWidth}×{prepared.SourceHeight}, image_resolution={apiResolution})…");

        var dataUri = "data:image/png;base64," + Convert.ToBase64String(prepared.PngBytes);
        var input = new Dictionary<string, object?>
        {
            ["image"] = dataUri,
            ["prompt"] = request.Prompt.Trim(),
            ["a_prompt"] = request.AddedPrompt,
            ["n_prompt"] = request.NegativePrompt,
            ["scale"] = request.GuidanceScale,
            ["ddim_steps"] = request.Steps,
            ["image_resolution"] = apiResolution.ToString(),
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
        var apiPngBytes = await ReplicateApiClient.DownloadBytesAsync(outputUrl, cancellationToken)
            .ConfigureAwait(false);
        if (apiPngBytes.Length < 64)
            throw new InvalidOperationException("Downloaded image was unexpectedly small.");

        progress?.Report(
            $"Mapping AI render back to survey canvas ({prepared.SourceWidth}×{prepared.SourceHeight})…");
        var canvasPngBytes = StructureMaskControlNetPreprocessor.ResizePngToDimensions(
                                 apiPngBytes,
                                 prepared.SourceWidth,
                                 prepared.SourceHeight)
                             ?? apiPngBytes;

        progress?.Report("Generative render complete.");

        return new GenerativeMapRenderResult
        {
            PngBytes = canvasPngBytes,
            ProviderName = ProviderName,
            ProviderPredictionId = finished.Id,
            PixelWidth = prepared.SourceWidth,
            PixelHeight = prepared.SourceHeight,
            ApiInputWidth = prepared.ApiWidth,
            ApiInputHeight = prepared.ApiHeight,
        };
    }
}
