using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Exports traverse tube mesh as minimal Wavefront OBJ or glTF 2.0 (ASCII JSON + embedded buffer).</summary>
public static class Viewport3DMeshExporter
{
    public enum ExportFormat
    {
        Obj,
        Gltf,
    }

    public static bool TryExport(
        CaveProjectDocument project,
        string outputPath,
        ExportFormat format,
        Viewport3DDisplayOptions? display = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(outputPath))
            return false;

        display ??= Viewport3DDisplayOptions.Default;
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var mesh = CaveSurveyTubeMeshBuilder.BuildTubeMesh(project.Shots, coords);
        if (mesh == null || mesh.Positions.Count == 0)
            return false;

        return format switch
        {
            ExportFormat.Obj => WriteObj(mesh, outputPath, project.Name),
            ExportFormat.Gltf => WriteGltf(mesh, outputPath, project.Name),
            _ => false,
        };
    }

    private static bool WriteObj(MeshGeometry3D mesh, string path, string? name)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# CAVE AI PRO — 3D viewport export");
        sb.AppendLine("o " + SanitizeName(name));
        foreach (Point3D p in mesh.Positions)
            sb.AppendLine(string.Format(inv, "v {0:0.#######} {1:0.#######} {2:0.#######}", p.X, p.Y, p.Z));

        for (var i = 0; i + 2 < mesh.TriangleIndices.Count; i += 3)
        {
            var a = mesh.TriangleIndices[i] + 1;
            var b = mesh.TriangleIndices[i + 1] + 1;
            var c = mesh.TriangleIndices[i + 2] + 1;
            sb.AppendLine($"f {a} {b} {c}");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return true;
    }

    private static bool WriteGltf(MeshGeometry3D mesh, string path, string? name)
    {
        var inv = CultureInfo.InvariantCulture;
        var positions = new List<float>();
        foreach (Point3D p in mesh.Positions)
        {
            positions.Add((float)p.X);
            positions.Add((float)p.Y);
            positions.Add((float)p.Z);
        }

        var indices = mesh.TriangleIndices.Select(i => (uint)i).ToArray();
        var posBytes = new byte[positions.Count * 4];
        Buffer.BlockCopy(positions.ToArray(), 0, posBytes, 0, posBytes.Length);
        var idxBytes = new byte[indices.Length * 4];
        Buffer.BlockCopy(indices, 0, idxBytes, 0, idxBytes.Length);
        var buffer = posBytes.Concat(idxBytes).ToArray();
        var base64 = Convert.ToBase64String(buffer);

        var json =
            $$"""
            {
              "asset": { "version": "2.0", "generator": "CAVE AI PRO" },
              "scene": 0,
              "scenes": [{ "nodes": [0] }],
              "nodes": [{ "mesh": 0, "name": "{{SanitizeName(name)}}" }],
              "meshes": [{
                "primitives": [{
                  "attributes": { "POSITION": 0 },
                  "indices": 1,
                  "mode": 4
                }]
              }],
              "buffers": [{ "byteLength": {{buffer.Length}}, "uri": "data:application/octet-stream;base64,{{base64}}" }],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": {{posBytes.Length}}, "target": 34962 },
                { "buffer": 0, "byteOffset": {{posBytes.Length}}, "byteLength": {{idxBytes.Length}}, "target": 34963 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": {{mesh.Positions.Count}}, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5125, "count": {{indices.Length}}, "type": "SCALAR" }
              ]
            }
            """;

        File.WriteAllText(path, json, Encoding.UTF8);
        return true;
    }

    private static string SanitizeName(string? name)
    {
        var s = (name ?? "cave").Trim();
        return s.Length == 0 ? "cave" : s.Replace('"', '_');
    }
}
