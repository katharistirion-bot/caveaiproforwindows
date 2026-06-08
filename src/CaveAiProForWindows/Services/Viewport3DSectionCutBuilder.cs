using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Semi-transparent cutting plane for pseudo-3D section preview.</summary>
public static class Viewport3DSectionCutBuilder
{
    public static GeometryModel3D? TryBuildCutPlane(
        Rect3D bounds,
        Viewport3DSectionCutOptions cut)
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

        var brush = new SolidColorBrush(Color.FromArgb(48, 0x0D, 0x94, 0x88));
        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(brush));
        mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(30, 0x5E, 0xE7, 0xDF))));
        return new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat };
    }
}
