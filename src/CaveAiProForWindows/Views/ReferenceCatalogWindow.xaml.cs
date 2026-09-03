using System.Threading;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Shapes;

using CaveAiProForWindows.Models;

using CaveAiProForWindows.Services;

using CaveAiProForWindows.Services.FieldTrip;

using CaveAiProForWindows.Services.Favorites;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.ReferenceCatalog;
using CaveAiProForWindows.Services.Gemini;

using System.Windows.Threading;

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
    private string? _nearMeStatusMessage;
    private bool _nearMeLocating;

    private readonly DispatcherTimer _searchDebounceTimer;

    private int _filterGeneration;



    public ReferenceCatalogWindow()

    {

        InitializeComponent();

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };

        _searchDebounceTimer.Tick += (_, _) =>

        {

            _searchDebounceTimer.Stop();

            ApplyFilter();

        };

        SearchBox.TextChanged += (_, _) =>

        {

            _searchDebounceTimer.Stop();

            _searchDebounceTimer.Start();

        };

        CountryCombo.SelectionChanged += (_, _) => ApplyFilter();

        NearMeCheck.Checked += (_, _) => _ = ResolveNearMeOriginAsync();

        NearMeCheck.Unchecked += (_, _) =>
        {
            _nearMeOrigin = null;
            _nearMeStatusMessage = null;
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



            if (CloudPublishWebViewHost.TokenCache.TryGetUsableToken() == null)

            {

                await DesktopAuthWindow.AcquireTokenAsync(this, CloudPublishWebViewHost.TokenCache).ConfigureAwait(true);

            }



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

            if (_allEntries.Count == 0)
            {
                HeaderText.Text = CaveAiProForWindows.Services.Auth.GuestLibraryCopy.SignInHeadingPanel;
                StatusText.Text = CaveAiProForWindows.Services.Auth.GuestLibraryCopy.LeadPanel;
            }



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

            StatusText.Text = CaveAiProForWindows.Services.Auth.GuestLibraryCopy.CatalogLoadFailure(ex);
            HeaderText.Text = CaveAiProForWindows.Services.Auth.GuestLibraryCopy.SignInHeadingPanel;

        }

    }



    private void ApplyFilter()

    {

        var generation = Interlocked.Increment(ref _filterGeneration);

        var nearMe = NearMeCheck.IsChecked == true;

        var country = CountryCombo.SelectedItem as string;

        var richMetadataOnly = RichMetadataOnlyCheck.IsChecked == true;

        var searchText = SearchBox.Text;

        var radiusKm = RadiusSlider.Value;

        var favoritesOnly = FavoritesOnlyCheck.IsChecked == true;

        var favIds = favoritesOnly ? _favorites.CachedFavoriteIds : null;

        var entries = _allEntries;

        var nearMeLocating = _nearMeLocating;

        var nearMeOrigin = _nearMeOrigin ?? TryGetProjectNearMeOrigin();



        DistanceColumn.Visibility = nearMe ? Visibility.Visible : Visibility.Collapsed;

        RadiusLabel.Text = $"{radiusKm:0} km";



        if (nearMe)

        {

            if (_allEntries.Count == 0)
            {
                StatusText.Text = CaveAiProForWindows.Services.Auth.GuestLibraryCopy.EmptyCatalogStatus;
                ResultsGrid.ItemsSource = Array.Empty<ReferenceCatalogRow>();
                return;
            }

            if (nearMeLocating)
            {
                StatusText.Text = _nearMeStatusMessage ?? "Near me: getting high-accuracy Windows location…";
                ResultsGrid.ItemsSource = Array.Empty<ReferenceCatalogRow>();
                return;
            }

            if (nearMeOrigin == null)
            {
                StatusText.Text = _nearMeStatusMessage
                    ?? "Near me: allow location access or open a survey project with entrance GPS.";
                ResultsGrid.ItemsSource = Array.Empty<ReferenceCatalogRow>();
                return;
            }

            if (!string.IsNullOrWhiteSpace(_nearMeStatusMessage))
            {
                StatusText.Text = $"Near me: {_nearMeStatusMessage}";
            }
        }



        if (!ReferenceCatalogSearch.ShouldRunSearch(searchText, country, nearMe, richMetadataOnly))

        {

            ResultsGrid.ItemsSource = BuildRows(entries.Take(200).ToList(), nearMeOrigin?.Lat, nearMeOrigin?.Lon, nearMe);

            if (MainTabs.SelectedIndex == 1)

                DrawMap();

            return;

        }



        _ = Task.Run(() =>

        {

            var filtered = ReferenceCatalogSearch.Filter(

                entries,

                searchText,

                country,

                nearMe ? nearMeOrigin?.Lat : null,

                nearMe ? nearMeOrigin?.Lon : null,

                nearRadiusKm: radiusKm,

                richMetadataOnly: richMetadataOnly);

            if (favoritesOnly && favIds != null)

                filtered = filtered.Where(e => favIds.Contains(e.Id)).ToList();

            var rows = BuildRows(filtered, nearMeOrigin?.Lat, nearMeOrigin?.Lon, nearMe);

            return (rows, filtered.Count, nearMe, nearMeOrigin, radiusKm);

        }).ContinueWith(t =>

        {

            if (generation != _filterGeneration || t.IsFaulted)

                return;

            var (rows, count, nearMeActive, origin, radius) = t.Result;

            Dispatcher.Invoke(() =>

            {

                if (generation != _filterGeneration)

                    return;

                ResultsGrid.ItemsSource = rows;

                if (nearMeActive && origin != null)

                {

                    var source = origin.SourceLabel ?? "GPS";

                    StatusText.Text = $"Near me ({source}): {count} caves within {radius:0} km.";

                }

                if (MainTabs.SelectedIndex == 1)

                    DrawMap();

            });

        }, TaskScheduler.Default);

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
        _nearMeStatusMessage = "Acquiring high-accuracy GPS…";
        ApplyFilter();

        try
        {
            var device = await ReferenceCatalogGeolocation.TryGetDeviceLocationAsync();
            if (device.Ok && device.Origin is { } origin)
            {
                _nearMeOrigin = origin;
                _nearMeStatusMessage = device.Warning;
            }
            else if (TryGetProjectNearMeOrigin() is { } projectOrigin)
            {
                var validated = ReferenceCatalogGeolocation.ValidateNearMeOrigin(projectOrigin);
                if (validated.Ok && validated.Origin is { } okOrigin)
                {
                    _nearMeOrigin = okOrigin;
                    _nearMeStatusMessage = validated.Warning ?? "Using project entrance (no GPS fix).";
                }
                else
                {
                    _nearMeStatusMessage = validated.Error ?? device.Error ?? "Near me unavailable.";
                }
            }
            else
            {
                _nearMeStatusMessage = device.Error ?? "Near me unavailable.";
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

            foreach (var note in ReferenceCatalogDisplay.ArchaeologicalNotes(pin))
                AddLine(note.Label, note.Value);

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

        var askAiBtn = new Button
        {
            Content = "Ask Cave AI (web)",
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Tag = pin,
        };
        askAiBtn.Click += (_, _) => OpenCaveAiWebForEntry(entry, pin);
        DetailPanel.Children.Add(askAiBtn);



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

            var diamond = ReferenceCatalogMapDepthColors.CreateClusterPin(c.Count, depthM, x, y);
            diamond.Tag = c;

            MapCanvas.Children.Add(diamond);

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
        var caveName = _selected.Name ?? _selected.Id ?? "Cave";
        var qrWindow = new SurveyPhoneQrWindow(caveName, url) { Owner = this };
        qrWindow.ShowDialog();
        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.CatalogSurveyStartLink);
    }

    private void AskCaveAiWeb_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            MessageBox.Show(this, "Select a reference cave first.", "Cave AI on web", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenCaveAiWebForEntry(_selected);
    }

    private async void CaveAiSend_Click(object sender, RoutedEventArgs e)
    {
        var prompt = CaveAiPromptBox.Text?.Trim() ?? "";
        if (prompt.Length < 2)
        {
            MessageBox.Show(this, "Enter a question (at least 2 characters).", "Cave AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!GeminiProxyClient.IsConfigured())
        {
            MessageBox.Show(this, "Cloud AI is not configured on this build.", "Cave AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CloudPublishWebViewHost.TokenCache.TryGetUsableToken() == null)
        {
            try
            {
                await DesktopAuthWindow.AcquireTokenAsync(this, CloudPublishWebViewHost.TokenCache).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Cave AI", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var caveContext = _selected == null
            ? "No specific reference cave is selected."
            : $"Reference cave: {_selected.Name} ({_selected.Country}, {_selected.Region}). " +
              $"Coordinates: {_selected.Lat:F5}, {_selected.Lon:F5}. Id: {_selected.Id}.";

        var systemPrompt =
            "You are Cave AI, a speleology assistant for CaveAI Pro. Answer briefly in English. " +
            "Prefer catalog facts; say when data is uncertain. " + caveContext;

        CaveAiSendButton.IsEnabled = false;
        CaveAiResponseBox.Text = "Thinking…";
        try
        {
            var body = GeminiProxyClient.BuildChatBody(GeminiProxyClient.DefaultModel, systemPrompt, prompt);
            var raw = await GeminiProxyClient.PostGenerateContentAsync(body).ConfigureAwait(true);
            var text = GeminiProxyClient.ExtractTextFromGenerateContentResponse(raw);
            CaveAiResponseBox.Text = string.IsNullOrWhiteSpace(text)
                ? "Cloud AI returned an empty response. Try rephrasing your question."
                : text;
        }
        catch (Exception ex)
        {
            CaveAiResponseBox.Text = ex.Message;
        }
        finally
        {
            CaveAiSendButton.IsEnabled = true;
        }
    }

    private void OpenCaveAiWebForEntry(ReferenceCaveIndexEntry entry, ReferenceCavePin? detail = null)
    {
        var url = CaveAiWebUrls.BuildFromReference(entry, detail);
        PublicLibraryCatalog.ShowInAppWindow(this, url);
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
        vm.MarkDirty("Reference link updated — Ctrl+S to save");
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

            var url = PublicLibraryCatalog.WithEmbed(ReferenceCatalogShareUrls.BuildShareUrl(_selected));

            PublicLibraryCatalog.ShowInAppWindow(this, url);

            return;

        }



        PublicLibraryCatalog.ShowMapInAppWindow(this);

    }




    private void OpenHydrologyScout_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            MessageBox.Show(this, "Select a reference cave first.", "Hydrology scout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var url = PublicLibraryCatalog.WithEmbed(ReferenceCatalogShareUrls.BuildExploreHydrologyScoutUrl(_selected));
        PublicLibraryCatalog.ShowInAppWindow(this, url);
        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.CatalogExploreHydrologyScoutLink);
    }

    private void OpenExploreTerrain_Click(object sender, RoutedEventArgs e)

    {

        if (_selected == null)

        {

            MessageBox.Show(this, "Select a reference cave first.", "Explore terrain", MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }



        var url = PublicLibraryCatalog.WithEmbed(ReferenceCatalogShareUrls.BuildExploreTerrainUrl(_selected));

        PublicLibraryCatalog.ShowInAppWindow(this, url);

        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.CatalogExploreTerrainLink);

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

