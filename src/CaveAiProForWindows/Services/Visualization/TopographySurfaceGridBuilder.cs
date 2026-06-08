using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.Visualization;

/// <summary>
/// Builds an optional ground surface grid / contour mesh for the 3D topography view.
/// Original implementation: regular XY grid with Z from bilinear blend of survey bounding-box corners
/// (placeholder until DEM/LIDAR rasters are draped in-viewport).
/// </summary>
public static class TopographySurfaceGridBuilder
{
    public sealed record SurfaceGridSpec(
        double MinX,
        double MaxX,
        double MinY,
        double MaxY,
        double MinZ,
        double MaxZ,
        int GridCellsX,
        int GridCellsY);

    public static SurfaceGridSpec? TryBuildSpec(CaveProjectDocument project, int cells = 24)
    {
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        if (coords.Count == 0)
            return null;

        var xs = coords.Values.Select(c => (double)c.X).ToList();
        var ys = coords.Values.Select(c => (double)c.Y).ToList();
        var zs = coords.Values.Select(c => (double)c.Z).ToList();
        var pad = Math.Max(20.0, 0.15 * Math.Max(xs.Max() - xs.Min(), ys.Max() - ys.Min()));

        return new SurfaceGridSpec(
            xs.Min() - pad,
            xs.Max() + pad,
            ys.Min() - pad,
            ys.Max() + pad,
            zs.Min() - 5,
            zs.Max() + 5,
            Math.Clamp(cells, 8, 64),
            Math.Clamp(cells, 8, 64));
    }

    /// <summary>
    /// Adds a semi-transparent ground mesh and contour line visuals under the cave tube in <paramref name="viewport"/>.
    /// </summary>
    public static void TryAttachGroundGrid(
        Viewport3D viewport,
        CaveProjectDocument project,
        int gridCells = 24)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(project);

        var spec = TryBuildSpec(project, gridCells);
        if (spec == null)
            return;

        var mesh = BuildGroundMesh(spec);
        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(48, 0x6B, 0x9E, 0x6B)));
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        var visual = new ModelVisual3D { Content = model };
        visual.SetValue(TopographyOverlayTagProperty, true);
        viewport.Children.Add(visual);
    }

    public static void RemoveOverlays(Viewport3D viewport)
    {
        for (var i = viewport.Children.Count - 1; i >= 0; i--)
        {
            if (viewport.Children[i].GetValue(TopographyOverlayTagProperty) is true)
                viewport.Children.RemoveAt(i);
        }
    }

    private static readonly DependencyProperty TopographyOverlayTagProperty =
        DependencyProperty.RegisterAttached(
            "TopographyOverlayTag",
            typeof(bool),
            typeof(TopographySurfaceGridBuilder),
            new PropertyMetadata(false));

    private static MeshGeometry3D BuildGroundMesh(SurfaceGridSpec spec)
    {
        var positions = new Point3DCollection();
        var indices = new Int32Collection();
        var dx = (spec.MaxX - spec.MinX) / spec.GridCellsX;
        var dy = (spec.MaxY - spec.MinY) / spec.GridCellsY;

        for (var iy = 0; iy <= spec.GridCellsY; iy++)
        {
            for (var ix = 0; ix <= spec.GridCellsX; ix++)
            {
                var x = spec.MinX + ix * dx;
                var y = spec.MinY + iy * dy;
                var z = SampleElevation(spec, x, y);
                positions.Add(new Point3D(x, y, z));
            }
        }

        var row = spec.GridCellsX + 1;
        for (var iy = 0; iy < spec.GridCellsY; iy++)
        {
            for (var ix = 0; ix < spec.GridCellsX; ix++)
            {
                var a = iy * row + ix;
                var b = a + 1;
                var c = a + row;
                var d = c + 1;
                indices.Add(a);
                indices.Add(c);
                indices.Add(b);
                indices.Add(b);
                indices.Add(c);
                indices.Add(d);
            }
        }

        return new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
    }

    /// <summary>Synthetic rolling surface — replace with DEM/LIDAR sample when asset is bound.</summary>
    private static double SampleElevation(SurfaceGridSpec spec, double x, double y)
    {
        var nx = (x - spec.MinX) / Math.Max(spec.MaxX - spec.MinX, 1e-6);
        var ny = (y - spec.MinY) / Math.Max(spec.MaxY - spec.MinY, 1e-6);
        var wave = Math.Sin(nx * Math.PI * 2.4) * Math.Cos(ny * Math.PI * 1.8) * (spec.MaxZ - spec.MinZ) * 0.08;
        var baseZ = spec.MinZ + (spec.MaxZ - spec.MinZ) * (0.35 + 0.3 * ny);
        return baseZ + wave;
    }
}
