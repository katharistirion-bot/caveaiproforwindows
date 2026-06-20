using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Shapes;

using CaveAiProForWindows.Models;

using CaveAiProForWindows.Services;

using CaveAiProForWindows.Services.FieldTrip;

using CaveAiProForWindows.Services.Favorites;
using CaveAiProForWindows.Services.ReferenceCatalog;

using CaveAiProForWindows.ViewModels;



namespace CaveAiProForWindows.Views;



public partial class ReferenceCatalogWindow : Window

{

    private readonly ReferenceCatalogFetchService _fetch = new();

    private readonly ReferenceCatalogDetailLoader _detailLoader = new();

    private readonly CaveFavoriteService _favorites = new();

    private IReadOnlyList<ReferenceCaveIndexEntry> _allEntries = [];

    private ReferenceCaveIndexEntry? _selected;

    private bool _isOffline;

    private static ReferenceCatalogWindow? _active;

    private ReferenceCatalogGeolocation.NearMeOrigin? _nearMeOrigin;

    private bool _nearMeLocating;



    public ReferenceCatalogWindow()

    {

        InitializeComponent();

        SearchBox.TextChanged += (_, _) => ApplyFilter();

        CountryCombo.SelectionChanged += (_, _) => ApplyFilter();

        NearMeCheck.Checked += (_, _) => _ = ResolveNearMeOriginAsync();

        NearMeCheck.Unchecked += (_, _) =>
        {
            _nearMeOrigin = null;
            _nearMeLocating = false;
            ApplyFilter();
        };

        RichMetadataOnlyCheck.Checked += (_, _) => ApplyFilter();
        RichMetadataOnlyCheck.Unchecked += (_, _) => ApplyFilter();

        Loaded += async (_, _) =>

        {

            ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.CatalogOpen);

            BuildMapDepthLegend();

            await LoadAsync();

        };

    }



    private void BuildMapDepthLegend()

    {

        if (MapDepthLegendPanel == null)

            return;

        MapDepthLegendPanel.Children.Clear();

        var title = new TextBlock

        {

            Text = "Depth",

            FontWeight = FontWeights.Bold,

            FontSize = 10,

            Foreground = Brushes.White,

            Margin = new Thickness(0, 0, 8, 0),

            VerticalAlignment = VerticalAlignment.Center,

        };

        MapDepthLegendPanel.Children.Add(title);

        AddDepthLegendChip("Shallow", ReferenceCatalogMapDepthColors.Shallow);

        AddDepthLegendChip("Medium", ReferenceCatalogMapDepthColors.Medium);

        AddDepthLegendChip("Deep", ReferenceCatalogMapDepthColors.Deep);

        AddDepthLegendChip("Unknown", ReferenceCatalogMapDepthColors.Unknown);

    }



    private void AddDepthLegendChip(string label, System.Windows.Media.Color fill)

    {

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };

        panel.Children.Add(new Polygon

        {

            Points = new PointCollection { new(4, 0), new(8, 4), new(4, 8), new(0, 4) },

            Fill = new SolidColorBrush(fill),

            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4E, 0x34, 0x2E)),

            StrokeThickness = 1,

            Width = 8,

            Height = 8,

            Margin = new Thickness(0, 0, 4, 0),

        });

        panel.Children.Add(new TextBlock

        {

            Text = label,

            FontSize = 9,

            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x94, 0x9E)),

            VerticalAlignment = VerticalAlignment.Center,

        });

        MapDepthLegendPanel.Children.Add(panel);

    }



    public static void ShowSingleton(Window? owner)

    {

        if (_active != null)

        {

            try

            {

                if (_active.IsLoaded)

                {

                    if (_active.WindowState == WindowState.Minimized)

                        _active.WindowState = WindowState.Normal;

                    _active.Show();

                    _active.Activate();

                    return;

                }

            }

            catch

            {

                _active = null;

            }

        }



        _active = new ReferenceCatalogWindow { Owner = owner };

        _active.Closed += (_, _) => { if (ReferenceEquals(_active, _active)) _active = null; };

        _active.Show();

        _active.Activate();

    }



    private async Task LoadAsync(bool forceRefresh = false)

    {

        StatusText.Text = forceRefresh

            ? "Force refreshing reference catalog index…"

            : "Loading reference catalog index…";

        try

        {

            if (forceRefresh)

                ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.CatalogForceRefresh);



            var state = await _fetch.LoadBrowseIndexAsync(forceRefresh, new Progress<string>(m => StatusText.Text = m));

            _allEntries = state.IndexEntries;

            _isOffline = state.IsOffline;

            OfflineBanner.Visibility = _isOffline ? Visibility.Visible : Visibility.Collapsed;



            HeaderText.Text = $"Reference catalog — {_allEntries.Count:N0} caves";

            StatusText.Text = state.FromNetwork

                ? "Downloaded latest index from caveaipro.com"

                : state.IsOffline

                    ? $"Offline — loaded from {state.Source}"

                    : $"Loaded from {state.Source}";



            var countries = new List<string> { "All" };

            countries.AddRange(ReferenceCatalogSearch.ListCountries(_allEntries));

            CountryCombo.ItemsSource = countries;

            CountryCombo.SelectedIndex = 0;



            var featured = await _fetch.LoadFeaturedCavesAsync();

            FeaturedList.ItemsSource = featured;



            ApplyFilter();

            DrawMap();

            _ = _favorites.RefreshFromCloudAsync();

        }

        catch (Exception ex)

        {

            _isOffline = true;

            OfflineBanner.Visibility = Visibility.Visible;

            StatusText.Text = "Failed to load catalog: " + ex.Message;

        }

    }



    private void ApplyFilter()

    {

        var nearMe = NearMeCheck.IsChecked == true;

        DistanceColumn.Visibility = nearMe ? Visibility.Visible : Visibility.Collapsed;



        double? nearLat = null;

        double? nearLon = null;

        var radiusKm = RadiusSlider.Value;

        RadiusLabel.Text = $"{radiusKm:0} km";



        if (nearMe)
        {
            if (_nearMeLocating)
            {
                if (StatusText.Text is not { Length: > 0 } s || !s.StartsWith("Near me:", StringComparison.Ordinal))
                    StatusText.Text = "Near me: getting Windows location…";
                ResultsGrid.ItemsSource = Array.Empty<ReferenceCatalogRow>();
                return;
            }

            var origin = _nearMeOrigin ?? TryGetProjectNearMeOrigin();
            if (origin == null)
            {
                StatusText.Text = "Near me: allow location access or open a survey project with entrance GPS.";
                ResultsGrid.ItemsSource = Array.Empty<ReferenceCatalogRow>();
                return;
            }

            nearLat = origin.Lat;
            nearLon = origin.Lon;
        }



        var country = CountryCombo.SelectedItem as string;
        var richMetadataOnly = RichMetadataOnlyCheck.IsChecked == true;

        if (!ReferenceCatalogSearch.ShouldRunSearch(SearchBox.Text, country, nearMe, richMetadataOnly))

        {

            ResultsGrid.ItemsSource = BuildRows(_allEntries.Take(200).ToList(), nearLat, nearLon, nearMe);

            return;

        }



        var filtered = ReferenceCatalogSearch.Filter(

            _allEntries,

            SearchBox.Text,

            country,

            nearLat,

            nearLon,

            nearRadiusKm: radiusKm,
            richMetadataOnly: richMetadataOnly);

        if (FavoritesOnlyCheck.IsChecked == true)
        {
            var favIds = _favorites.CachedFavoriteIds;
            filtered = filtered.Where(e => favIds.Contains(e.Id)).ToList();
        }

        ResultsGrid.ItemsSource = BuildRows(filtered, nearLat, nearLon, nearMe);

        if (nearMe && nearLat.HasValue && nearLon.HasValue)
        {
            var source = _nearMeOrigin?.SourceLabel ?? TryGetProjectNearMeOrigin()?.SourceLabel ?? "GPS";
            StatusText.Text = $"Near me ({source}): {filtered.Count} caves within {radiusKm:0} km.";
        }

        DrawMap();
    }



    private static List<ReferenceCatalogRow> BuildRows(

        IReadOnlyList<ReferenceCaveIndexEntry> entries,

        double? nearLat,

        double? nearLon,

        bool showDistance)

    {

        return entries.Select(e =>

        {

            double? dist = showDistance && nearLat.HasValue && nearLon.HasValue

                ? GeoHaversine.DistanceKm(nearLat.Value, nearLon.Value, e.Lat, e.Lon)

                : null;

            return ReferenceCatalogRow.FromEntry(e, dist);

        }).ToList();

    }



    private ReferenceCatalogGeolocation.NearMeOrigin? TryGetProjectNearMeOrigin()
    {
        if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
            return ReferenceCatalogGeolocation.TryGetProjectEntrance(vm.SelectedProject);
        return null;
    }

    private async Task ResolveNearMeOriginAsync()
    {
        if (NearMeCheck.IsChecked != true)
            return;

        _nearMeLocating = true;
        _nearMeOrigin = null;
        ApplyFilter();

        try
        {
            var device = await ReferenceCatalogGeolocation.TryGetDeviceLocationAsync();
            if (device != null)
            {
                _nearMeOrigin = device;
            }
            else if (TryGetProjectNearMeOrigin() is { } projectOrigin)
            {
                _nearMeOrigin = projectOrigin;
            }
        }
        finally
        {
            _nearMeLocating = false;
            if (IsLoaded)
                ApplyFilter();
        }
    }



    private void RadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)

    {

        if (!IsLoaded)

            return;

        ApplyFilter();

    }



    private async void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (ResultsGrid.SelectedItem is not ReferenceCatalogRow row)

            return;

        _selected = row.Entry;

        await ShowDetailAsync(row.Entry);

    }



    private async void FeaturedList_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (FeaturedList.SelectedItem is not ReferenceCaveIndexEntry entry)

            return;

        _selected = entry;

        await ShowDetailAsync(entry);

        MainTabs.SelectedIndex = 0;

        ResultsGrid.SelectedItem = BuildRows([entry], null, null, false).FirstOrDefault();

    }



    private async Task ShowDetailAsync(ReferenceCaveIndexEntry entry)

    {

        DetailPanel.Children.Clear();

        DetailPanel.Children.Add(new TextBlock

        {

            Text = entry.Name,

            FontWeight = FontWeights.SemiBold,

            FontSize = 16,

            TextWrapping = TextWrapping.Wrap,

            Margin = new Thickness(0, 0, 0, 8),

        });



        void AddLine(string label, string? value)

        {

            if (string.IsNullOrWhiteSpace(value))

                return;

            DetailPanel.Children.Add(new TextBlock

            {

                Text = $"{label}: {value}",

                TextWrapping = TextWrapping.Wrap,

                Margin = new Thickness(0, 0, 0, 4),

                Foreground = (Brush)FindResource("Cave.TextMuted"),

            });

        }



        AddLine("Country", entry.Country);

        AddLine("Region", entry.Region);

        var locationLine = string.Join(" · ", new[] { entry.Region, entry.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
        AddLine("Location", locationLine);

        AddLine("Coordinates", $"{entry.Lat:F5}, {entry.Lon:F5}");

        if (entry.DepthM is > 0) AddLine("Depth", $"{entry.DepthM:0.#} m");

        if (entry.LengthM is > 0) AddLine("Length", $"{entry.LengthM:0.#} m");

        if (entry.ElevationM is double elev && double.IsFinite(elev))
            AddLine("Elevation", $"{elev:0.#} m");

        AddLine("Type", entry.CaveType);

        AddLine("Badge", ReferenceCatalogDisplay.ListBadge(entry));

        var summary = ReferenceCatalogDisplay.ListSummary(entry);
        if (!string.IsNullOrWhiteSpace(summary))
            AddLine("Summary", summary);

        AddLine("Preview", ReferenceCatalogDisplay.PreviewText(entry));

        AddLine("Share", ReferenceCatalogShareUrls.BuildShareUrl(entry));



        StatusText.Text = _isOffline ? "Loading detail from offline cache…" : "Loading detail…";

        var pin = await _detailLoader.LoadByIdAsync(entry.Id, entry.Country);

        if (pin != null)

        {

            AddLine("Ref code", pin.RefCode);

            AddLine("Access", pin.AccessNote);

            AddLine("Description", ReferenceCatalogDisplay.PinDescription(pin));

            AddLine("Website", pin.Website);

            AddLine("Wikipedia", pin.Wikipedia);

            AddLine("Source", "OpenStreetMap");

            if (!string.IsNullOrWhiteSpace(pin.OsmType) && pin.OsmId is > 0)
                AddLine("OSM", $"{pin.OsmType}/{pin.OsmId}");

        }

        else if (_isOffline)

        {

            AddLine("Detail", "Shard not cached offline for this country.");

        }

        var similar = ReferenceCatalogSimilarCaves.FindSimilar(entry, _allEntries);
        if (similar.Count > 0)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = "Similar caves nearby",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 6),
            });
            foreach (var match in similar)
            {
                var line = $"{match.Entry.Name} — {match.DistanceKm:0.#} km";
                if (match.Entry.DepthM is > 0)
                    line += $", ~{match.Entry.DepthM:0.#} m";
                var link = new TextBlock
                {
                    Text = line,
                    TextDecorations = TextDecorations.Underline,
                    Foreground = (Brush)FindResource("Cave.AccentBrush"),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 0, 4),
                    Tag = match.Entry,
                };
                link.MouseLeftButtonUp += (_, _) =>
                {
                    if (link.Tag is ReferenceCaveIndexEntry target)
                    {
                        _ = ShowDetailAsync(target);
                        ResultsGrid.SelectedItem = BuildRows([target], null, null, false).FirstOrDefault();
                    }
                };
                DetailPanel.Children.Add(link);
            }
        }

        var favBtn = new Button
        {
            Content = _favorites.IsFavoritedLocally(entry.Id) ? "★ Favorited" : "☆ Add to favorites",
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Tag = entry,
        };
        favBtn.Click += async (_, _) =>
        {
            if (favBtn.Tag is not ReferenceCaveIndexEntry favEntry)
                return;
            try
            {
                var next = !_favorites.IsFavoritedLocally(favEntry.Id);
                await _favorites.SetFavoriteAsync(
                    favEntry.Id,
                    next,
                    "reference",
                    favEntry.Name,
                    favEntry.Country);
                favBtn.Content = next ? "★ Favorited" : "☆ Add to favorites";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Favorites", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        DetailPanel.Children.Add(favBtn);



        StatusText.Text = $"{_allEntries.Count:N0} caves in index";

    }



    private void DrawMap()

    {

        MapCanvas.Children.Clear();

        var items = (ResultsGrid.ItemsSource as IEnumerable<ReferenceCatalogRow>)?.Select(r => r.Entry).ToList()

                    ?? _allEntries.Take(500).ToList();

        if (items.Count == 0)

            return;



        var bounds = ReferenceCatalogMapClustering.ComputeBounds(items);

        var clusters = ReferenceCatalogMapClustering.Cluster(

            items, bounds.MinLat, bounds.MaxLat, bounds.MinLon, bounds.MaxLon);



        MapCanvas.Loaded += (_, _) => RenderClusters(clusters, bounds);

        if (MapCanvas.ActualWidth > 10)

            RenderClusters(clusters, bounds);

    }



    private void RenderClusters(

        IReadOnlyList<ReferenceCatalogMapClustering.ClusterPin> clusters,

        (double MinLat, double MaxLat, double MinLon, double MaxLon) bounds)

    {

        MapCanvas.Children.Clear();

        var w = MapCanvas.ActualWidth;

        var h = MapCanvas.ActualHeight;

        if (w < 10 || h < 10)

            return;



        var latSpan = Math.Max(0.001, bounds.MaxLat - bounds.MinLat);

        var lonSpan = Math.Max(0.001, bounds.MaxLon - bounds.MinLon);



        foreach (var c in clusters)

        {

            var x = (c.Lon - bounds.MinLon) / lonSpan * (w - 20) + 10;

            var y = h - ((c.Lat - bounds.MinLat) / latSpan * (h - 20) + 10);

            var depthM = c.Count == 1 ? c.Members[0].DepthM : null;

            var ellipse = new Ellipse

            {

                Width = c.Count > 1 ? 14 : 10,

                Height = c.Count > 1 ? 14 : 10,

                Fill = c.Count > 1

                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0xFF))

                    : ReferenceCatalogMapDepthColors.ReferenceFillBrush(depthM),

                Stroke = Brushes.White,

                StrokeThickness = 1,

                Tag = c,

            };

            Canvas.SetLeft(ellipse, x - ellipse.Width / 2);

            Canvas.SetTop(ellipse, y - ellipse.Height / 2);

            MapCanvas.Children.Add(ellipse);

        }

    }



    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)

    {

        if (e.OriginalSource is not Shape shape || shape.Tag is not ReferenceCatalogMapClustering.ClusterPin cluster)

            return;

        var first = cluster.Members.FirstOrDefault();

        if (first == null)

            return;

        _selected = first;

        ResultsGrid.SelectedItem = BuildRows([first], null, null, NearMeCheck.IsChecked == true).FirstOrDefault();

        _ = ShowDetailAsync(first);

    }



    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync(forceRefresh: true);

    private void FavoritesOnlyCheck_Changed(object sender, RoutedEventArgs e) => ApplyFilter();



    private void Compare_Click(object sender, RoutedEventArgs e) =>

        ReferenceCommunityCompareWindow.ShowDialog(this, _selected);



    private void StartSurveyOnPhone_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            MessageBox.Show(this, "Select a reference cave first.", "Map with Cave AI Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var url = ReferenceCatalogShareUrls.BuildSurveyStartUrl(_selected);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.CatalogSurveyStartLink);
    }

    private async void LinkToCurrentProject_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            MessageBox.Show(this, "Select a reference cave first.", "Link reference", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Application.Current.MainWindow?.DataContext is not MainViewModel vm || vm.SelectedProject == null)
        {
            MessageBox.Show(this, "Open a survey project in the main window first.", "Link reference", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var project = vm.SelectedProject;
        if (ReferenceSurveyLinkService.TryGetLink(project, out var existing) &&
            string.Equals(existing?.Id, _selected.Id, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "This project is already linked to the selected reference pin.", "Link reference", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ReferenceCavePin? detail = null;
        try
        {
            detail = await _detailLoader.LoadByIdAsync(_selected.Id, _selected.Country).ConfigureAwait(true);
        }
        catch
        {
            /* optional shard detail */
        }

        ReferenceSurveyLinkService.SetLink(project, _selected, detail);
        vm.PersistProjectBeforeSave?.Invoke(project);
        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.CatalogSurveyStartLink);
        MessageBox.Show(
            this,
            $"Linked \"{project.Name}\" to reference pin \"{_selected.Name}\".\n\nreferenceCatalogId is written to Firestore when you publish from Android or Push to Cloud.",
            "Link reference",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CopyShareLink_Click(object sender, RoutedEventArgs e)

    {

        if (_selected == null)

        {

            MessageBox.Show(this, "Select a reference cave first.", "Share link", MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }



        var url = ReferenceCatalogShareUrls.BuildShareUrl(_selected);

        Clipboard.SetText(url);

        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.CatalogShareLinkCopy);

        MessageBox.Show(this, "Share link copied to clipboard:\n" + url, "Share link", MessageBoxButton.OK, MessageBoxImage.Information);

    }



    private void AddToFieldTrip_Click(object sender, RoutedEventArgs e)

    {

        if (_selected == null)

        {

            MessageBox.Show(this, "Select a reference cave first.", "Field trip", MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }



        FieldTripPlannerWindow.AddStopFromReference(this, _selected);

    }



    private void OpenOnWeb_Click(object sender, RoutedEventArgs e)

    {

        if (_selected != null)

        {

            var url = ReferenceCatalogShareUrls.BuildShareUrl(_selected);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

            return;

        }



        PublicLibraryCatalog.OpenMap();

    }



    private async void PasteShareUrl_Click(object sender, RoutedEventArgs e)

    {

        var pasted = ShareUrlPrompt.Show(this, "Reference share URL", "Paste a caveaipro.com/cave/ref/ URL:");

        if (string.IsNullOrWhiteSpace(pasted))

            return;

        if (!ReferenceCatalogShareUrls.TryParseReferenceShareUrl(pasted, out var refId, out var countryHint) ||

            string.IsNullOrWhiteSpace(refId))

        {

            MessageBox.Show(this, "Not a valid reference catalog share URL.", "Share URL", MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }

        var entry = _allEntries.FirstOrDefault(x => string.Equals(x.Id, refId, StringComparison.OrdinalIgnoreCase));

        if (entry == null)

        {

            var pin = await _detailLoader.LoadByIdAsync(refId, countryHint).ConfigureAwait(true);

            if (pin != null)

            {

                entry = new ReferenceCaveIndexEntry

                {

                    Id = pin.Id,

                    Name = pin.Name,

                    Lat = pin.Lat,

                    Lon = pin.Lon,

                    Country = pin.Country,

                    Region = pin.Region,

                    DepthM = pin.DepthM,

                    LengthM = pin.LengthM,

                    ElevationM = pin.ElevationM,

                    CaveType = pin.CaveType,

                    OsmType = pin.OsmType,

                    OsmId = pin.OsmId,

                    Preview = pin.Description ?? string.Empty,

                    Rich = pin.Rich,

                };

            }

        }

        if (entry == null)

        {

            MessageBox.Show(this, "Reference id not found in the loaded catalog index. Try refresh or search.", "Share URL", MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }

        _selected = entry;

        ResultsGrid.SelectedItem = ResultsGrid.Items.Cast<ReferenceCatalogRow>().FirstOrDefault(r => r.Entry.Id == entry.Id);

        await ShowDetailAsync(entry);

    }



    private void OpenBrowser_Click(object sender, RoutedEventArgs e) => OpenOnWeb_Click(sender, e);



    private void Close_Click(object sender, RoutedEventArgs e)

    {

        ReferenceCatalogLightAnalytics.PersistSnapshot();

        Close();

    }



    private sealed class ReferenceCatalogRow

    {

        public ReferenceCaveIndexEntry Entry { get; init; } = null!;

        public string Name => Entry.Name;

        public string? Country => Entry.Country;

        public string? Region => Entry.Region;

        public string DepthDisplay => Entry.DepthM is > 0 ? $"{Entry.DepthM:0.#}" : "";

        public string LengthDisplay => Entry.LengthM is > 0 ? $"{Entry.LengthM:0.#}" : "";

        public string Summary => ReferenceCatalogDisplay.PreviewText(Entry, 100);

        public string Badge => ReferenceCatalogDisplay.ListBadge(Entry);

        public string DistanceDisplay { get; init; } = "";



        public static ReferenceCatalogRow FromEntry(ReferenceCaveIndexEntry entry, double? distanceKm) =>

            new()

            {

                Entry = entry,

                DistanceDisplay = distanceKm is > 0 ? $"{distanceKm:0.#}" : distanceKm is 0 ? "0" : "",

            };

    }

}

