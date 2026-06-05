using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Reads and writes the Firestore <c>published_caves</c> document id linked to a survey project.</summary>
public static class LinkedLibraryCaveIdResolver
{
    public static string? TryGet(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!string.IsNullOrWhiteSpace(project.LinkedLibraryCaveId))
            return project.LinkedLibraryCaveId.Trim();

        if (project.ExtensionData?.TryGetValue("linkedLibraryCaveId", out var el) != true)
            return null;

        return el.ValueKind == JsonValueKind.String ? el.GetString()?.Trim() : null;
    }

    public static void Set(CaveProjectDocument project, string docId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var trimmed = docId.Trim();
        project.LinkedLibraryCaveId = trimmed;
        project.ExtensionData ??= new Dictionary<string, JsonElement>();
        project.ExtensionData["linkedLibraryCaveId"] = JsonSerializer.SerializeToElement(trimmed);
    }
}
