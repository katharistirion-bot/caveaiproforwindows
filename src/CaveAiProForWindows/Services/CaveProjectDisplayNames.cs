using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Resolves the human cave / project title shown on maps and HUD panels.</summary>
public static class CaveProjectDisplayNames
{
    public static string GetDisplayName(CaveProjectDocument? project)
    {
        if (project == null)
            return "";

        var name = (project.Name ?? "").Trim();
        if (name.Length > 0)
            return name;

        if (project.ExtensionData != null)
        {
            foreach (var key in new[] { "caveName", "projectName", "title" })
            {
                if (!project.ExtensionData.TryGetValue(key, out var el))
                    continue;
                var s = ReadString(el)?.Trim();
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }

        return "";
    }

    private static string? ReadString(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
