using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Services.SketchAssist;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>Queues AI render for multiple projects with structure masks (respects rate limits).</summary>
public sealed class BatchAiRenderService
{
    private readonly IGenerativeMapRenderer _renderer;

    public BatchAiRenderService(IGenerativeMapRenderer? renderer = null)
    {
        _renderer = renderer ?? new ReplicateControlNetProvider();
    }

    public sealed class BatchItemResult
    {
        public required string ProjectName { get; init; }

        public bool Success { get; init; }

        public string? Error { get; init; }
    }

    public async Task<IReadOnlyList<BatchItemResult>> RenderAllAsync(
        IReadOnlyList<CaveProjectDocument> projects,
        string prompt,
        string negativePrompt,
        double guidanceScale,
        IProgress<(int Index, int Total, string Message)>? progress,
        Func<CaveProjectDocument, byte[], byte[], bool>? persistAfterRender = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BatchItemResult>();
        var eligible = new List<(CaveProjectDocument Project, byte[] Mask)>();
        foreach (var p in projects)
        {
            var mask = TryBuildStructureMask(p);
            if (mask is { Length: > 0 })
                eligible.Add((p, mask));
        }

        if (eligible.Count == 0)
            return results;

        for (var i = 0; i < eligible.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (project, mask) = eligible[i];
            progress?.Report((i + 1, eligible.Count, $"Rendering {project.Name}…"));

            try
            {
                var request = new GenerativeMapRenderRequest
                {
                    StructureMaskPng = mask,
                    Prompt = prompt.Trim(),
                    NegativePrompt = negativePrompt.Trim(),
                    GuidanceScale = guidanceScale,
                };

                var renderProgress = new Progress<string>(m => progress?.Report((i + 1, eligible.Count, m)));
                var result = await _renderer.RenderAsync(request, renderProgress, cancellationToken).ConfigureAwait(false);
                GenerativeMapSessionCache.Set(project, result.PngBytes, mask);
                persistAfterRender?.Invoke(project, result.PngBytes, mask);
                results.Add(new BatchItemResult { ProjectName = project.Name, Success = true });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var msg = ex is FirebaseCallableException fce
                    ? GenerativeAiRateLimitFormatter.Format(fce)
                    : ex.Message;
                results.Add(new BatchItemResult { ProjectName = project.Name, Success = false, Error = msg });
                if (msg.Contains("Rate limit", StringComparison.OrdinalIgnoreCase))
                    break;
            }
        }

        return results;
    }

    public static byte[]? TryBuildStructureMask(CaveProjectDocument project)
    {
        const double size = 1024;
        var session = SketchAssistInputBuilder.TryBuild(project, null, size, size);
        return session == null ? null : SketchAssistStructureMaskExporter.TryExportStructureMaskPng(session);
    }
}
