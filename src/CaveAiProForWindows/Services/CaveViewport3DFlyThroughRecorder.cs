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
    private readonly FrameworkElement _captureRoot;
    private readonly UIElement? _labelChrome;
    private readonly DispatcherTimer _timer;
    private readonly List<string> _framePaths = new();
    private readonly string _tempDir;
    private readonly IReadOnlyList<CaveViewport3DFlyThroughPathBuilder.PathSample> _path;
    private double _progress;
    private int _frameIndex;
    private int _targetFrames = 120;

    public CaveViewport3DFlyThroughRecorder(
        Viewport3D viewport,
        Border shell,
        CaveProjectDocument project,
        int targetFrames = 120,
        Canvas? labelCanvas = null)
    {
        _viewport = viewport;
        _captureRoot = shell;
        _labelChrome = labelCanvas;
        _targetFrames = Math.Clamp(targetFrames, 24, 600);
        _path = CaveViewport3DFlyThroughPathBuilder.BuildPath(project);
        _tempDir = Path.Combine(Path.GetTempPath(), "caveai-flythrough-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.IsEnabled;

    public int PathSampleCount => _path.Count;

    public event Action? RecordingCompleted;

    public void Start() => StartRecording();

    public void StartRecording()
    {
        if (_path.Count < 2)
            return;

        if (_labelChrome != null)
            _labelChrome.Visibility = Visibility.Collapsed;

        _progress = 0;
        _frameIndex = 0;
        _framePaths.Clear();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        if (_viewport.Tag is CaveViewport3DPresenter.OrbitState st)
            CaveViewport3DPresenter.EndFlyThroughCamera(st);
        if (_labelChrome != null)
            _labelChrome.Visibility = Visibility.Visible;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        Stop();
    }

    public async Task<FlyThroughRecordResult> FinishAsync(string? mp4OutputPath = null)
    {
        _timer.Stop();
        await _captureRoot.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render).Task.ConfigureAwait(true);
        await Task.Delay(30).ConfigureAwait(true);

        if (_labelChrome != null)
            _labelChrome.Visibility = Visibility.Visible;

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
            RecordingCompleted?.Invoke();
            return;
        }

        _progress = _frameIndex / (double)Math.Max(_targetFrames - 1, 1);
        if (!CaveViewport3DFlyThroughPathBuilder.TrySampleTunnelCamera(
                _path, _progress, lookAheadNormalized: 0.045, out var eye, out var lookDirection))
        {
            _timer.Stop();
            RecordingCompleted?.Invoke();
            return;
        }

        CaveViewport3DPresenter.ApplyTunnelCameraPublic(st, eye, lookDirection, flyThrough: true);

        if (_frameIndex < _targetFrames)
            CaptureFrame();

        if (_frameIndex >= _targetFrames)
        {
            _timer.Stop();
            RecordingCompleted?.Invoke();
        }
    }

    private void CaptureFrame()
    {
        var w = (int)Math.Max(960, _captureRoot.ActualWidth > 8 ? _captureRoot.ActualWidth : 960);
        var h = (int)Math.Max(640, _captureRoot.ActualHeight > 8 ? _captureRoot.ActualHeight : 640);

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(_captureRoot);
        rtb.Freeze();

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

    public sealed class FlyThroughRecordResult
    {
        public required string FrameDirectory { get; init; }

        public int FrameCount { get; init; }

        public string? Mp4Path { get; init; }

        public bool UsedFfmpeg { get; init; }
    }
}
