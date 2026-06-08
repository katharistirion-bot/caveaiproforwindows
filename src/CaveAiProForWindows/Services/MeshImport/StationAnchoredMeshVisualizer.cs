using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.MeshImport;

/// <summary>Places imported meshes in the WPF 3D viewport anchored to survey stations.</summary>
public static class StationAnchoredMeshVisualizer
{
    private static readonly DependencyProperty MeshOverlayTagProperty =
        DependencyProperty.RegisterAttached(
            "MeshOverlayTag",
            typeof(string),
            typeof(StationAnchoredMeshVisualizer),
            new PropertyMetadata(null));

    public static void AttachMeshes(
        Viewport3D viewport,
        CaveProjectDocument project,
        IReadOnlyList<StationAnchoredMeshAttachment> attachments,
        string? zipPath = null,
        Brush? materialBrush = null)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(project);
        RemoveMeshes(viewport);

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var brush = materialBrush ?? new SolidColorBrush(Color.FromArgb(180, 0xC8, 0xA8, 0x70));
        brush.Freeze();
        var mat = new DiffuseMaterial(brush);

        foreach (var att in attachments)
        {
            if (!coords.TryGetValue(att.AnchorStationName, out var anchor))
                continue;

            ParsedTriangleMesh parsed;
            try
            {
                parsed = MeshImportService.LoadAttachment(att, zipPath);
            }
            catch
            {
                continue;
            }

            var mesh = TransformMesh(parsed, anchor, att);
            var model = new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat };
            var visual = new ModelVisual3D { Content = model };
            visual.SetValue(MeshOverlayTagProperty, att.Id);
            viewport.Children.Add(visual);
        }
    }

    public static void RemoveMeshes(Viewport3D viewport)
    {
        for (var i = viewport.Children.Count - 1; i >= 0; i--)
        {
            if (viewport.Children[i].GetValue(MeshOverlayTagProperty) is string)
                viewport.Children.RemoveAt(i);
        }
    }

    private static MeshGeometry3D TransformMesh(
        ParsedTriangleMesh parsed,
        SurveyStationGeometry.StationPlanCoords anchor,
        StationAnchoredMeshAttachment att)
    {
        var rad = att.RotationZDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var scale = att.Scale <= 0 ? 1f : att.Scale;

        var positions = new Point3DCollection(parsed.Positions.Count);
        foreach (var p in parsed.Positions)
        {
            var sx = p.X * scale;
            var sy = p.Y * scale;
            var sz = p.Z * scale;
            var rx = sx * cos - sy * sin;
            var ry = sx * sin + sy * cos;
            positions.Add(new Point3D(
                anchor.X + att.OffsetX + rx,
                anchor.Y + att.OffsetY + ry,
                anchor.Z + att.OffsetZ + sz));
        }

        return new MeshGeometry3D
        {
            Positions = positions,
            TriangleIndices = parsed.TriangleIndices,
        };
    }
}
