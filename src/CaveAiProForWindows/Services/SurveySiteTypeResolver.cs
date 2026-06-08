using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Resolves cave vs mine (etc.) for a survey project from JSON and optional Cave Library rows.</summary>
public static class SurveySiteTypeResolver
{
    public static SurveySiteTypeKind Resolve(
        CaveProjectDocument? project,
        IEnumerable<KnownCaveRecord>? library = null)
    {
        if (project == null)
            return SurveySiteTypeKind.Unknown;

        foreach (var raw in ReadCandidateTokens(project))
        {
            var kind = SurveySiteType.Parse(raw);
            if (kind != SurveySiteTypeKind.Unknown)
                return kind;
        }

        var lib = library?.ToList();
        if (lib == null || lib.Count == 0)
            return SurveySiteTypeKind.Cave;

        var linkedId = (project.LinkedLibraryCaveId ?? "").Trim();
        if (linkedId.Length > 0)
        {
            var byId = lib.FirstOrDefault(k =>
                string.Equals(k.Id, linkedId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                var kind = SurveySiteType.Parse(byId.Type);
                if (kind != SurveySiteTypeKind.Unknown)
                    return kind;
            }
        }

        var name = (project.Name ?? "").Trim();
        if (name.Length > 0)
        {
            var byName = lib.FirstOrDefault(k =>
                string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                var kind = SurveySiteType.Parse(byName.Type);
                if (kind != SurveySiteTypeKind.Unknown)
                    return kind;
            }
        }

        return SurveySiteTypeKind.Cave;
    }

    public static string GetMapLabel(
        CaveProjectDocument? project,
        IEnumerable<KnownCaveRecord>? library = null)
    {
        var kind = Resolve(project, library);
        return SurveySiteType.GetMapLabel(kind);
    }

    public static void EnrichProjectsFromLibrary(
        IEnumerable<CaveProjectDocument> projects,
        IEnumerable<KnownCaveRecord> library)
    {
        var lib = library.ToList();
        if (lib.Count == 0)
            return;

        foreach (var project in projects)
        {
            if (!string.IsNullOrWhiteSpace(project.SurveySiteType))
                continue;

            var kind = Resolve(project, lib);
            if (kind == SurveySiteTypeKind.Unknown)
                continue;

            project.SurveySiteType = SurveySiteType.CanonicalToken(kind);
        }
    }

    private static IEnumerable<string?> ReadCandidateTokens(CaveProjectDocument project)
    {
        yield return project.SurveySiteType;

        if (project.ExtensionData == null)
            yield break;

        foreach (var key in new[] { SurveySiteType.JsonKey, "siteType", "caveType", "type" })
        {
            if (!project.ExtensionData.TryGetValue(key, out var el))
                continue;
            if (el.ValueKind == JsonValueKind.String)
                yield return el.GetString();
        }
    }
}
