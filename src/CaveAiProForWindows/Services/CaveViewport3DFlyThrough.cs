using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services;

/// <summary>Animates the 3D camera along the LRUD centerline, looking ahead through the passage.</summary>
public sealed class CaveViewport3DFlyThrough : IDisposable
{
    private readonly Viewport3D _viewport;
    private readonly UIElement? _labelChrome;
    private readonly UIElement? _hudChrome;
    private readonly DispatcherTimer _timer;
    private readonly IReadOnlyList<CaveViewport3DFlyThroughPathBuilder.PathSample> _path;
    private double _progress;

    public CaveViewport3DFlyThrough(
        Viewport3D viewport,
        Border shell,
        CaveProjectDocument project,
        Canvas? labelCanvas = null,
        UIElement? hudChrome = null)
    {
        _ = shell;
        _viewport = viewport;
        _labelChrome = labelCanvas;
        _hudChrome = hudChrome;
        _path = CaveViewport3DFlyThroughPathBuilder.BuildPath(project);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.IsEnabled;

    public int PathSampleCount => _path.Count;

    public event Action? Completed;

    public void Start()
    {
        if (_path.Count < 2)
            return;

        SetChromeVisible(false);
        _progress = 0;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        if (_viewport.Tag is CaveViewport3DPresenter.OrbitState st)
            CaveViewport3DPresenter.EndFlyThroughCamera(st);
        SetChromeVisible(true);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        Stop();
    }

    private void SetChromeVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        if (_labelChrome != null)
            _labelChrome.Visibility = v;
        if (_hudChrome != null)
            _hudChrome.Visibility = v;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_viewport.Tag is not CaveViewport3DPresenter.OrbitState st || _path.Count < 2)
        {
            Stop();
            Completed?.Invoke();
            return;
        }

        _progress += 0.0028;
        if (_progress >= 1)
        {
            Stop();
            Completed?.Invoke();
            return;
        }

        if (!CaveViewport3DFlyThroughPathBuilder.TrySampleTunnelCamera(
                _path, _progress, lookAheadNormalized: 0.045, out var eye, out var lookDirection))
        {
            Stop();
            Completed?.Invoke();
            return;
        }

        CaveViewport3DPresenter.ApplyTunnelCameraPublic(st, eye, lookDirection, flyThrough: true);
    }
}
