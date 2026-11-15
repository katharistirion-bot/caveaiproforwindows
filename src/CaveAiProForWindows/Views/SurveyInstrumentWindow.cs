using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Services;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Records one leg at a time from a paired DistoX (classic serial), CaveBLE, or BRIC.
/// </summary>
public sealed class SurveyInstrumentWindow : Window
{
    private static readonly Guid DistoSerial = Guid.Parse("00001101-0000-1000-8000-00805F9B34FB");
    private static readonly Guid CaveService = Guid.Parse("137c4435-8a64-4bcb-93f1-3792c6bdc965");
    private static readonly Guid CaveLeg = Guid.Parse("137c4435-8a64-4bcb-93f1-3792c6bdc968");
    private static readonly Guid CaveCommand = Guid.Parse("137c4435-8a64-4bcb-93f1-3792c6bdc967");
    private static readonly Guid BricService = Guid.Parse("000058d0-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BricPrimary = Guid.Parse("000058d1-0000-1000-8000-00805f9b34fb");

    private readonly Action<SurveyInstrumentReading> _onReading;
    private readonly TextBlock _status;
    private readonly ComboBox _kind;
    private CancellationTokenSource? _session;
    private StreamSocket? _socket;

    public SurveyInstrumentWindow(Action<SurveyInstrumentReading> onReading)
    {
        _onReading = onReading;
        Title = "Survey instrument";
        Width = 460;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Pair the instrument in Windows Bluetooth settings first. Each shot is added to the open survey. Distance, azimuth, and clino are the instrument's.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        _kind = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        _kind.Items.Add(new ComboBoxItem { Content = "DistoX", Tag = SurveyInstrumentKind.DistoX });
        _kind.Items.Add(new ComboBoxItem { Content = "CaveBLE", Tag = SurveyInstrumentKind.CaveBle });
        _kind.Items.Add(new ComboBoxItem { Content = "BRIC4 / BRIC5", Tag = SurveyInstrumentKind.Bric });
        _kind.SelectedIndex = 0;
        root.Children.Add(_kind);
        var connect = new Button { Content = "Connect", IsDefault = true, Margin = new Thickness(0, 0, 0, 10) };
        connect.Click += async (_, _) => await ConnectAsync();
        root.Children.Add(connect);
        _status = new TextBlock { Text = "Not connected.", TextWrapping = TextWrapping.Wrap };
        root.Children.Add(_status);
        Content = root;
        Closed += (_, _) => Stop();
    }

    private SurveyInstrumentKind SelectedKind =>
        (_kind.SelectedItem as ComboBoxItem)?.Tag is SurveyInstrumentKind kind
            ? kind
            : SurveyInstrumentKind.DistoX;

    private async Task ConnectAsync()
    {
        Stop();
        _session = new CancellationTokenSource();
        var token = _session.Token;
        try
        {
            _status.Text = "Choose the paired instrument…";
            var picker = new DevicePicker();
            var selector = SelectedKind == SurveyInstrumentKind.DistoX
                ? RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(DistoSerial))
                : GattDeviceService.GetDeviceSelectorFromUuid(SelectedKind == SurveyInstrumentKind.CaveBle ? CaveService : BricService);
            picker.Filter.SupportedDeviceSelectors.Add(selector);
            var picked = await picker.PickSingleDeviceAsync(new Windows.Foundation.Rect(Left, Top, Width, Height));
            if (picked == null || token.IsCancellationRequested)
            {
                _status.Text = "Not connected.";
                return;
            }

            if (SelectedKind == SurveyInstrumentKind.DistoX)
                await ReadDistoAsync(picked, token);
            else
                await ReadBleAsync(picked, token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Disconnected.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private async Task ReadDistoAsync(DeviceInformation picked, CancellationToken token)
    {
        var service = await RfcommDeviceService.FromIdAsync(picked.Id);
        if (service == null)
            throw new InvalidOperationException("Windows did not open the DistoX serial port. Pair the instrument and try again.");
        _socket = new StreamSocket();
        await _socket.ConnectAsync(
            service.ConnectionHostName,
            service.ConnectionServiceName,
            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
        _status.Text = "DistoX connected. Take a shot.";
        var reader = new DataReader(_socket.InputStream) { ByteOrder = ByteOrder.LittleEndian };
        var writer = new DataWriter(_socket.OutputStream);
        var pending = new List<byte>(8);
        while (!token.IsCancellationRequested)
        {
            var loaded = await reader.LoadAsync(8);
            if (loaded == 0)
                break;
            var chunk = new byte[loaded];
            reader.ReadBytes(chunk);
            pending.AddRange(chunk);
            while (pending.Count >= 8)
            {
                var frame = pending.Take(8).ToArray();
                pending.RemoveRange(0, 8);
                if (!SurveyInstrumentPackets.TryParseDisto(frame, out var reading))
                    continue;
                writer.WriteByte(reading.Ack);
                await writer.StoreAsync();
                Deliver(reading);
            }
        }
    }

    private async Task ReadBleAsync(DeviceInformation picked, CancellationToken token)
    {
        var service = await GattDeviceService.FromIdAsync(picked.Id);
        if (service == null)
            throw new InvalidOperationException("Windows did not open the instrument. Pair it and try again.");
        var legUuid = SelectedKind == SurveyInstrumentKind.CaveBle ? CaveLeg : BricPrimary;
        var commandUuid = CaveCommand;
        var chars = await service.GetCharacteristicsAsync();
        var leg = chars.Characteristics.FirstOrDefault(c => c.Uuid == legUuid);
        if (leg == null)
            throw new InvalidOperationException("The instrument did not publish a measurement characteristic.");
        var mode = SelectedKind == SurveyInstrumentKind.Bric
            ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
            : GattClientCharacteristicConfigurationDescriptorValue.Notify;
        var status = await leg.WriteClientCharacteristicConfigurationDescriptorAsync(mode);
        if (status != GattCommunicationStatus.Success)
            throw new InvalidOperationException("Could not subscribe to instrument shots.");
        _status.Text = (SelectedKind == SurveyInstrumentKind.Bric ? "BRIC" : "CaveBLE") + " connected. Take a shot.";
        var command = chars.Characteristics.FirstOrDefault(c => c.Uuid == commandUuid);
        leg.ValueChanged += (sender, args) =>
        {
            if (token.IsCancellationRequested)
                return;
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);
            if (!SurveyInstrumentPackets.TryParse(SelectedKind, data, out var reading))
                return;
            if (command != null && reading.Ack != 0)
            {
                var writer = new DataWriter();
                writer.WriteByte(reading.Ack);
                var ackWrite = command.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse);
                _ = ackWrite.AsTask();
            }
            Deliver(reading);
        };
        await Task.Delay(Timeout.Infinite, token);
    }

    private void Deliver(SurveyInstrumentReading reading)
    {
        Dispatcher.Invoke(() =>
        {
            _onReading(reading);
            _status.Text = $"Saved {reading.DistanceM:0.000} m at {reading.AzimuthDeg:0.0}°. Take the next shot.";
        });
    }

    private void Stop()
    {
        try
        {
            _session?.Cancel();
        }
        catch (Exception)
        {
            /* already stopped */
        }
        _session?.Dispose();
        _session = null;
        try
        {
            _socket?.Dispose();
        }
        catch (Exception)
        {
            /* socket already closed */
        }
        _socket = null;
    }
}
