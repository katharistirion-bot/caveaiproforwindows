using CaveAiProForWindows.Models;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Assembles publish artifacts from the active project (sketch editor when available).</summary>
public static class CloudPublishArtifactCollector
{
    public static CloudPublishArtifactCapture Collect(
        CaveProjectDocument project,
        Func<CloudPublishArtifactCapture?>? trySketchCapture = null,
        Action<CaveProjectDocument>? persistBeforeCapture = null,
        string? zipPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        persistBeforeCapture?.Invoke(project);

        var sketch = trySketchCapture?.Invoke();
        if (sketch != null)
            return sketch;

        return new CloudPublishArtifactCapture
        {
            SurveyJsonUtf8 = CloudPublishService.SerializeProjectJsonUtf8(project),
            PendingGalleryPhotos = CloudPublishPhotoCollector.CollectFromZip(zipPath, project),
        };
    }
}
