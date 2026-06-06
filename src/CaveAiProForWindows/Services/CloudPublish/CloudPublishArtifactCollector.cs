using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.GenerativeMap;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Assembles publish artifacts from the active project (sketch editor when available, else session cache).</summary>
public static class CloudPublishArtifactCollector
{
    public static CloudPublishArtifactCapture Collect(
        CaveProjectDocument project,
        Func<CloudPublishArtifactCapture?>? trySketchCapture = null,
        Action<CaveProjectDocument>? persistBeforeCapture = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        persistBeforeCapture?.Invoke(project);

        var sketch = trySketchCapture?.Invoke();
        if (sketch != null)
            return sketch;

        var session = GenerativeMapSessionCache.TryGet(project);
        var surveyJson = CloudPublishService.SerializeProjectJsonUtf8(project);
        return new CloudPublishArtifactCapture
        {
            AiMapPng = session?.PngBytes,
            StructureMaskPng = session?.StructureMaskPng,
            SurveyJsonUtf8 = surveyJson,
        };
    }
}
