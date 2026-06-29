using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.FieldTrip;
using CaveAiProForWindows.Services.ReferenceCatalog;
using Microsoft.Win32;

namespace CaveAiProForWindows.Views;

public partial class FieldTripPlannerWindow : Window
{
    private FieldTripStoreFile _store = new();
    private FieldTripDocument? _selectedTrip;
    private static FieldTripPlannerWindow? _active;

    public FieldTripPlannerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ReloadStore();
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
            return;
        }

        TripNameBox.Text = _selectedTrip.Name;
        TripNotesBox.Text = _selectedTrip.Notes ?? "";
        StopsList.ItemsSource = _selectedTrip.Stops;
        RefreshStopsMapPreview();
    }

    private void StopsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshStopsMapPreview();

    private void RefreshStopsMapPreview()
    {
        StopsMapPreview.Children.Clear();
        if (_selectedTrip == null)
            return;

        var stops = _selectedTrip.Stops.Where(s => s.Lat != 0 || s.Lon != 0).ToList();
        if (stops.Count == 0)
        {
            StopsMapPreview.Children.Add(new TextBlock
            {
                Text = "Add stops with coordinates to see a preview.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(8),
            });
            return;
        }

        var minLat = stops.Min(s => s.Lat);
        var maxLat = stops.Max(s => s.Lat);
        var minLon = stops.Min(s => s.Lon);
        var maxLon = stops.Max(s => s.Lon);
        if (Math.Abs(maxLat - minLat) < 1e-8)
        {
            minLat -= 0.001;
            maxLat += 0.001;
        }

        if (Math.Abs(maxLon - minLon) < 1e-8)
        {
            minLon -= 0.001;
            maxLon += 0.001;
        }

        void LayoutPreview(object? sender, EventArgs _)
        {
            StopsMapPreview.Children.Clear();
            var w = StopsMapPreview.ActualWidth;
            var h = StopsMapPreview.ActualHeight;
            if (w < 8 || h < 8)
                return;

            const int gridLines = 4;
            for (var g = 1; g < gridLines; g++)
            {
                var gx = g * w / gridLines;
                var gy = g * h / gridLines;
                StopsMapPreview.Children.Add(new Line
                {
                    X1 = gx, Y1 = 0, X2 = gx, Y2 = h,
                    Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false,
                });
                StopsMapPreview.Children.Add(new Line
                {
                    X1 = 0, Y1 = gy, X2 = w, Y2 = gy,
                    Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false,
                });
            }

            var points = new List<Point>();
            for (var i = 0; i < stops.Count; i++)
            {
                var s = stops[i];
                var x = (s.Lon - minLon) / (maxLon - minLon) * (w - 16) + 8;
                var y = (maxLat - s.Lat) / (maxLat - minLat) * (h - 16) + 8;
                points.Add(new Point(x, y));
            }

            if (points.Count > 1)
            {
                StopsMapPreview.Children.Add(new Polyline
                {
                    Points = new PointCollection(points),
                    Stroke = new SolidColorBrush(Color.FromArgb(180, 80, 160, 255)),
                    StrokeThickness = 2,
                    StrokeDashArray = [4, 3],
                    IsHitTestVisible = false,
                });
            }

            for (var i = 0; i < stops.Count; i++)
            {
                var s = stops[i];
                var pt = points[i];
                var dot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = i == 0 ? Brushes.LimeGreen : Brushes.DeepSkyBlue,
                    ToolTip = $"{i + 1}. {s.Name}",
                };
                Canvas.SetLeft(dot, pt.X - 5);
                Canvas.SetTop(dot, pt.Y - 5);
                StopsMapPreview.Children.Add(dot);

                var label = new TextBlock
                {
                    Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(label, pt.X + 6);
                Canvas.SetTop(label, pt.Y - 6);
                StopsMapPreview.Children.Add(label);
            }
        }

        StopsMapPreview.SizeChanged -= LayoutPreview;
        StopsMapPreview.SizeChanged += LayoutPreview;
        LayoutPreview(StopsMapPreview, EventArgs.Empty);
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
    }

    private void RemoveStop_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTrip == null || StopsList.SelectedItem is not FieldTripStop stop)
            return;
        _selectedTrip.Stops.Remove(stop);
        StopsList.Items.Refresh();
        FieldTripStore.Upsert(_selectedTrip);
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
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
