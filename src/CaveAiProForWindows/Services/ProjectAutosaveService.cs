using System.IO;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Autosaves dirty project drafts to LocalAppData on a timer.</summary>
public sealed class ProjectAutosaveService : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private Func<CaveProjectDocument?>? _getProject;
    private Func<string?>? _getSourcePath;
    private Action<CaveProjectDocument>? _persistBeforeSave;

    public ProjectAutosaveService()
    {
        _timer = new System.Threading.Timer(_ => TryAutosave(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
    }

    public void Configure(
        Func<CaveProjectDocument?> getProject,
        Func<string?> getSourcePath,
        Action<CaveProjectDocument> persistBeforeSave)
    {
        _getProject = getProject;
        _getSourcePath = getSourcePath;
        _persistBeforeSave = persistBeforeSave;
    }

    public string DraftsDirectory =>
        Path.Combine(DiagnosticLogPaths.AppDataDirectory, "drafts");

    private void TryAutosave()
    {
        try
        {
            var project = _getProject?.Invoke();
            if (project == null)
                return;

            _persistBeforeSave?.Invoke(project);
            Directory.CreateDirectory(DraftsDirectory);
            var safe = string.Join("_", (project.Name ?? "project").Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(DraftsDirectory, $"{safe}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            var json = SurveyPortableZipExporter.SerializeProjectArray(new[] { project });
            File.WriteAllText(path, json);
        }
        catch
        {
            /* best-effort */
        }
    }

    public void Dispose() => _timer.Dispose();
}
