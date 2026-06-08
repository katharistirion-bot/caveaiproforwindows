using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services;

/// <summary>Captures fly-through frames and optionally encodes MP4 via ffmpeg when available.</summary>
public sealed class CaveViewport3DFlyThroughRecorder : IDisposable
{
    private readonly Viewport3D _viewport;
    private readonly Grid _renderRoot;
    private readonly CaveProjectDocument _project;
    private readonly DispatcherTimer _timer;
    private readonly List<string> _framePaths = new();
    private readonly string _tempDir;
    private IReadOnlyList<Point3D> _path = Array.Empty<Point3D>();
    private int _index;
    private double _phase;
    private int _frameIndex;
    private bool _recording;
    private int _targetFrames = 120;

    public CaveViewport3DFlyThroughRecorder(Viewport3D viewport, Border shell, CaveProjectDocument project, int targetFrames = 120)
    {
        _viewport = viewport;
        _project = project;
        _targetFrames = Math.Clamp(targetFrames, 24, 600);
        _renderRoot = new Grid { Width = viewport.Width, Height = viewport.Height };
        _renderRoot.Children.Add(viewport);
        _path = BuildPath(project);
        _tempDir = Path.Combine(Path.GetTempPath(), "caveai-flythrough-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start() => StartRecording();

    public void StartRecording()
    {
        if (_path.Count < 2)
            return;
        _index = 0;
        _phase = 0;
        _frameIndex = 0;
        _framePaths.Clear();
        _recording = true;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    public async Task<FlyThroughRecordResult> FinishAsync(string? mp4OutputPath = null)
    {
        _recording = false;
        _timer.Stop();
        await Task.Delay(50).ConfigureAwait(true);

        if (_framePaths.Count == 0)
            return new FlyThroughRecordResult { FrameDirectory = _tempDir, FrameCount = 0 };

        if (!string.IsNullOrWhiteSpace(mp4OutputPath) && TryEncodeMp4(_framePaths, mp4OutputPath))
        {
            return new FlyThroughRecordResult
            {
                FrameDirectory = _tempDir,
                FrameCount = _framePaths.Count,
                Mp4Path = mp4OutputPath,
                UsedFfmpeg = true,
            };
        }

        return new FlyThroughRecordResult
        {
            FrameDirectory = _tempDir,
            FrameCount = _framePaths.Count,
            UsedFfmpeg = false,
        };
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_viewport.Tag is not CaveViewport3DPresenter.OrbitState st || _path.Count < 2)
        {
            _timer.Stop();
            return;
        }

        _phase += 1.0 / Math.Max(_targetFrames / (_path.Count - 1.0), 1);
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
        st.Azimuth += 0.015;
        CaveViewport3DPresenter.ApplyOrbitCameraPublic(st);

        if (_recording && _frameIndex < _targetFrames)
            CaptureFrame();

        if (_frameIndex >= _targetFrames)
            _timer.Stop();
    }

    private void CaptureFrame()
    {
        var w = (int)Math.Max(640, _viewport.ActualWidth);
        var h = (int)Math.Max(480, _viewport.ActualHeight);
        _renderRoot.Measure(new Size(w, h));
        _renderRoot.Arrange(new Rect(0, 0, w, h));
        _renderRoot.UpdateLayout();

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(_viewport);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        var path = Path.Combine(_tempDir, $"frame_{_frameIndex:D4}.png");
        using (var fs = File.Create(path))
            enc.Save(fs);
        _framePaths.Add(path);
        _frameIndex++;
    }

    private static bool TryEncodeMp4(IReadOnlyList<string> frames, string outputPath)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg == null || frames.Count == 0)
            return false;

        var listFile = Path.Combine(Path.GetDirectoryName(frames[0])!, "frames.txt");
        var sb = new System.Text.StringBuilder();
        foreach (var f in frames)
            sb.AppendLine($"file '{f.Replace("'", "'\\''")}'");
        File.WriteAllText(listFile, sb.ToString());

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -f concat -safe 0 -i \"{listFile}\" -c:v libx264 -pix_fmt yuv420p -r 24 \"{outputPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return false;
            proc.WaitForExit(120_000);
            return proc.ExitCode == 0 && File.Exists(outputPath);
        }
        catch
        {
            return false;
        }
    }

    private static string? FindFfmpeg()
    {
        foreach (var name in new[] { "ffmpeg", "ffmpeg.exe" })
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                var full = Path.Combine(dir.Trim(), name);
                if (File.Exists(full))
                    return full;
            }
        }

        return null;
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

    public sealed class FlyThroughRecordResult
    {
        public required string FrameDirectory { get; init; }

        public int FrameCount { get; init; }

        public string? Mp4Path { get; init; }

        public bool UsedFfmpeg { get; init; }
    }
}
