using System.Windows.Controls;

using System.Windows.Media;

using System.Windows.Media.Media3D;

using CaveAiProForWindows.Models;

using CaveAiProForWindows.Services.Visualization;



namespace CaveAiProForWindows.Services;



/// <summary>

/// Builds a ground mesh draped on GeoTIFF elevation when available, else inverse-distance weighting from station Z.

/// </summary>

public static class DemSurfaceMeshBuilder

{

    public static void TryAttachDemSurface(Viewport3D viewport, CaveProjectDocument project, string? zipPath = null)

    {

        var spec = TopographySurfaceGridBuilder.TryBuildSpec(project, 32);

        if (spec == null)

            return;



        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);

        if (coords.Count == 0)

            return;



        zipPath ??= project.LoadedFromFile?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true

            ? project.LoadedFromFile

            : ZipMapSiblingResolver.TryResolve(project.LoadedFromFile, project);



        using var dem = GeoTiffElevationSampler.TryOpenDem(project, zipPath);

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

                var z = dem != null

                    ? GeoTiffElevationSampler.SampleElevation(dem, x, y, spec.MinZ)

                    : SampleZFromStations(coords, x, y, spec.MinZ);

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

            new SolidColorBrush(Color.FromArgb(dem != null ? (byte)110 : (byte)90, 0x6B, 0x72, 0x80))));

        var model = new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat };

        viewport.Children.Add(new ModelVisual3D { Content = model });

    }



    private static double SampleZFromStations(

        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,

        double x,

        double y,

        double fallback)

    {

        double num = 0;

        double den = 0;

        foreach (var c in coords.Values)

        {

            var d2 = (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y);

            if (d2 < 1e-6)

                return c.Z;

            var w = 1.0 / d2;

            num += w * c.Z;

            den += w;

        }



        return den > 0 ? num / den : fallback;

    }

}

