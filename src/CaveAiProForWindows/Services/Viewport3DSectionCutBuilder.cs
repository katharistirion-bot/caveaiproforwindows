using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Section-cut plane resolution and optional subtle cut-plane overlay.</summary>
public static class Viewport3DSectionCutBuilder
{
    /// <summary>Plane point and outward normal; geometry with non-negative signed distance is kept.</summary>
    public static (Point3D PlanePoint, Vector3D Normal) ResolveCutPlane(
        Rect3D bounds,
        Viewport3DSectionCutOptions cut)
    {
        var t = Math.Clamp(cut.NormalizedPosition, 0.02, 0.98);
        return cut.Axis switch
        {
            Viewport3DSectionCutAxis.VerticalX => (
                new Point3D(bounds.X + bounds.SizeX * t, 0, 0),
                new Vector3D(-1, 0, 0)),
            Viewport3DSectionCutAxis.VerticalY => (
                new Point3D(0, bounds.Y + bounds.SizeY * t, 0),
                new Vector3D(0, -1, 0)),
            _ => (
                new Point3D(0, 0, bounds.Z + bounds.SizeZ * t),
                new Vector3D(0, 0, -1)),
        };
    }

    public static MeshGeometry3D? ClipMesh(MeshGeometry3D? mesh, Rect3D bounds, Viewport3DSectionCutOptions cut)
    {
        if (!cut.Enabled || mesh == null)
            return mesh;
        var (planePoint, normal) = ResolveCutPlane(bounds, cut);
        return MeshPlaneClipper.ClipKeepPositiveHalfSpace(mesh, planePoint, normal);
    }

    public static GeometryModel3D? TryBuildCutPlane(
        Rect3D bounds,
        Viewport3DSectionCutOptions cut,
        bool subtle = true)
    {
        if (!cut.Enabled)
            return null;

        var t = Math.Clamp(cut.NormalizedPosition, 0.02, 0.98);
        var pad = Math.Max(2.0, Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)) * 0.08);
        Point3D p0, p1, p2, p3;
        switch (cut.Axis)
        {
            case Viewport3DSectionCutAxis.VerticalX:
            {
                var x = bounds.X + bounds.SizeX * t;
                p0 = new Point3D(x, bounds.Y - pad, bounds.Z - pad);
                p1 = new Point3D(x, bounds.Y + bounds.SizeY + pad, bounds.Z - pad);
                p2 = new Point3D(x, bounds.Y + bounds.SizeY + pad, bounds.Z + bounds.SizeZ + pad);
                p3 = new Point3D(x, bounds.Y - pad, bounds.Z + bounds.SizeZ + pad);
                break;
            }
            case Viewport3DSectionCutAxis.VerticalY:
            {
                var y = bounds.Y + bounds.SizeY * t;
                p0 = new Point3D(bounds.X - pad, y, bounds.Z - pad);
                p1 = new Point3D(bounds.X + bounds.SizeX + pad, y, bounds.Z - pad);
                p2 = new Point3D(bounds.X + bounds.SizeX + pad, y, bounds.Z + bounds.SizeZ + pad);
                p3 = new Point3D(bounds.X - pad, y, bounds.Z + bounds.SizeZ + pad);
                break;
            }
            default:
            {
                var z = bounds.Z + bounds.SizeZ * t;
                p0 = new Point3D(bounds.X - pad, bounds.Y - pad, z);
                p1 = new Point3D(bounds.X + bounds.SizeX + pad, bounds.Y - pad, z);
                p2 = new Point3D(bounds.X + bounds.SizeX + pad, bounds.Y + bounds.SizeY + pad, z);
                p3 = new Point3D(bounds.X - pad, bounds.Y + bounds.SizeY + pad, z);
                break;
            }
        }

        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(p0);
        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);

        var alpha = subtle ? (byte)28 : (byte)62;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0x0D, 0x94, 0x88));
        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(brush));
        if (!subtle)
        {
            mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(70, 0xCC, 0xFF, 0xF7)), 18));
            mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(42, 0x5E, 0xE7, 0xDF))));
        }
        return new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat };
    }
}
