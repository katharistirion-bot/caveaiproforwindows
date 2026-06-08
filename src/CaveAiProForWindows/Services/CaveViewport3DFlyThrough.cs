using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services;

/// <summary>Animates the 3D camera along traverse stations.</summary>
public sealed class CaveViewport3DFlyThrough : IDisposable
{
    private readonly Viewport3D _viewport;
    private readonly Border _shell;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<Point3D> _path = Array.Empty<Point3D>();
    private int _index;
    private double _phase;

    public CaveViewport3DFlyThrough(Viewport3D viewport, Border shell, CaveProjectDocument project)
    {
        _viewport = viewport;
        _shell = shell;
        _path = BuildPath(project);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (_path.Count < 2)
            return;
        _index = 0;
        _phase = 0;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_viewport.Tag is not CaveViewport3DPresenter.OrbitState st || _path.Count < 2)
        {
            Stop();
            return;
        }

        _phase += 0.018;
        if (_phase >= 1)
        {
            _phase = 0;
            _index = (_index + 1) % (_path.Count - 1);
        }

        var a = _path[_index];
        var b = _path[_index + 1];
        var t = _phase;
        st.Target = new Point3D(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);
        st.Azimuth += 0.012;
        CaveViewport3DPresenter.ApplyOrbitCameraPublic(st);
    }

    private static IReadOnlyList<Point3D> BuildPath(CaveProjectDocument project)
    {
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var legs = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        if (legs.Count == 0)
            return Array.Empty<Point3D>();

        var path = new List<Point3D>();
        var root = (legs[0].FromStation ?? "").Trim();
        if (coords.TryGetValue(root, out var c0))
            path.Add(new Point3D(c0.X, c0.Y, c0.Z));

        var current = root;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        while (path.Count < coords.Count)
        {
            var nextLeg = legs.FirstOrDefault(l =>
                string.Equals(l.FromStation?.Trim(), current, StringComparison.OrdinalIgnoreCase));
            if (nextLeg == null)
                break;
            var next = (nextLeg.ToStation ?? "").Trim();
            if (!coords.TryGetValue(next, out var cn))
                break;
            path.Add(new Point3D(cn.X, cn.Y, cn.Z));
            if (!used.Add(next))
                break;
            current = next;
        }

        return path;
    }
}
