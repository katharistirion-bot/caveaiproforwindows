using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Fills a <see cref="Viewport3D"/> with a lit solid LRUD tube mesh (no 2D wireframe).
/// </summary>
public static class CaveViewport3DPresenter
{
    private sealed class OrbitState
    {
        public PerspectiveCamera Camera = null!;
        public Point3D Target;
        public double Distance = 40;
        public double Azimuth;
        public double Elevation = 0.35;
        public bool Dragging;
        public Point Last;
    }

    public static bool TryPopulate(Viewport3D viewport, CaveProjectDocument project, Border wheelShell)
    {
        Detach(viewport, wheelShell);

        viewport.Children.Clear();
        viewport.Camera = null;

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var mesh = CaveSurveyTubeMeshBuilder.BuildTubeMesh(project.Shots, coords);
        if (mesh == null)
            return false;

        var legs = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        var traverseStations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in legs)
        {
            traverseStations.Add(s.FromStation);
            traverseStations.Add(s.ToStation);
        }

        var extent = Math.Max(Math.Max(mesh.Bounds.SizeX, mesh.Bounds.SizeY), mesh.Bounds.SizeZ);
        if (extent < 0.5)
            extent = 5;
        var pipeR = Math.Max(0.035, extent * 0.0035);
        var markerR = Math.Max(0.055, pipeR * 1.8);

        var radii = CaveSurveyTubeMeshBuilder.ComputeStationBallJointRadii(project.Shots, coords);
        var traverseMesh = CaveSurveyTubeMeshBuilder.BuildTraverseTubeMesh(project.Shots, coords, pipeR, 8);
        var jointMesh = CaveSurveyTubeMeshBuilder.BuildBallJointSpheresMesh(project.Shots, coords, radii, 10, 14);
        var markerMesh = CaveSurveyTubeMeshBuilder.BuildStationMarkerSpheresMesh(coords, traverseStations, markerR, 6, 8);

        // Light Slate Gray (#778899), semi-transparent wall + stronger ambient so the tube reads on a dark viewport.
        const byte wallAlpha = 140; // ~0.55
        var wallBrush = new SolidColorBrush(Color.FromArgb(wallAlpha, 0x77, 0x88, 0x99));
        var wallBackBrush = new SolidColorBrush(Color.FromArgb(wallAlpha, 0x5A, 0x66, 0x74));
        var wallMat = new MaterialGroup();
        wallMat.Children.Add(new DiffuseMaterial(wallBrush));
        wallMat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(55, 0x99, 0xA8, 0xB8))));
        var wallBack = new MaterialGroup();
        wallBack.Children.Add(new DiffuseMaterial(wallBackBrush));
        wallBack.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(40, 0x70, 0x7C, 0x88))));

        var tube = new GeometryModel3D
        {
            Geometry = mesh,
            Material = wallMat,
            BackMaterial = wallBack,
        };

        var jointMat = new MaterialGroup();
        jointMat.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(175, 0x82, 0x92, 0xA4))));
        jointMat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(45, 0x90, 0x9E, 0xAE))));
        GeometryModel3D? jointsModel = null;
        if (jointMesh != null)
        {
            jointsModel = new GeometryModel3D
            {
                Geometry = jointMesh,
                Material = jointMat,
                BackMaterial = jointMat,
            };
        }

        var cyanBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xDD, 0xEE));
        var traverseMat = new MaterialGroup();
        traverseMat.Children.Add(new DiffuseMaterial(cyanBrush));
        traverseMat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x20, 0x88, 0x98))));
        GeometryModel3D? traverseModel = null;
        if (traverseMesh != null)
        {
            traverseModel = new GeometryModel3D
            {
                Geometry = traverseMesh,
                Material = traverseMat,
                BackMaterial = traverseMat,
            };
        }

        var markerMat = new MaterialGroup();
        markerMat.Children.Add(new DiffuseMaterial(cyanBrush));
        markerMat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x40, 0xEE, 0xFF))));
        GeometryModel3D? markersModel = null;
        if (markerMesh != null)
        {
            markersModel = new GeometryModel3D
            {
                Geometry = markerMesh,
                Material = markerMat,
                BackMaterial = markerMat,
            };
        }

        var lights = new Model3DGroup();
        // ~40% ambient fill
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x66, 0x69, 0x6E)));
        lights.Children.Add(new DirectionalLight
        {
            Color = Colors.White,
            Direction = new Vector3D(-0.45, -0.85, -0.28),
        });

        var root = new Model3DGroup();
        root.Children.Add(lights);
        if (traverseModel != null)
            root.Children.Add(traverseModel);
        if (markersModel != null)
            root.Children.Add(markersModel);
        if (jointsModel != null)
            root.Children.Add(jointsModel);
        root.Children.Add(tube);

        viewport.Children.Add(new ModelVisual3D { Content = root });

        var bounds = UnionMeshBounds(mesh, traverseMesh, jointMesh, markerMesh);
        var center = new Point3D(
            bounds.X + bounds.SizeX * 0.5,
            bounds.Y + bounds.SizeY * 0.5,
            bounds.Z + bounds.SizeZ * 0.5);
        var radius = Math.Max(Math.Max(bounds.SizeX, bounds.SizeY), bounds.SizeZ);
        if (radius < 0.5)
            radius = 5;

        var cam = new PerspectiveCamera
        {
            FieldOfView = 50,
            NearPlaneDistance = Math.Max(0.05, radius * 1e-4),
            FarPlaneDistance = Math.Max(5000, radius * 50),
            UpDirection = new Vector3D(0, 0, 1),
        };

        var st = new OrbitState
        {
            Camera = cam,
            Target = center,
            Distance = radius * 2.4,
            Azimuth = 0.65,
            Elevation = 0.38,
        };
        ApplyOrbitCamera(st);
        viewport.Camera = cam;
        viewport.Tag = st;

        viewport.MouseDown -= Viewport_MouseDown;
        viewport.MouseMove -= Viewport_MouseMove;
        viewport.MouseUp -= Viewport_MouseUp;
        viewport.MouseDown += Viewport_MouseDown;
        viewport.MouseMove += Viewport_MouseMove;
        viewport.MouseUp += Viewport_MouseUp;

        wheelShell.Tag = st;
        wheelShell.PreviewMouseWheel += Shell_PreviewMouseWheel;

        return true;
    }

    private static Rect3D UnionMeshBounds(params MeshGeometry3D?[] meshes)
    {
        var has = false;
        var u = new Rect3D();
        foreach (var m in meshes)
        {
            if (m == null)
                continue;
            var b = m.Bounds;
            if (!has)
            {
                u = b;
                has = true;
            }
            else
            {
                u = Rect3D.Union(u, b);
            }
        }

        return has ? u : new Rect3D(0, 0, 0, 1, 1, 1);
    }

    private static void Shell_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Border b || b.Tag is not OrbitState st)
            return;
        var f = e.Delta > 0 ? 0.92 : 1.09;
        st.Distance = Math.Clamp(st.Distance * f, 0.5, 1e6);
        ApplyOrbitCamera(st);
        e.Handled = true;
    }

    public static void Detach(Viewport3D viewport, Border? wheelShell)
    {
        viewport.MouseDown -= Viewport_MouseDown;
        viewport.MouseMove -= Viewport_MouseMove;
        viewport.MouseUp -= Viewport_MouseUp;
        viewport.Tag = null;
        if (wheelShell != null)
        {
            wheelShell.PreviewMouseWheel -= Shell_PreviewMouseWheel;
            wheelShell.Tag = null;
        }
    }

    private static void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Viewport3D vp || vp.Tag is not OrbitState st)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        st.Dragging = true;
        st.Last = e.GetPosition(vp);
        vp.CaptureMouse();
    }

    private static void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Viewport3D vp || vp.Tag is not OrbitState st || !st.Dragging)
            return;
        var p = e.GetPosition(vp);
        var dx = p.X - st.Last.X;
        var dy = p.Y - st.Last.Y;
        st.Last = p;
        st.Azimuth -= dx * 0.008;
        st.Elevation = Math.Clamp(st.Elevation - dy * 0.008, -1.45, 1.45);
        ApplyOrbitCamera(st);
    }

    private static void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Viewport3D vp || vp.Tag is not OrbitState st)
            return;
        st.Dragging = false;
        vp.ReleaseMouseCapture();
    }

    private static void ApplyOrbitCamera(OrbitState st)
    {
        var ca = Math.Cos(st.Azimuth);
        var sa = Math.Sin(st.Azimuth);
        var ce = Math.Cos(st.Elevation);
        var se = Math.Sin(st.Elevation);
        var dir = new Vector3D(ce * sa, ce * ca, se);
        dir.Normalize();
        st.Camera.Position = st.Target - dir * st.Distance;
        st.Camera.LookDirection = dir;
    }
}
