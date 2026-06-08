using System.Windows.Controls;

using System.Windows.Media;

using System.Windows.Media.Media3D;

using CaveAiProForWindows.Models;

using CaveAiProForWindows.Services.Visualization;



namespace CaveAiProForWindows.Services;



/// <summary>
/// Builds a ground mesh draped on GeoTIFF elevation when the project zip contains a DEM raster.
/// </summary>

public static class DemSurfaceMeshBuilder

{

    public static void TryAttachDemSurface(Viewport3D viewport, CaveProjectDocument project, string? zipPath = null)

    {

        var spec = TopographySurfaceGridBuilder.TryBuildSpec(project, 32);

        if (spec == null)
            return;

        zipPath ??= project.LoadedFromFile?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true

            ? project.LoadedFromFile

            : ZipMapSiblingResolver.TryResolve(project.LoadedFromFile, project);



        using var dem = GeoTiffElevationSampler.TryOpenDem(project, zipPath);
        if (dem == null)
            return;

        var mesh = new MeshGeometry3D();

        var nx = spec.GridCellsX;

        var ny = spec.GridCellsY;

        var dx = (spec.MaxX - spec.MinX) / nx;

        var dy = (spec.MaxY - spec.MinY) / ny;



        for (var iy = 0; iy <= ny; iy++)

        {

            for (var ix = 0; ix <= nx; ix++)

            {

                var x = spec.MinX + ix * dx;

                var y = spec.MinY + iy * dy;

                var z = GeoTiffElevationSampler.SampleElevation(dem, x, y, spec.MinZ);

                mesh.Positions.Add(new Point3D(x, y, z - 0.15));

            }

        }



        for (var iy = 0; iy < ny; iy++)

        {

            for (var ix = 0; ix < nx; ix++)

            {

                var a = iy * (nx + 1) + ix;

                var b = a + nx + 1;

                mesh.TriangleIndices.Add(a);

                mesh.TriangleIndices.Add(b);

                mesh.TriangleIndices.Add(a + 1);

                mesh.TriangleIndices.Add(a + 1);

                mesh.TriangleIndices.Add(b);

                mesh.TriangleIndices.Add(b + 1);

            }

        }



        var mat = new MaterialGroup();

        mat.Children.Add(new DiffuseMaterial(
            new SolidColorBrush(Color.FromArgb(110, 0x6B, 0x72, 0x80))));

        var model = new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat };

        viewport.Children.Add(new ModelVisual3D { Content = model });
    }
}

