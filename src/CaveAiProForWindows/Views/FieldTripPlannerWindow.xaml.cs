using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.FieldTrip;
using CaveAiProForWindows.Services.ReferenceCatalog;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace CaveAiProForWindows.Views;

public partial class FieldTripPlannerWindow : Window
{
    private FieldTripStoreFile _store = new();
    private FieldTripDocument? _selectedTrip;
    private static FieldTripPlannerWindow? _active;
    private bool _mapReady;
    private bool _webViewInitialized;
    private System.Windows.Threading.DispatcherTimer? _mapLoadTimer;

    public FieldTripPlannerWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            StartMapLoadWatchdog();
            await EnsureMapWebViewAsync().ConfigureAwait(true);
            ReloadStore();
        };
        Closed += (_, _) => _mapLoadTimer?.Stop();
    }

    public static void Show(Window? owner)
    {
        if (_active != null)
        {
            try
            {
                if (_active.IsLoaded)
                {
                    _active.Activate();
                    return;
                }
            }
            catch
            {
                _active = null;
            }
        }

        var win = new FieldTripPlannerWindow { Owner = owner };
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_active, win))
                _active = null;
        };
        _active = win;
        win.Show();
    }

    public static void ShowDialog(Window? owner)
    {
        new FieldTripPlannerWindow { Owner = owner }.ShowDialog();
    }

    public static void AddStopFromReference(Window? owner, ReferenceCaveIndexEntry entry)
    {
        var store = FieldTripStore.Load();
        var trip = store.Trips.FirstOrDefault() ?? new FieldTripDocument { Name = "Field trip" };
        if (!store.Trips.Contains(trip))
            store.Trips.Add(trip);
        trip.Stops.Add(FieldTripExportService.FromIndexEntry(entry));
        FieldTripStore.Upsert(trip);
        MessageBox.Show(
            owner,
            $"Added \"{entry.Name}\" to \"{trip.Name}\" ({trip.Stops.Count} stop(s)).",
            "Field trip",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task EnsureMapWebViewAsync()
    {
        if (_webViewInitialized)
            return;

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaveAiProForWindows",
                "WebView2",
                "FieldTripMap");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await StopsMapWebView.EnsureCoreWebView2Async(environment);

            var core = StopsMapWebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 core is unavailable.");

            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsWebMessageEnabled = true;
            core.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
                    if (doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "ready")
                    {
                        _mapReady = true;
                        Dispatcher.Invoke(() =>
                        {
                            HideMapLoadingOverlay();
                            PushStopsToMap();
                        });
                    }
                }
                catch
                {
                    /* ignore */
                }
            };

            var assetsFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "field-trip-map");
            if (!Directory.Exists(assetsFolder))
            {
                ShowMapError("Field trip map assets not found.");
                return;
            }

            core.SetVirtualHostNameToFolderMapping(
                FieldTripMapBridge.VirtualHost,
                assetsFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                    ShowMapError("Map failed to load — check WebView2 runtime and internet connection.");
            };

            core.Navigate(FieldTripMapBridge.EntryUri);
            _webViewInitialized = true;
        }
        catch (Exception ex)
        {
            ShowMapError("WebView2 unavailable — install Microsoft Edge WebView2 Runtime.\n" + ex.Message);
        }
    }

    private void StartMapLoadWatchdog()
    {
        _mapLoadTimer?.Stop();
        _mapLoadTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _mapLoadTimer.Tick += (_, _) =>
        {
            _mapLoadTimer.Stop();
            if (!_mapReady)
                ShowMapError("Map is taking longer than usual — check your connection or try again later.");
        };
        _mapLoadTimer.Start();
    }

    private void HideMapLoadingOverlay()
    {
        _mapLoadTimer?.Stop();
        StopsMapLoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowMapError(string message)
    {
        _mapLoadTimer?.Stop();
        StopsMapLoadingOverlay.Visibility = Visibility.Visible;
        StopsMapPlaceholder.Text = message;
    }

    private void ReloadStore()
    {
        _store = FieldTripStore.Load();
        if (_store.Trips.Count == 0)
            _store.Trips.Add(new FieldTripDocument { Name = "Field trip" });
        TripsList.ItemsSource = null;
        TripsList.ItemsSource = _store.Trips;
        TripsList.SelectedIndex = 0;
    }

    private void TripsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveCurrentTripFields();
        _selectedTrip = TripsList.SelectedItem as FieldTripDocument;
        if (_selectedTrip == null)
        {
            StopsList.ItemsSource = null;
            PushStopsToMap();
            return;
        }

        TripNameBox.Text = _selectedTrip.Name;
        TripNotesBox.Text = _selectedTrip.Notes ?? "";
        StopsList.ItemsSource = _selectedTrip.Stops;
        PushStopsToMap();
    }

    private void StopsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => PushStopsToMap();

    private void PushStopsToMap()
    {
        if (!_mapReady || StopsMapWebView?.CoreWebView2 == null)
            return;

        var stops = _selectedTrip?.Stops ?? [];
        if (stops.Count == 0 || stops.All(s => s.Lat == 0 && s.Lon == 0))
        {
            if (_mapReady)
            {
                StopsMapLoadingOverlay.Visibility = Visibility.Visible;
                StopsMapPlaceholder.Text = "Add stops with coordinates to see a preview.";
            }
        }
        else if (_mapReady)
        {
            HideMapLoadingOverlay();
        }

        StopsMapWebView.CoreWebView2.PostWebMessageAsJson(
            FieldTripMapBridge.BuildStopsMessageJson(stops));
    }

    private void SaveCurrentTripFields()
    {
        if (_selectedTrip == null)
            return;
        _selectedTrip.Name = string.IsNullOrWhiteSpace(TripNameBox.Text) ? "Field trip" : TripNameBox.Text.Trim();
        _selectedTrip.Notes = TripNotesBox.Text?.Trim();
        FieldTripStore.Save(_store);
    }

    private void NewTrip_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentTripFields();
        var trip = new FieldTripDocument { Name = $"Trip {_store.Trips.Count + 1}" };
        _store.Trips.Add(trip);
        FieldTripStore.Save(_store);
        ReloadStore();
        TripsList.SelectedItem = trip;
    }

    private void DeleteTrip_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTrip == null)
            return;
        if (_store.Trips.Count <= 1)
        {
            MessageBox.Show(this, "Keep at least one trip.", "Field trip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Delete trip \"{_selectedTrip.Name}\"?", "Field trip", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _store.Trips.Remove(_selectedTrip);
        FieldTripStore.Save(_store);
        ReloadStore();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveStop(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveStop(1);

    private void MoveStop(int delta)
    {
        if (_selectedTrip == null || StopsList.SelectedItem is not FieldTripStop stop)
            return;
        var idx = _selectedTrip.Stops.IndexOf(stop);
        var newIdx = idx + delta;
        if (idx < 0 || newIdx < 0 || newIdx >= _selectedTrip.Stops.Count)
            return;
        _selectedTrip.Stops.RemoveAt(idx);
        _selectedTrip.Stops.Insert(newIdx, stop);
        StopsList.Items.Refresh();
        StopsList.SelectedItem = stop;
        FieldTripStore.Upsert(_selectedTrip);
        PushStopsToMap();
    }

    private void RemoveStop_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTrip == null || StopsList.SelectedItem is not FieldTripStop stop)
            return;
        _selectedTrip.Stops.Remove(stop);
        StopsList.Items.Refresh();
        FieldTripStore.Upsert(_selectedTrip);
        PushStopsToMap();
    }

    private void AddCommunityStop_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTrip == null)
            return;

        var input = CommunityStopInput?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            MessageBox.Show(this, "Paste a community doc id or share URL first.", "Field trip",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stop = FieldTripStopImporter.TryParseInput(input);
        if (stop == null)
        {
            MessageBox.Show(this, "Could not parse community stop. Use a Firestore doc id, map?cave= URL, or field trip share link.",
                "Field trip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (stop.Lat == 0 && stop.Lon == 0)
        {
            MessageBox.Show(this,
                "Stop added without coordinates — set lat/lon from Reference catalog or import a share URL that includes coordinates.",
                "Field trip", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        SaveCurrentTripFields();
        _selectedTrip.Stops.Add(stop);
        StopsList.Items.Refresh();
        StopsList.SelectedItem = stop;
        FieldTripStore.Upsert(_selectedTrip);
        if (CommunityStopInput != null)
            CommunityStopInput.Text = "";
        PushStopsToMap();
    }

    private FieldTripDocument? CurrentTrip()
    {
        SaveCurrentTripFields();
        return _selectedTrip;
    }

    private void ExportGpx_Click(object sender, RoutedEventArgs e)
    {
        var trip = CurrentTrip();
        if (trip == null || trip.Stops.Count == 0)
            return;
        var dlg = new SaveFileDialog
        {
            Title = "Export field trip GPX",
            Filter = "GPX|*.gpx",
            FileName = $"{trip.Name}.gpx",
        };
        if (dlg.ShowDialog(this) != true)
            return;
        File.WriteAllText(dlg.FileName, FieldTripExportService.ExportGpx(trip));
    }

    private void ExportKml_Click(object sender, RoutedEventArgs e)
    {
        var trip = CurrentTrip();
        if (trip == null || trip.Stops.Count == 0)
            return;
        var dlg = new SaveFileDialog
        {
            Title = "Export field trip KML",
            Filter = "KML|*.kml",
            FileName = $"{trip.Name}.kml",
        };
        if (dlg.ShowDialog(this) != true)
            return;
        File.WriteAllText(dlg.FileName, FieldTripExportService.ExportKml(trip));
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var trip = CurrentTrip();
        if (trip == null || trip.Stops.Count == 0)
            return;
        var dlg = new SaveFileDialog
        {
            Title = "Export field trip PDF",
            Filter = "PDF|*.pdf",
            FileName = $"{trip.Name}.pdf",
        };
        if (dlg.ShowDialog(this) != true)
            return;
        FieldTripPdfExportService.ExportPdf(trip, dlg.FileName);
        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.FieldTripExportPdf);
        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.FieldTripExport);
        MessageBox.Show(this, "PDF itinerary saved.", "Field trip", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        var trip = CurrentTrip();
        if (trip == null)
            return;
        Clipboard.SetText(FieldTripExportService.BuildTextSummary(trip));
        MessageBox.Show(this, "Itinerary copied to clipboard.", "Field trip", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenGoogleMaps_Click(object sender, RoutedEventArgs e)
    {
        var trip = CurrentTrip();
        if (trip == null)
            return;
        var url = FieldTripExportService.BuildGoogleMapsDirectionsUrl(trip);
        if (url == null)
        {
            MessageBox.Show(this, "Add at least two stops with coordinates.", "Field trip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void CopyShareLink_Click(object sender, RoutedEventArgs e)
    {
        var trip = CurrentTrip();
        if (trip == null || trip.Stops.Count == 0)
            return;
        var url = await FieldTripShareCodec.BuildShareUrlAsync(trip.Stops);
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this,
                "Could not create a share link for this trip. Sign in to CaveAI Pro, then try again (large trips need cloud storage).",
                "Field trip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Clipboard.SetText(url);
        MessageBox.Show(this, "Share link copied to clipboard:\n" + url, "Field trip", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ImportShareLink_Click(object sender, RoutedEventArgs e)
    {
        var pasted = ShareUrlPrompt.Show(this, "Field trip share link", "Paste a caveaipro.com field trip URL:");
        if (string.IsNullOrWhiteSpace(pasted))
            return;

        var payload = await FieldTripShareCodec.TryParseFromUrlAsync(pasted);
        if (payload == null)
        {
            MessageBox.Show(this, "Could not parse field trip link.", "Field trip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SaveCurrentTripFields();
        var trip = _selectedTrip ?? _store.Trips.FirstOrDefault();
        if (trip == null)
        {
            trip = new FieldTripDocument { Name = "Imported trip" };
            _store.Trips.Add(trip);
        }

        trip.Stops.Clear();
        trip.Stops.AddRange(FieldTripShareCodec.ToFieldTripStops(payload));
        FieldTripStore.Upsert(trip);
        ReloadStore();
        TripsList.SelectedItem = trip;
        MessageBox.Show(this, $"Imported {trip.Stops.Count} stop(s).", "Field trip", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OpenShareOnWeb_Click(object sender, RoutedEventArgs e)
    {
        var trip = CurrentTrip();
        if (trip == null || trip.Stops.Count == 0)
            return;
        var url = await FieldTripShareCodec.BuildShareUrlAsync(trip.Stops);
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this,
                "Could not create a share link for this trip. Sign in to CaveAI Pro, then try again (large trips need cloud storage).",
                "Field trip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void PreviewOnExploreTerrain_Click(object sender, RoutedEventArgs e)
    {
        var trip = CurrentTrip();
        if (trip == null || trip.Stops.Count == 0)
        {
            MessageBox.Show(this, "Add at least one stop first.", "Explore terrain", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SaveCurrentTripFields();
        var url = PublicLibraryCatalog.WithEmbed(
            ReferenceCatalogShareUrls.BuildExploreTerrainUrlForStops(trip.Stops, notes: trip.Notes));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
