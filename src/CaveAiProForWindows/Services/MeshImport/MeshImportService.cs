using System.IO;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.MeshImport;

/// <summary>Resolves mesh files from disk or backup ZIP and parses OBJ/STL locally.</summary>
public static class MeshImportService
{
    public static ParsedTriangleMesh LoadAttachment(
        StationAnchoredMeshAttachment attachment,
        string? zipPath = null)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        var local = ResolveLocalPath(attachment.SourcePath, zipPath);
        return LoadFile(local);
    }

    public static ParsedTriangleMesh LoadFile(string localPath)
    {
        var ext = Path.GetExtension(localPath).ToLowerInvariant();
        return ext switch
        {
            ".obj" => ObjMeshParser.ParseFile(localPath),
            ".stl" => StlMeshParser.ParseFile(localPath),
            _ => throw new NotSupportedException($"Mesh format '{ext}' is not supported. Use .obj or .stl."),
        };
    }

    public static string ResolveLocalPath(string sourcePath, string? zipPath)
    {
        if (File.Exists(sourcePath))
            return sourcePath;

        if (!string.IsNullOrWhiteSpace(zipPath) && File.Exists(zipPath))
        {
            if (MapAssetOpener.TryEnsureLocalFilePath(sourcePath, zipPath, out var extracted, out _) &&
                !string.IsNullOrEmpty(extracted) &&
                File.Exists(extracted))
            {
                return extracted;
            }
        }

        throw new FileNotFoundException("Could not resolve mesh file on disk or inside backup ZIP.", sourcePath);
    }

    /// <summary>Build attachments from project metadata and standalone OBJ URIs.</summary>
    public static IReadOnlyList<StationAnchoredMeshAttachment> DiscoverAttachments(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var list = new List<StationAnchoredMeshAttachment>();

        if (!string.IsNullOrWhiteSpace(project.CartographyTlsMeshObjUri))
        {
            var anchor = SurveyStationGeometry.CalculatePlanCoordinates(project).Keys
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? "1";

            list.Add(new StationAnchoredMeshAttachment
            {
                Id = "tls-mesh",
                AnchorStationName = anchor,
                SourcePath = project.CartographyTlsMeshObjUri!,
                DisplayName = "TLS mesh",
            });
        }

        return list;
    }
}
