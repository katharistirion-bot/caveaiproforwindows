using System.IO;
using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace CaveAiProForWindows.Views;

/// <summary>Import a scan file or read a live XYZ stream, then put the wall outline on the plan.</summary>
public sealed class LidarScannerWindow : Window
{
    private static readonly Guid CaveAiLidar = Guid.Parse("a1b2c3d4-e5f6-47a1-9c01-0e1f2a3b4c5d");
    private static readonly Guid SerialPort = Guid.Parse("00001101-0000-1000-8000-00805F9B34FB");

    private readonly CaveProjectDocument _project;
    private readonly Action _onChanged;
    private readonly TextBlock _status;
    private readonly TextBox _station;
    private readonly TextBox _heading;
    private readonly List<(float X, float Y, float Z)> _points = [];
    private CancellationTokenSource? _session;
    private StreamSocket? _socket;

    public LidarScannerWindow(CaveProjectDocument project, Action onChanged)
    {
        _project = project;
        _onChanged = onChanged;
        Title = "LiDAR scanner";
        Width = 480;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "OBJ, ASCII XYZ, or uncompressed LAS. A live scanner uses the CaveAI frame or x,y,z lines. Degree files belong on Aerial DEM, not on this plan.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        _station = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Station name. Leave blank for the entrance." };
        root.Children.Add(new TextBlock { Text = "Place at station" });
        root.Children.Add(_station);
        _heading = new TextBox { Text = "0", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(new TextBlock { Text = "Scan heading, clockwise degrees" });
        root.Children.Add(_heading);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        var import = new Button { Content = "Import file", Margin = new Thickness(0, 0, 8, 0) };
        import.Click += (_, _) => ImportFile();
        var live = new Button { Content = "Connect scanner", Margin = new Thickness(0, 0, 8, 0) };
        live.Click += async (_, _) => await ConnectAsync();
        var put = new Button { Content = "Put outline on plan" };
        put.Click += (_, _) => PutOnPlan();
        row.Children.Add(import);
        row.Children.Add(live);
        row.Children.Add(put);
        root.Children.Add(row);
        _status = new TextBlock { Text = "No scan yet.", TextWrapping = TextWrapping.Wrap };
        root.Children.Add(_status);
        Content = root;
        Closed += (_, _) => Stop();
    }

    private void ImportFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "LiDAR scan",
            Filter = "Scan|*.obj;*.xyz;*.txt;*.las;*.laz|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true)
            return;
        try
        {
            var ext = Path.GetExtension(dlg.FileName);
            List<(float X, float Y, float Z)> points;
            if (ext.Equals(".laz", StringComparison.OrdinalIgnoreCase))
            {
                _status.Text = "Export an uncompressed LAS, or use OBJ.";
                return;
            }

            if (ext.Equals(".las", StringComparison.OrdinalIgnoreCase))
                points = LidarPlanOutline.ReadLas(dlg.FileName);
            else
            {
                var text = File.ReadAllText(dlg.FileName);
                points = ext.Equals(".obj", StringComparison.OrdinalIgnoreCase)
                    ? LidarPlanOutline.ParseObjVertices(text)
                    : LidarPlanOutline.ParseAsciiCloud(text);
            }

            _points.Clear();
            _points.AddRange(points);
            _status.Text = $"{_points.Count} points read.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private async Task ConnectAsync()
    {
        Stop();
        _session = new CancellationTokenSource();
        var token = _session.Token;
        try
        {
            var picker = new DevicePicker();
            picker.Filter.SupportedDeviceSelectors.Add(RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(CaveAiLidar)));
            picker.Filter.SupportedDeviceSelectors.Add(RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(SerialPort)));
            var picked = await picker.PickSingleDeviceAsync(new Windows.Foundation.Rect(Left, Top, Width, Height));
            if (picked == null || token.IsCancellationRequested)
                return;
            var service = await RfcommDeviceService.FromIdAsync(picked.Id);
            if (service == null)
                throw new InvalidOperationException("Windows did not open the scanner. Pair it and try again.");
            _socket = new StreamSocket();
            await _socket.ConnectAsync(
                service.ConnectionHostName,
                service.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            _status.Text = "Scanner connected.";
            var reader = new DataReader(_socket.InputStream);
            var pending = new List<byte>();
            var text = "";
            while (!token.IsCancellationRequested)
            {
                var loaded = await reader.LoadAsync(256);
                if (loaded == 0)
                    break;
                var chunk = new byte[loaded];
                reader.ReadBytes(chunk);
                pending.AddRange(chunk);
                Consume(pending, ref text);
                Dispatcher.Invoke(() => _status.Text = $"Scanner connected. {_points.Count} points.");
            }
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Scanner disconnected.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void Consume(List<byte> pending, ref string text)
    {
        while (pending.Count >= 7 &&
               pending[0] == 0xCA && pending[1] == 0x1D && pending[2] == 0x01 && pending[3] == 0x02)
        {
            var count = pending[5] | (pending[6] << 8);
            var size = 7 + count * 12;
            if (count > 8192)
            {
                pending.RemoveAt(0);
                continue;
            }

            if (pending.Count < size)
                return;
            var frame = pending.Take(size).ToArray();
            pending.RemoveRange(0, size);
            _points.AddRange(LidarPlanOutline.ParseBinaryFrame(frame));
        }

        if (pending.Count > 0 && pending[0] == 0xCA && pending.Count < 7)
            return;

        text += System.Text.Encoding.UTF8.GetString(pending.ToArray());
        pending.Clear();
        var lines = text.Split('\n');
        text = lines[^1];
        for (var i = 0; i < lines.Length - 1; i++)
            _points.AddRange(LidarPlanOutline.ParseAsciiCloud(lines[i]));
    }

    private void PutOnPlan()
    {
        try
        {
            if (!int.TryParse(_heading.Text.Trim(), out var heading))
                heading = 0;
            var anchor = LidarPlanOutline.Anchor(_project, _station.Text);
            var outline = LidarPlanOutline.OutlineFromYUp(_points);
            if (outline.Count < 2)
            {
                _status.Text = "No wall outline. A degree-coordinate file belongs on Aerial DEM.";
                return;
            }

            var placed = LidarPlanOutline.RotateAndPlace(outline, anchor.East, anchor.North, heading);
            var lines = LidarPlanOutline.ReplacePlanLines(_project, placed);
            _onChanged();
            _status.Text = lines == 1
                ? "Outline is on the plan. Save the project so the phone shows the same lines."
                : "The outline was cleared.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void Stop()
    {
        try { _session?.Cancel(); } catch (Exception) { /* already stopped */ }
        _session?.Dispose();
        _session = null;
        try { _socket?.Dispose(); } catch (Exception) { /* already closed */ }
        _socket = null;
    }
}
