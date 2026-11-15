using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CaveAiProForWindows.Services;
using Windows.Devices.Geolocation;

namespace CaveAiProForWindows.Views;

/// <summary>Live GPS distance and bearing back to the entrance, with an alarm.</summary>
public sealed class FieldReturnWindow : Window
{
    private readonly double _entranceLat;
    private readonly double _entranceLon;
    private readonly TextBlock _status;
    private readonly TextBlock _guidance;
    private readonly object _gpsGate = new();
    private int _gpsGeneration;
    private Geolocator? _gps;
    private Windows.Foundation.TypedEventHandler<Geolocator, PositionChangedEventArgs>? _onFix;
    private CancellationTokenSource? _alarm;
    private Button? _alarmButton;
    private DateTimeOffset _lastFixUtc = DateTimeOffset.MinValue;
    private readonly DispatcherTimer _watch;

    public FieldReturnWindow(double entranceLat, double entranceLon)
    {
        _entranceLat = entranceLat;
        _entranceLon = entranceLon;
        Title = "Return to entrance";
        Width = 440;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Windows GPS. The alarm is local. The phone SOS is still the one to carry underground.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        _guidance = new TextBlock { Text = "Waiting for a fix…", FontSize = 18, TextWrapping = TextWrapping.Wrap };
        root.Children.Add(_guidance);
        _status = new TextBlock { Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };
        root.Children.Add(_status);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var alarm = new Button { Content = "Start alarm", Width = 140, Margin = new Thickness(0, 0, 8, 0) };
        _alarmButton = alarm;
        alarm.Click += (_, _) => ToggleAlarm(alarm);
        row.Children.Add(alarm);
        root.Children.Add(row);
        Content = root;
        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _watch.Tick += (_, _) => RefreshLost();
        Loaded += async (_, _) => await StartGpsAsync();
        Closed += (_, _) => Stop();
    }

    private async Task StartGpsAsync()
    {
        var generation = _gpsGeneration;
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (generation != _gpsGeneration)
                return;
            if (access != GeolocationAccessStatus.Allowed)
            {
                _status.Text = "Location access denied. Enable Windows location for this app.";
                return;
            }

            Geolocator gps;
            lock (_gpsGate)
            {
                if (generation != _gpsGeneration)
                    return;
                gps = new Geolocator
                {
                    DesiredAccuracy = PositionAccuracy.High,
                    ReportInterval = 2000,
                };
                _gps = gps;
                _onFix = (_, args) =>
                {
                    var coord = args.Position.Coordinate;
                    _lastFixUtc = coord.Timestamp;
                    Dispatcher.Invoke(() => ShowFix(coord.Latitude, coord.Longitude, coord.Accuracy));
                };
                gps.PositionChanged += _onFix;
            }

            var first = await gps.GetGeopositionAsync();
            if (generation != _gpsGeneration)
                return;
            _lastFixUtc = first.Coordinate.Timestamp;
            ShowFix(first.Coordinate.Latitude, first.Coordinate.Longitude, first.Coordinate.Accuracy);
            _watch.Start();
        }
        catch (Exception ex)
        {
            if (generation == _gpsGeneration)
                _status.Text = ex.Message;
        }
    }

    private void ShowFix(double lat, double lon, double accuracy)
    {
        var distance = FieldReturnGuide.DistanceMeters(lat, lon, _entranceLat, _entranceLon);
        var bearing = FieldReturnGuide.BearingDegrees(lat, lon, _entranceLat, _entranceLon);
        _guidance.Text = $"Entrance {distance:0} m, bearing {bearing:0}°.";
        _status.Text = $"Fix ±{accuracy:0} m. GPS is live.";
    }

    private void RefreshLost()
    {
        if (_lastFixUtc == DateTimeOffset.MinValue)
            return;
        if (FieldReturnGuide.GpsLost(_lastFixUtc, DateTimeOffset.UtcNow))
            _status.Text = "GPS lost. Last fix is older than 25 seconds. The entrance bearing stays on the last fix.";
    }

    private void ToggleAlarm(Button button)
    {
        if (_alarm != null)
        {
            _alarm.Cancel();
            _alarm = null;
            button.Content = "Start alarm";
            return;
        }

        _alarm = new CancellationTokenSource();
        var token = _alarm.Token;
        button.Content = "Stop alarm";
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Console.Beep(880, 280);
                }
                catch (Exception)
                {
                    try
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _status.Text = "The alarm could not sound on this PC.";
                            if (_alarmButton != null)
                                _alarmButton.Content = "Start alarm";
                            _alarm?.Cancel();
                            _alarm = null;
                        });
                    }
                    catch (Exception)
                    {
                        // The window is already closed.
                    }

                    break;
                }

                if (token.WaitHandle.WaitOne(180))
                    break;
            }
        }, token);
    }

    private void Stop()
    {
        _gpsGeneration++;
        _watch.Stop();
        _alarm?.Cancel();
        _alarm = null;
        lock (_gpsGate)
        {
            if (_gps != null && _onFix != null)
                _gps.PositionChanged -= _onFix;
            _onFix = null;
            _gps = null;
        }
    }
}
