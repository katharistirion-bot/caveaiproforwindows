using System.IO;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Services.Secrets;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>
/// ControlNet scribble img2img via Replicate (<c>jagilley/controlnet-scribble</c>).
/// Default: Firebase Callable proxy (server-side API key). Set <c>CAVEAIPRO_REPLICATE_BYOK=1</c> for direct BYOK.
/// </summary>
public sealed class ReplicateControlNetProvider : IGenerativeMapRenderer
{
    /// <summary>Latest stable version hash for jagilley/controlnet-scribble.</summary>
    public const string DefaultModelVersion =
        "435061a1b5a4c1e26740464bf786efdfa9cb3a3ac488595a2de23e143fdb0117";

    private readonly ReplicateApiClient? _directClient;
    private readonly ReplicateCallableProxyClient? _proxyClient;
    private readonly string _modelVersion;

    public ReplicateControlNetProvider(
        ReplicateApiClient? directClient = null,
        ReplicateCallableProxyClient? proxyClient = null,
        string? modelVersion = null)
    {
        _modelVersion = string.IsNullOrWhiteSpace(modelVersion) ? DefaultModelVersion : modelVersion!;
        if (GenerativeAiAccessGate.UseDirectByok)
            _directClient = directClient ?? new ReplicateApiClient(ReplicateApiTokenStore.TryRead);
        else
            _proxyClient = proxyClient ?? new ReplicateCallableProxyClient();
    }

    public string ProviderName =>
        GenerativeAiAccessGate.UseDirectByok
            ? "Replicate ControlNet Scribble"
            : "CaveAI Cloud AI (Replicate ControlNet)";

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

        var rawResolution = request.ImageResolution ?? Math.Max(prepared.ApiWidth, prepared.ApiHeight);
        var apiResolution = ReplicateImageResolution.Snap(rawResolution);
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
            ["image_resolution"] = ReplicateImageResolution.ToApiString(rawResolution),
            ["num_samples"] = "1",
        };
        if (request.Seed is int seed)
            input["seed"] = seed;

        string predictionId;
        string outputUrl;

        if (_proxyClient != null)
        {
            progress?.Report("Submitting cloud AI render (secure proxy)…");
            var proxyResult = await _proxyClient.RunPredictionAsync(_modelVersion, input, cancellationToken)
                .ConfigureAwait(false);
            predictionId = proxyResult.PredictionId
                ?? throw new InvalidOperationException("Cloud proxy did not return a prediction id.");
            outputUrl = proxyResult.OutputUrl
                ?? throw new InvalidOperationException("Cloud proxy succeeded but returned no output URL.");
        }
        else
        {
            progress?.Report("Submitting Replicate ControlNet job…");
            var created = await _directClient!.CreatePredictionAsync(_modelVersion, input, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Replicate did not return a prediction id.");

            progress?.Report("Waiting for Replicate GPU…");
            var finished = await _directClient.WaitForPredictionAsync(created.Id, progress, cancellationToken)
                .ConfigureAwait(false);
            predictionId = finished.Id ?? created.Id!;
            outputUrl = ReplicateApiClient.TryGetFirstOutputUrl(finished)
                ?? throw new InvalidOperationException("Replicate prediction succeeded but returned no output URL.");
        }

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
            ProviderPredictionId = predictionId,
            PixelWidth = prepared.SourceWidth,
            PixelHeight = prepared.SourceHeight,
            ApiInputWidth = prepared.ApiWidth,
            ApiInputHeight = prepared.ApiHeight,
        };
    }
}
