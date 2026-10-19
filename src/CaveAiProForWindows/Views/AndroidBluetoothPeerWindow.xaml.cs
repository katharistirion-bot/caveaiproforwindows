using System.Globalization;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.AndroidBluetooth;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Optional Android RFCOMM companion. Closing this window stops advertising/connecting;
/// ZIP Desktop Sync and Survey Cloud are unchanged.
/// </summary>
public partial class AndroidBluetoothPeerWindow : Window
{
    private readonly MainViewModel _main;
    private readonly AndroidBluetoothPeerRfcommService _bt = new();
    private CaveProjectDocument? _pendingProject;
    private string _pendingSafety = "NOMINAL";
    private bool _busy;

    public AndroidBluetoothPeerWindow(MainViewModel main)
    {
        _main = main;
        InitializeComponent();
        _bt.StatusChanged += s => Dispatcher.Invoke(() => StatusText.Text = s);
        _bt.ListeningChanged += listening => Dispatcher.Invoke(() =>
        {
            ListenButton.Content = listening ? "Listening…" : "Listen for Android";
        });
        _bt.ConnectionChanged += name => Dispatcher.Invoke(() => ApplyConnectionUi(name));
        _bt.SafetyReceived += msg => Dispatcher.Invoke(() => AppendSafety(msg));
        _bt.SessionReceived += snap => Dispatcher.Invoke(() => OnSessionReceived(snap));
        _bt.ErrorOccurred += msg => Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(msg))
                StatusText.Text = msg;
        });
    }

    private async void Listen_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await _bt.StartListeningAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Android Bluetooth", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void RefreshPaired_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var devices = await _bt.ListPairedDevicesAsync();
            PairedList.ItemsSource = devices;
            if (devices.Count == 0)
            {
                StatusText.Text = "No paired Bluetooth devices. Pair the phone in Windows Settings → Bluetooth first.";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (PairedList.SelectedItem is not AndroidBluetoothPairedDevice device)
        {
            MessageBox.Show(this, "Select a paired phone first.", "Android Bluetooth", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _busy = true;
        try
        {
            await _bt.ConnectAsync(device.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Android Bluetooth", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        await _bt.StopAsync();
        ApplyConnectionUi(null);
        StatusText.Text = "Disconnected";
    }

    private async void SendSafety_Click(object sender, RoutedEventArgs e)
    {
        if (!_bt.IsConnected || _busy) return;
        var project = _main.SelectedProject;
        var last = project?.Shots.LastOrDefault();
        var station = last?.ToStation ?? "";
        var depth = last?.Depth ?? project?.Alt ?? 0;
        _busy = true;
        try
        {
            await _bt.SendSafetyAsync("NOMINAL", station, depth, _main.StatusMessage ?? "");
            StatusText.Text = "Safety update sent";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Android Bluetooth", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void SendSession_Click(object sender, RoutedEventArgs e)
    {
        if (!_bt.IsConnected || _busy) return;
        var project = _main.SelectedProject;
        if (project is null)
        {
            MessageBox.Show(this, "Open a survey project first (File → Open, or receive from Android).", "Android Bluetooth", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _busy = true;
        SendSessionButton.IsEnabled = false;
        try
        {
            await _bt.SendSessionAsync(project, "NOMINAL", new Progress<string>(s => StatusText.Text = s));
            StatusText.Text = "Session sent";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Android Bluetooth", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SendSessionButton.IsEnabled = true;
            _busy = false;
        }
    }

    private void OpenIncoming_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingProject is null) return;
        if (!_main.TryOpenBluetoothPeerProject(_pendingProject))
            return;
        ClearPending();
        StatusText.Text = "Opened Android Bluetooth session — File → Save as… to keep a ZIP.";
    }

    private void MergeShotsOnly_Click(object sender, RoutedEventArgs e) =>
        MergeIncoming(AndroidBluetoothPeerMergeMode.ShotsOnly);

    private void MergeShotsAndBrackets_Click(object sender, RoutedEventArgs e) =>
        MergeIncoming(AndroidBluetoothPeerMergeMode.ShotsAndBrackets);

    private void MergeAllNoMedia_Click(object sender, RoutedEventArgs e) =>
        MergeIncoming(AndroidBluetoothPeerMergeMode.AllNoMedia);

    private void MergeIncoming(AndroidBluetoothPeerMergeMode mode)
    {
        if (_pendingProject is null) return;
        var result = _main.MergeBluetoothPeerIntoSelected(_pendingProject, mode);
        ClearPending();
        var total = result.ShotsAdded + result.BracketsAdded + result.DepthSpansAdded + result.RocksAdded;
        StatusText.Text = total == 0
            ? "Nothing new to merge (ids already present)."
            : mode switch
            {
                AndroidBluetoothPeerMergeMode.ShotsAndBrackets =>
                    $"Merged {result.ShotsAdded} shot(s) and {result.BracketsAdded + result.DepthSpansAdded} annotation(s). Existing legs were not changed.",
                AndroidBluetoothPeerMergeMode.AllNoMedia =>
                    $"Merged {result.ShotsAdded} shot(s), {result.BracketsAdded + result.DepthSpansAdded} annotation(s), {result.RocksAdded} rock(s). Media URIs were not kept.",
                _ =>
                    $"Merged {result.ShotsAdded} new shot(s) from Android. Existing legs were not changed.",
            };
    }

    private void DismissIncoming_Click(object sender, RoutedEventArgs e) => ClearPending();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object sender, EventArgs e) => _bt.Dispose();

    private void OnSessionReceived(AndroidBluetoothPeerDecodedSnapshot snap)
    {
        try
        {
            _pendingProject = AndroidBluetoothPeerProjectCodec.ParseProjectJson(snap.ProjectJson);
            _pendingSafety = snap.Safety;
            PendingSummary.Text =
                $"From {snap.NodeId} · {_pendingProject.Name} · {_pendingProject.Shots.Count} shot(s) · safety {_pendingSafety}";
            PendingPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Android session received — review before opening or merging.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not decode Android session: " + ex.Message;
        }
    }

    private void AppendSafety(AndroidBluetoothPeerSafetyMessage msg)
    {
        var depth = msg.Depth is { } d ? d.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "-";
        SafetyLog.Items.Insert(0, $"{msg.NodeId} · {msg.Safety} · {msg.Station ?? "-"} · {depth}");
        while (SafetyLog.Items.Count > 20)
            SafetyLog.Items.RemoveAt(SafetyLog.Items.Count - 1);
    }

    private void ApplyConnectionUi(string? remoteName)
    {
        var connected = !string.IsNullOrEmpty(remoteName) || _bt.IsConnected;
        DisconnectedPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        ConnectedPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (!connected)
            ListenButton.Content = "Listen for Android";
    }

    private void ClearPending()
    {
        _pendingProject = null;
        _pendingSafety = "NOMINAL";
        PendingPanel.Visibility = Visibility.Collapsed;
    }
}
