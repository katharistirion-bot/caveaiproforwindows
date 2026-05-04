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

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project.Shots, (float)project.Alt);
        var mesh = CaveSurveyTubeMeshBuilder.BuildTubeMesh(project.Shots, coords);
        if (mesh == null)
            return false;

        var rock = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x86, 0x82, 0x7C)));
        var rockDark = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x58, 0x55, 0x52)));
        var tube = new GeometryModel3D
        {
            Geometry = mesh,
            Material = rock,
            BackMaterial = rockDark,
        };

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x40, 0x42, 0x48)));
        lights.Children.Add(new DirectionalLight
        {
            Color = Colors.White,
            Direction = new Vector3D(-0.45, -0.85, -0.28),
        });

        var root = new Model3DGroup();
        root.Children.Add(lights);
        root.Children.Add(tube);

        viewport.Children.Add(new ModelVisual3D { Content = root });

        var bounds = mesh.Bounds;
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
