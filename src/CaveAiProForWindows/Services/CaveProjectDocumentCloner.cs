using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Deep clone via JSON round-trip (Gson-compatible field preservation).</summary>
public static class CaveProjectDocumentCloner
{
    public static CaveProjectDocument Clone(CaveProjectDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var json = SurveyPortableZipExporter.SerializeSingleProjectArray(source);
        var projects = ExplorationDataLoader.DeserializeProjectsFromText(json);
        if (projects.Count == 0)
            throw new InvalidOperationException("Clone produced no project.");
        return projects[0];
    }
}
