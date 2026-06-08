using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Visualization;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Lit LRUD tube mesh, traverse centreline, splays, vectors, and screen-space survey labels.
/// </summary>
public static class CaveViewport3DPresenter
{
    internal sealed class OrbitState
    {
        public PerspectiveCamera Camera = null!;
        public Viewport3D Viewport = null!;
        public Canvas? LabelCanvas;
        public IReadOnlyList<Viewport3DLabelEntry> Labels = Array.Empty<Viewport3DLabelEntry>();
        public CaveProjectDocument? Project;
        public IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> Coords =
            new Dictionary<string, SurveyStationGeometry.StationPlanCoords>();
        public Action<SurveyPickResult?>? OnPick;
        public Action<Viewport3DLabelEntry>? OnLabelClick;
        public double LabelScale = 1.0;
        public string? HighlightStation;
        public Point DragStart;
        public Point3D Target;
        public double Distance = 40;
        public double Azimuth;
        public double Elevation = 0.35;
        public bool Dragging;
        public bool OrbitGestureActive;
        public Point Last;
        public Model3DGroup? RootGroup;
        public GeometryModel3D? HighlightModel;
        public List<Viewport3DLabelHitTarget> LabelHitTargets = new();
    }

    private static readonly Dictionary<string, CachedTubeMesh> TubeMeshCache = new(StringComparer.Ordinal);

    private sealed class CachedTubeMesh
    {
        public required MeshGeometry3D Mesh { get; init; }
        public required bool HasLrudVolume { get; init; }
    }

    public static bool TryPopulate(
        Viewport3D viewport,
        CaveProjectDocument project,
        Border wheelShell,
        Canvas? labelCanvas = null,
        Viewport3DDisplayOptions? display = null,
        Action<SurveyPickResult?>? onPick = null,
        string? highlightStation = null,
        Action<Viewport3DLabelEntry>? onLabelClick = null,
        double labelScale = 1.0)
    {
        display ??= Viewport3DDisplayOptions.Default;
        Detach(viewport, wheelShell, labelCanvas);

        viewport.Children.Clear();
        viewport.Camera = null;
        labelCanvas?.Children.Clear();

        if (display.ShowTopographyGrid)
            TopographySurfaceGridBuilder.TryAttachGroundGrid(viewport, project);
        if (display.ShowDemSurface)
            DemSurfaceMeshBuilder.TryAttachDemSurface(viewport, project, project.LoadedFromFile);

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var fingerprint = ComputeTubeFingerprint(project);
        MeshGeometry3D mesh;
        bool hasLrudVolume;
        if (TubeMeshCache.TryGetValue(fingerprint, out var cached))
        {
            mesh = cached.Mesh;
            hasLrudVolume = cached.HasLrudVolume;
        }
        else
        {
            var built = CaveSurveyTubeMeshBuilder.BuildTubeMesh(project.Shots, coords);
            if (built == null)
                return false;
            mesh = built;
            hasLrudVolume = mesh.Positions.Count > 48;
            TubeMeshCache[fingerprint] = new CachedTubeMesh { Mesh = mesh, HasLrudVolume = hasLrudVolume };
            if (TubeMeshCache.Count > 12)
            {
                var drop = TubeMeshCache.Keys.First();
                TubeMeshCache.Remove(drop);
            }
        }

        var hasLrudVolumeLocal = hasLrudVolume;

        var (lines, labels) = CaveViewport3DAnnotationsBuilder.Build(
            project,
            coords,
            display.ResolvedAnnotations);

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
        if (hasLrudVolumeLocal)
            pipeR *= 0.35;
        var markerR = Math.Max(0.055, pipeR * 1.8);
        var splayR = Math.Max(0.012, pipeR * 0.45);
        var vectorR = Math.Max(0.014, pipeR * 0.5);

        var radii = CaveSurveyTubeMeshBuilder.ComputeStationBallJointRadii(project.Shots, coords);
        var traverseMesh = hasLrudVolumeLocal
            ? null
            : CaveSurveyTubeMeshBuilder.BuildTraverseTubeMesh(project.Shots, coords, pipeR, 8);
        var jointMesh = hasLrudVolumeLocal
            ? null
            : CaveSurveyTubeMeshBuilder.BuildBallJointSpheresMesh(project.Shots, coords, radii, 10, 14);
        var markerMesh = CaveSurveyTubeMeshBuilder.BuildStationMarkerSpheresMesh(coords, traverseStations, markerR, 6, 8);
        var splayMesh = display.ShowSplines && lines.SplaySegments.Count > 0
            ? CaveSurveyTubeMeshBuilder.BuildLineSegmentsMesh(lines.SplaySegments, splayR, 4)
            : null;
        var vectorMesh = display.ShowSplines && lines.VectorSegments.Count > 0
            ? CaveSurveyTubeMeshBuilder.BuildLineSegmentsMesh(lines.VectorSegments, vectorR, 4)
            : null;
        var depthMesh = display.ShowSplines && lines.DepthSpanSegments.Count > 0
            ? CaveSurveyTubeMeshBuilder.BuildLineSegmentsMesh(lines.DepthSpanSegments, vectorR * 1.1, 4)
            : null;

        const byte wallAlpha = 185;
        var wallBrush = new SolidColorBrush(Color.FromArgb(wallAlpha, 0xA8, 0x90, 0x78));
        var wallBackBrush = new SolidColorBrush(Color.FromArgb(wallAlpha, 0x78, 0x68, 0x58));
        var wallMat = new MaterialGroup();
        wallMat.Children.Add(new DiffuseMaterial(wallBrush));
        wallMat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(90, 0xFF, 0xFF, 0xFF)), 24));
        wallMat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(40, 0xE8, 0xDC, 0xC8))));
        var wallBack = new MaterialGroup();
        wallBack.Children.Add(new DiffuseMaterial(wallBackBrush));
        wallBack.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(40, 0x70, 0x7C, 0x88))));

        var tube = new GeometryModel3D { Geometry = mesh, Material = wallMat, BackMaterial = wallBack };

        var jointMat = new MaterialGroup();
        jointMat.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(175, 0x82, 0x92, 0xA4))));
        jointMat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(45, 0x90, 0x9E, 0xAE))));
        GeometryModel3D? jointsModel = jointMesh == null
            ? null
            : new GeometryModel3D { Geometry = jointMesh, Material = jointMat, BackMaterial = jointMat };

        var cyanBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xDD, 0xEE));
        var traverseMat = new MaterialGroup();
        traverseMat.Children.Add(new DiffuseMaterial(cyanBrush));
        traverseMat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x20, 0x88, 0x98))));
        GeometryModel3D? traverseModel = traverseMesh == null
            ? null
            : new GeometryModel3D { Geometry = traverseMesh, Material = traverseMat, BackMaterial = traverseMat };

        var markerMat = new MaterialGroup();
        markerMat.Children.Add(new DiffuseMaterial(cyanBrush));
        markerMat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x40, 0xEE, 0xFF))));
        GeometryModel3D? markersModel = markerMesh == null
            ? null
            : new GeometryModel3D { Geometry = markerMesh, Material = markerMat, BackMaterial = markerMat };

        var splayMat = CreateLineMaterial(Color.FromArgb(200, 0xF5, 0x9E, 0x0B));
        GeometryModel3D? splayModel = splayMesh == null
            ? null
            : new GeometryModel3D { Geometry = splayMesh, Material = splayMat, BackMaterial = splayMat };

        var vectorMat = CreateLineMaterial(Color.FromArgb(180, 0x38, 0xBD, 0xF8));
        GeometryModel3D? vectorModel = vectorMesh == null
            ? null
            : new GeometryModel3D { Geometry = vectorMesh, Material = vectorMat, BackMaterial = vectorMat };

        var depthMat = CreateLineMaterial(Color.FromArgb(210, 0xEA, 0x58, 0x0C));
        GeometryModel3D? depthModel = depthMesh == null
            ? null
            : new GeometryModel3D { Geometry = depthMesh, Material = depthMat, BackMaterial = depthMat };

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x78, 0x7C, 0x82)));
        lights.Children.Add(new DirectionalLight
        {
            Color = Color.FromRgb(0xFF, 0xFA, 0xF5),
            Direction = new Vector3D(-0.45, -0.85, -0.28),
        });
        lights.Children.Add(new DirectionalLight
        {
            Color = Color.FromRgb(0x88, 0xAA, 0xCC),
            Direction = new Vector3D(0.35, 0.25, -0.92),
        });

        var root = new Model3DGroup();
        root.Children.Add(lights);
        if (vectorModel != null)
            root.Children.Add(vectorModel);
        if (splayModel != null)
            root.Children.Add(splayModel);
        if (depthModel != null)
            root.Children.Add(depthModel);
        if (traverseModel != null)
            root.Children.Add(traverseModel);
        if (markersModel != null)
            root.Children.Add(markersModel);
        if (jointsModel != null)
            root.Children.Add(jointsModel);
        root.Children.Add(tube);

        var bounds = UnionMeshBounds(mesh, traverseMesh, jointMesh, markerMesh, splayMesh, vectorMesh, depthMesh);
        if (display.SectionCut is { Enabled: true } cut)
        {
            var plane = Viewport3DSectionCutBuilder.TryBuildCutPlane(bounds, cut);
            if (plane != null)
                root.Children.Add(plane);
        }

        viewport.Children.Add(new ModelVisual3D { Content = root });

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
            Viewport = viewport,
            LabelCanvas = labelCanvas,
            Labels = labels,
            Project = project,
            Coords = coords,
            OnPick = onPick,
            OnLabelClick = onLabelClick,
            LabelScale = labelScale,
            HighlightStation = highlightStation,
            Target = center,
            Distance = radius * 2.4,
            Azimuth = 0.65,
            Elevation = 0.38,
            RootGroup = root,
        };
        ApplyPickHighlight(st);
        ApplyOrbitCamera(st);
        viewport.Camera = cam;
        viewport.Tag = st;

        viewport.MouseDown -= Viewport_MouseDown;
        viewport.MouseMove -= Viewport_MouseMove;
        viewport.MouseUp -= Viewport_MouseUp;
        viewport.SizeChanged -= Viewport_SizeChanged;
        viewport.MouseDown += Viewport_MouseDown;
        viewport.MouseMove += Viewport_MouseMove;
        viewport.MouseUp += Viewport_MouseUp;
        viewport.SizeChanged += Viewport_SizeChanged;

        wheelShell.Tag = st;
        wheelShell.PreviewMouseWheel -= Shell_PreviewMouseWheel;
        wheelShell.PreviewMouseWheel += Shell_PreviewMouseWheel;
        wheelShell.PreviewMouseLeftButtonDown -= Shell_PreviewMouseLeftButtonDown;
        wheelShell.PreviewMouseLeftButtonDown += Shell_PreviewMouseLeftButtonDown;
        wheelShell.PreviewMouseMove -= Shell_PreviewMouseMove;
        wheelShell.PreviewMouseMove += Shell_PreviewMouseMove;
        wheelShell.PreviewMouseLeftButtonUp -= Shell_PreviewMouseLeftButtonUp;
        wheelShell.PreviewMouseLeftButtonUp += Shell_PreviewMouseLeftButtonUp;

        RefreshLabels(st);
        ScheduleLabelRefresh(st);
        return true;
    }

    internal static void ApplyOrbitCameraPublic(OrbitState st) => ApplyOrbitCamera(st);

    internal static OrbitState? TryGetState(Viewport3D viewport) =>
        viewport.Tag as OrbitState;

    public static void SetHighlightStation(Viewport3D viewport, string? stationName)
    {
        if (viewport.Tag is not OrbitState st)
            return;
        st.HighlightStation = stationName;
        ApplyPickHighlight(st);
    }

    private static void ApplyPickHighlight(OrbitState st)
    {
        if (st.RootGroup == null)
            return;
        if (st.HighlightModel != null)
        {
            st.RootGroup.Children.Remove(st.HighlightModel);
            st.HighlightModel = null;
        }

        if (string.IsNullOrWhiteSpace(st.HighlightStation))
            return;
        if (!st.Coords.TryGetValue(st.HighlightStation.Trim(), out var c))
        {
            var match = st.Coords.Keys.FirstOrDefault(k =>
                string.Equals(k.Trim(), st.HighlightStation.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null || !st.Coords.TryGetValue(match, out c))
                return;
        }

        var sphere = new MeshGeometry3D();
        const int seg = 12;
        const double r = 0.22;
        var center = new Point3D(c.X, c.Y, c.Z);
        for (var lat = 0; lat <= seg; lat++)
        {
            var v = lat / (double)seg;
            var phi = v * Math.PI;
            for (var lon = 0; lon <= seg; lon++)
            {
                var u = lon / (double)seg;
                var theta = u * Math.PI * 2;
                sphere.Positions.Add(new Point3D(
                    center.X + r * Math.Sin(phi) * Math.Cos(theta),
                    center.Y + r * Math.Sin(phi) * Math.Sin(theta),
                    center.Z + r * Math.Cos(phi)));
            }
        }

        for (var lat = 0; lat < seg; lat++)
        {
            for (var lon = 0; lon < seg; lon++)
            {
                var a = lat * (seg + 1) + lon;
                var b = a + seg + 1;
                sphere.TriangleIndices.Add(a);
                sphere.TriangleIndices.Add(b);
                sphere.TriangleIndices.Add(a + 1);
                sphere.TriangleIndices.Add(a + 1);
                sphere.TriangleIndices.Add(b);
                sphere.TriangleIndices.Add(b + 1);
            }
        }

        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(90, 0x2D, 0xD4, 0xBF))));
        mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(120, 0x5E, 0xE7, 0xDF))));
        st.HighlightModel = new GeometryModel3D { Geometry = sphere, Material = mat, BackMaterial = mat };
        st.RootGroup.Children.Add(st.HighlightModel);
    }

    private static void ScheduleLabelRefresh(OrbitState st)
    {
        st.Viewport.Dispatcher.BeginInvoke(
            () => RefreshLabels(st),
            System.Windows.Threading.DispatcherPriority.Loaded);
        st.Viewport.Dispatcher.BeginInvoke(
            () => RefreshLabels(st),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private static MaterialGroup CreateLineMaterial(Color color)
    {
        var brush = new SolidColorBrush(color);
        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(brush));
        mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B))));
        return mat;
    }

    private static void RefreshLabels(OrbitState st) =>
        CaveViewport3DLabelOverlay.Sync(
            st.Viewport,
            st.LabelCanvas,
            st.Labels,
            st.LabelScale,
            st.OnLabelClick,
            st.LabelHitTargets);

    private static string ComputeTubeFingerprint(CaveProjectDocument project)
    {
        var sb = new StringBuilder();
        sb.Append(project.Name).Append('|').Append(project.Shots.Count);
        foreach (var s in project.Shots)
        {
            sb.Append('|')
                .Append(s.FromStation).Append('>').Append(s.ToStation)
                .Append(':').Append(s.Distance.ToString("0.###"))
                .Append(':').Append(s.L).Append(':').Append(s.R)
                .Append(':').Append(s.U).Append(':').Append(s.D);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
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

    private static void Shell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not OrbitState st)
            return;
        if (e.ChangedButton != MouseButton.Left)
            return;
        st.Dragging = true;
        st.OrbitGestureActive = false;
        st.Last = e.GetPosition(st.Viewport);
        st.DragStart = st.Last;
        b.CaptureMouse();
    }

    private static void Shell_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border b || b.Tag is not OrbitState st || !st.Dragging)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var p = e.GetPosition(st.Viewport);
        if (!st.OrbitGestureActive && (p - st.DragStart).Length >= 4)
            st.OrbitGestureActive = true;
        if (!st.OrbitGestureActive)
            return;

        var dx = p.X - st.Last.X;
        var dy = p.Y - st.Last.Y;
        st.Last = p;
        st.Azimuth -= dx * 0.008;
        st.Elevation = Math.Clamp(st.Elevation - dy * 0.008, -1.45, 1.45);
        ApplyOrbitCamera(st);
        e.Handled = true;
    }

    private static void Shell_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not OrbitState st)
            return;

        var p = e.GetPosition(st.Viewport);
        var moved = (p - st.DragStart).Length;
        st.Dragging = false;
        b.ReleaseMouseCapture();

        if (st.OrbitGestureActive)
        {
            st.OrbitGestureActive = false;
            e.Handled = true;
            RefreshLabels(st);
            return;
        }

        st.OrbitGestureActive = false;

        if (moved < 5)
        {
            var labelEntry = HitTestLabel(st, p);
            if (labelEntry != null && st.OnLabelClick != null)
            {
                st.OnLabelClick(labelEntry);
                e.Handled = true;
                return;
            }

            if (st.Project != null && st.OnPick != null)
            {
                var pick = CaveViewport3DPickService.TryPick(st.Viewport, p, st.Project, st.Coords);
                st.OnPick(pick);
            }
        }

        RefreshLabels(st);
    }

    private static Viewport3DLabelEntry? HitTestLabel(OrbitState st, Point p)
    {
        for (var i = st.LabelHitTargets.Count - 1; i >= 0; i--)
        {
            if (st.LabelHitTargets[i].Bounds.Contains(p))
                return st.LabelHitTargets[i].Entry;
        }

        return null;
    }

    public static void Detach(Viewport3D viewport, Border? wheelShell, Canvas? labelCanvas = null)
    {
        viewport.MouseDown -= Viewport_MouseDown;
        viewport.MouseMove -= Viewport_MouseMove;
        viewport.MouseUp -= Viewport_MouseUp;
        viewport.SizeChanged -= Viewport_SizeChanged;
        viewport.Tag = null;
        if (wheelShell != null)
        {
            wheelShell.PreviewMouseWheel -= Shell_PreviewMouseWheel;
            wheelShell.PreviewMouseLeftButtonDown -= Shell_PreviewMouseLeftButtonDown;
            wheelShell.PreviewMouseMove -= Shell_PreviewMouseMove;
            wheelShell.PreviewMouseLeftButtonUp -= Shell_PreviewMouseLeftButtonUp;
            wheelShell.ReleaseMouseCapture();
            wheelShell.Tag = null;
        }

        labelCanvas?.Children.Clear();
        if (wheelShell?.Tag is OrbitState detachSt)
            detachSt.LabelHitTargets.Clear();
    }

    private static void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Viewport3D vp && vp.Tag is OrbitState st)
            RefreshLabels(st);
    }

    private static void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Viewport3D vp || vp.Tag is not OrbitState st)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        st.Dragging = true;
        st.Last = e.GetPosition(vp);
        st.DragStart = st.Last;
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
        var p = e.GetPosition(vp);
        var moved = (p - st.DragStart).Length;
        st.Dragging = false;
        vp.ReleaseMouseCapture();
        if (moved < 5 && st.Project != null && st.OnPick != null)
        {
            var pick = CaveViewport3DPickService.TryPick(vp, p, st.Project, st.Coords);
            st.OnPick(pick);
        }

        RefreshLabels(st);
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
        RefreshLabels(st);
    }
}
