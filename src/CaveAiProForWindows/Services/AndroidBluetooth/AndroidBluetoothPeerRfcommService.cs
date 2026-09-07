using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.AndroidBluetooth;

/// <summary>
/// Optional Bluetooth Classic RFCOMM companion for Android Share survey data.
/// Idle until the user opens Tools → Android Bluetooth. Does not start on launch
/// and does not touch ZIP folder sync, Survey Cloud, or instrument BLE.
/// </summary>
public sealed class AndroidBluetoothPeerRfcommService : IDisposable
{
    private const uint SdpServiceNameAttributeId = 0x100;
    private const byte SdpServiceNameAttributeType = (4 << 3) | 5;

    private readonly object _gate = new();
    private readonly AndroidBluetoothPeerSessionAssembler _assembler = new();
    private CancellationTokenSource? _cts;
    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;
    private StreamSocket? _socket;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Task? _readTask;
    private bool _disposed;
    private bool _waitingForAck;
    private TaskCompletionSource<bool>? _ackWaiter;

    public event Action<string>? StatusChanged;
    public event Action<bool>? ListeningChanged;
    public event Action<string?>? ConnectionChanged;
    public event Action<AndroidBluetoothPeerSafetyMessage>? SafetyReceived;
    public event Action<AndroidBluetoothPeerDecodedSnapshot>? SessionReceived;
    public event Action<string>? ErrorOccurred;

    public bool IsListening { get; private set; }
    public bool IsConnected { get; private set; }
    public string? RemoteName { get; private set; }

    public async Task StartListeningAsync()
    {
        ThrowIfDisposed();
        await StopAsync().ConfigureAwait(false);

        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null)
                throw new InvalidOperationException("No Bluetooth adapter. Pair the phone in Windows Settings after enabling Bluetooth.");

            var serviceId = RfcommServiceId.FromUuid(AndroidBluetoothPeerProtocol.RfcommServiceUuid);
            _provider = await RfcommServiceProvider.CreateAsync(serviceId);
            WriteServiceNameSdpAttribute(_provider);

            _listener = new StreamSocketListener();
            _listener.ConnectionReceived += OnConnectionReceived;
            await _listener.BindServiceNameAsync(
                _provider.ServiceId.AsString(),
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            _provider.StartAdvertising(_listener);

            IsListening = true;
            RaiseListening(true);
            RaiseStatus("Listening for CaveAI Pro (Android)… Pair the phone in Windows Settings, then Connect from Share survey data.");
        }
        catch (Exception ex)
        {
            await StopAsync().ConfigureAwait(false);
            var msg = FormatBluetoothError(ex);
            RaiseError(msg);
            RaiseStatus(msg);
            throw new InvalidOperationException(msg, ex);
        }
    }

    public async Task<IReadOnlyList<AndroidBluetoothPairedDevice>> ListPairedDevicesAsync()
    {
        ThrowIfDisposed();
        try
        {
            var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var found = await DeviceInformation.FindAllAsync(selector);
            var list = new List<AndroidBluetoothPairedDevice>(found.Count);
            foreach (var info in found)
            {
                var name = string.IsNullOrWhiteSpace(info.Name) ? info.Id : info.Name;
                list.Add(new AndroidBluetoothPairedDevice { Id = info.Id, Name = name });
            }

            return list
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            var msg = FormatBluetoothError(ex);
            RaiseError(msg);
            return Array.Empty<AndroidBluetoothPairedDevice>();
        }
    }

    public async Task ConnectAsync(string deviceId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await StopAsync().ConfigureAwait(false);

        Exception? last = null;
        for (var attempt = 0; attempt < AndroidBluetoothPeerProtocol.ConnectMaxAttempts; attempt++)
        {
            BluetoothDevice? device = null;
            RfcommDeviceService? service = null;
            StreamSocket? socket = null;
            try
            {
                device = await BluetoothDevice.FromIdAsync(deviceId);
                if (device is null)
                    throw new InvalidOperationException("Bluetooth device not found. Pair the phone in Windows Settings first.");

                var services = await device.GetRfcommServicesForIdAsync(
                    RfcommServiceId.FromUuid(AndroidBluetoothPeerProtocol.RfcommServiceUuid),
                    BluetoothCacheMode.Uncached);
                if (services.Services.Count == 0)
                    throw new InvalidOperationException(
                        "CaveAI Share survey data is not listening on that phone. Open Share survey data → START LISTENING, then try again.");

                service = services.Services[0];
                socket = new StreamSocket();
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName);
                AttachSocket(socket, device.Name);
                socket = null;
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                if (socket != null)
                    socket.Dispose();
                service?.Dispose();
                device?.Dispose();
                if (attempt < AndroidBluetoothPeerProtocol.ConnectMaxAttempts - 1)
                    await Task.Delay(AndroidBluetoothPeerProtocol.ConnectRetryDelayMs).ConfigureAwait(false);
            }
        }

        var msg = FormatBluetoothError(last);
        RaiseStatus("Connection failed");
        RaiseError(msg);
        throw new InvalidOperationException(msg, last);
    }

    public async Task SendSafetyAsync(string safety, string station, double depth, string statusText)
    {
        var line = AndroidBluetoothPeerProtocol.BuildSafetyLine(
            safety,
            station,
            depth,
            statusText,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (!await SendLineWithAckAsync(line).ConfigureAwait(false))
            throw new IOException("Safety update failed (no ACK from Android).");
    }

    public async Task SendSessionAsync(CaveProjectDocument project, string safety, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var snapshot = AndroidBluetoothPeerProjectCodec.BuildSnapshotJson(project, safety);
        var lines = AndroidBluetoothPeerProtocol.BuildSessionChunkLines(snapshot, safety);
        for (var i = 0; i < lines.Count; i++)
        {
            progress?.Report($"Sending session {i + 1}/{lines.Count}…");
            if (!await SendLineWithAckAsync(lines[i]).ConfigureAwait(false))
                throw new IOException($"Session send failed (no ACK on chunk {i + 1}/{lines.Count}).");
            if (i < lines.Count - 1)
                await Task.Delay(AndroidBluetoothPeerProtocol.InterChunkDelayMs).ConfigureAwait(false);
        }
    }

    public async Task StopAsync()
    {
        IsListening = false;
        RaiseListening(false);
        CloseSocket(disconnectedStatus: false);

        var provider = _provider;
        _provider = null;
        var listener = _listener;
        _listener = null;
        if (listener != null)
            listener.ConnectionReceived -= OnConnectionReceived;

        try { provider?.StopAdvertising(); } catch { /* ignore */ }
        try { listener?.Dispose(); } catch { /* ignore */ }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            /* ignore */
        }
    }

    private void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        try
        {
            AttachSocket(args.Socket, "Android");
        }
        catch (Exception ex)
        {
            RaiseError(FormatBluetoothError(ex));
        }
    }

    private void AttachSocket(StreamSocket socket, string? fallbackName)
    {
        lock (_gate)
        {
            CloseSocketLocked(disconnectedStatus: false);
            _cts = new CancellationTokenSource();
            _socket = socket;
            var read = socket.InputStream.AsStreamForRead();
            var write = socket.OutputStream.AsStreamForWrite();
            _reader = new StreamReader(read, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            _writer = new StreamWriter(write, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n",
            };
            RemoteName = string.IsNullOrWhiteSpace(fallbackName)
                ? (socket.Information.RemoteHostName?.DisplayName ?? "Android")
                : fallbackName;
            IsConnected = true;
            IsListening = false;
            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
        }

        RaiseListening(false);
        RaiseConnection(RemoteName);
        RaiseStatus("Connected to " + RemoteName);
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        StreamReader? reader;
        lock (_gate) reader = _reader;
        if (reader is null)
            return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                    break;
                if (AndroidBluetoothPeerProtocol.IsAckLine(line))
                {
                    CompleteAck(true);
                    continue;
                }

                if (!AndroidBluetoothPeerProtocol.TryParseJsonLine(line, out var obj) || obj is null)
                    continue;

                try
                {
                    HandleIncoming(obj);
                    await SendAckAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RaiseError("Peer line rejected (no ACK): " + ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* expected on stop */
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                RaiseError("Bluetooth link closed: " + ex.Message);
        }
        finally
        {
            CloseSocket(disconnectedStatus: true);
        }
    }

    private void HandleIncoming(JsonObject obj)
    {
        switch (AndroidBluetoothPeerProtocol.GetType(obj))
        {
            case AndroidBluetoothPeerProtocol.TypeSafety:
                SafetyReceived?.Invoke(AndroidBluetoothPeerSafetyMessage.FromJson(obj));
                break;
            case AndroidBluetoothPeerProtocol.TypeSessionChunk:
                if (_assembler.TryAddChunk(obj, out var snapshot, out var error))
                {
                    if (snapshot != null)
                        SessionReceived?.Invoke(snapshot.Value);
                }
                else if (!string.IsNullOrWhiteSpace(error))
                {
                    throw new InvalidDataException(error);
                }

                break;
        }
    }

    private async Task<bool> SendLineWithAckAsync(string payloadLine)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        var payload = payloadLine + "\n";
        for (var attempt = 0; attempt < AndroidBluetoothPeerProtocol.AckMaxRetransmissions; attempt++)
        {
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                if (_writer is null)
                    return false;
                _waitingForAck = true;
                _ackWaiter = waiter;
                _writer.Write(payload);
            }

            var completed = await Task.WhenAny(waiter.Task, Task.Delay(AndroidBluetoothPeerProtocol.AckTimeoutMs))
                .ConfigureAwait(false);
            lock (_gate)
            {
                _waitingForAck = false;
                _ackWaiter = null;
            }

            if (completed == waiter.Task && waiter.Task.Result)
                return true;
        }

        return false;
    }

    private Task SendAckAsync()
    {
        lock (_gate)
        {
            _writer?.Write(AndroidBluetoothPeerProtocol.AckToken + "\n");
        }

        return Task.CompletedTask;
    }

    private void CompleteAck(bool ok)
    {
        lock (_gate)
        {
            if (_waitingForAck)
                _ackWaiter?.TrySetResult(ok);
        }
    }

    private void CloseSocket(bool disconnectedStatus)
    {
        lock (_gate)
            CloseSocketLocked(disconnectedStatus);
        if (disconnectedStatus && !IsConnected)
        {
            RaiseConnection(null);
            RaiseStatus("Disconnected");
        }
    }

    private void CloseSocketLocked(bool disconnectedStatus)
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts = null;
        CompleteAck(false);
        try { _writer?.Dispose(); } catch { /* ignore */ }
        try { _reader?.Dispose(); } catch { /* ignore */ }
        try { _socket?.Dispose(); } catch { /* ignore */ }
        _writer = null;
        _reader = null;
        _socket = null;
        _readTask = null;
        var wasConnected = IsConnected;
        IsConnected = false;
        RemoteName = null;
        if (!disconnectedStatus && wasConnected)
        {
            /* caller raises status */
        }
    }

    private void RaiseStatus(string status) => StatusChanged?.Invoke(status);
    private void RaiseListening(bool listening) => ListeningChanged?.Invoke(listening);
    private void RaiseConnection(string? name) => ConnectionChanged?.Invoke(name);
    private void RaiseError(string message) => ErrorOccurred?.Invoke(message);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void WriteServiceNameSdpAttribute(RfcommServiceProvider provider)
    {
        var writer = new DataWriter();
        var name = AndroidBluetoothPeerProtocol.ServiceName;
        writer.WriteByte(SdpServiceNameAttributeType);
        writer.WriteByte((byte)name.Length);
        writer.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
        writer.WriteString(name);
        provider.SdpRawAttributes[SdpServiceNameAttributeId] = writer.DetachBuffer();
    }

    private static string FormatBluetoothError(Exception? ex)
    {
        var raw = ex?.Message ?? "Unknown Bluetooth error.";
        if (raw.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("access", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("E_ACCESSDENIED", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows blocked Bluetooth. Pair the phone in Settings → Bluetooth, then try again.";
        }

        return raw;
    }
}
