using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.ExpeditionShare;
using CaveAiProForWindows.Services.ReferenceCatalog;
using CaveAiProForWindows.Services.SurfaceMap;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Views;

/// <summary>
/// MapLibre GL JS surface map around the cave entrance — WebView2 host for bundled <c>Assets/surface-map</c>.
/// </summary>
public partial class SurfaceMapView : UserControl
{
    private bool _webViewInitialized;
    private bool _mapReady;
    private bool _applyingSettings;
    private bool _fitSurveyOnReady;
    private bool _measureActive;
    private CaveProjectDocument? _pendingProject;
    private string? _pendingZipPath;
    private string? _cloudAssetCacheDir;
    private SurfaceMapTileCacheService? _tileCache;
    private TaskCompletionSource<string?>? _pngExportTcs;
    private TaskCompletionSource<JsonElement>? _exportPackageTcs;
    private readonly ExpeditionShareRepository _expeditionShareRepository = new();
    private DispatcherTimer? _expeditionShareTimer;
    private bool _expeditionShareRefreshRunning;
    private CancellationTokenSource? _offlinePackCts;
    private bool _offlinePackDownloading;

    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(SurfaceMapView),
        new PropertyMetadata(null, OnProjectOrZipChanged));

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(SurfaceMapView),
        new PropertyMetadata(null, OnProjectOrZipChanged));

    public static readonly DependencyProperty CloudAssetCacheDirProperty = DependencyProperty.Register(
        nameof(CloudAssetCacheDir),
        typeof(string),
        typeof(SurfaceMapView),
        new PropertyMetadata(null, OnProjectOrZipChanged));

    public string? CloudAssetCacheDir
    {
        get => (string?)GetValue(CloudAssetCacheDirProperty);
        set => SetValue(CloudAssetCacheDirProperty, value);
    }

    public CaveProjectDocument? Project
    {
        get => (CaveProjectDocument?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public string? ZipPath
    {
        get => (string?)GetValue(ZipPathProperty);
        set => SetValue(ZipPathProperty, value);
    }

    public SurfaceMapView()
    {
        InitializeComponent();
        Loaded += OnLoadedAsync;
        Unloaded += OnUnloaded;
    }

    private static void OnProjectOrZipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SurfaceMapView view)
            view.QueueProjectPush();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        ApplyPersistedUiSettings();
        UpdateTileCacheSizeLabel();
        UpdateDeclinationBadge();
        await EnsureWebViewAsync().ConfigureAwait(true);
        ExpeditionSharePanel.Project = Project;
        UpdateCoordsLine();
        QueueProjectPush();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _expeditionShareTimer?.Stop();
        if (SurfaceWebView?.CoreWebView2 != null)
            SurfaceWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
    }

    private void ApplyPersistedUiSettings()
    {
        var s = AppUiSettingsStore.LoadOrDefault().SurfaceMap;
        _applyingSettings = true;
        try
        {
            HillshadeCheck.IsChecked = s.HillshadeEnabled;
            Terrain3dCheck.IsChecked = s.Terrain3dEnabled;
            CorridorCheck.IsChecked = s.CorridorOverlayEnabled;
            LidarCheck.IsChecked = s.LidarOverlayEnabled;
            CopernicusCheck.IsChecked = s.CopernicusDsmEnabled;
            OfflineCacheCheck.IsChecked = s.OfflineTileCacheEnabled;
            CacheOnlyCheck.IsChecked = s.CacheOnlyMode;
            EntrancePinCheck.IsChecked = s.EntrancePinEnabled;
            VehiclePinsCheck.IsChecked = s.VehiclePinsEnabled;
            LidarOpacitySlider.Value = s.LidarOpacity;
            LidarOpacityLabel.Text = $"{(int)Math.Round(s.LidarOpacity * 100)}%";

            HillshadeCheck.Checked += LayerToggle_Changed;
            HillshadeCheck.Unchecked += LayerToggle_Changed;
            Terrain3dCheck.Checked += LayerToggle_Changed;
            Terrain3dCheck.Unchecked += LayerToggle_Changed;
            CorridorCheck.Checked += LayerToggle_Changed;
            CorridorCheck.Unchecked += LayerToggle_Changed;
            LidarCheck.Checked += LayerToggle_Changed;
            LidarCheck.Unchecked += LayerToggle_Changed;
            CopernicusCheck.Checked += LayerToggle_Changed;
            CopernicusCheck.Unchecked += LayerToggle_Changed;
            EntrancePinCheck.Checked += LayerToggle_Changed;
            EntrancePinCheck.Unchecked += LayerToggle_Changed;
            VehiclePinsCheck.Checked += LayerToggle_Changed;
            VehiclePinsCheck.Unchecked += LayerToggle_Changed;
            OfflineCacheCheck.Checked += OfflineCacheToggle_Changed;
            OfflineCacheCheck.Unchecked += OfflineCacheToggle_Changed;
            CacheOnlyCheck.Checked += CacheOnlyToggle_Changed;
            CacheOnlyCheck.Unchecked += CacheOnlyToggle_Changed;
            PerformanceToggle.Checked += PerformanceToggle_Changed;
            PerformanceToggle.Unchecked += PerformanceToggle_Changed;
            RefreshOfflinePackStatusLabel();
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private async Task EnsureWebViewAsync()
    {
        if (_webViewInitialized)
            return;

        try
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Starting WebView2…";

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaveAiProForWindows",
                "WebView2",
                "SurfaceMap");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await SurfaceWebView.EnsureCoreWebView2Async(environment);

            var core = SurfaceWebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 core is unavailable.");

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsWebMessageEnabled = true;

            var assetsFolder = ResolveSurfaceMapAssetsFolder();
            if (string.IsNullOrEmpty(assetsFolder))
            {
                PlaceholderText.Text = "Surface map assets not found (Assets/surface-map).";
                StatusText.Text = "Assets missing";
                LoadingOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            core.SetVirtualHostNameToFolderMapping(
                SurfaceMapProjectBridge.VirtualHost,
                assetsFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaveAiProForWindows",
                "surface-map-cache");
            Directory.CreateDirectory(cacheRoot);
            core.SetVirtualHostNameToFolderMapping(
                SurfaceMapProjectBridge.CacheVirtualHost,
                cacheRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            core.NavigationStarting += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Uri))
                    return;
                if (!IsAllowedNavigation(args.Uri))
                {
                    args.Cancel = true;
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.Uri)
                        {
                            UseShellExecute = true,
                        });
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
            };

            core.WebMessageReceived += OnWebMessageReceived;

            _tileCache = new SurfaceMapTileCacheService();
            _tileCache.Enabled = AppUiSettingsStore.LoadOrDefault().SurfaceMap.OfflineTileCacheEnabled;
            _tileCache.CacheOnlyMode = AppUiSettingsStore.LoadOrDefault().SurfaceMap.CacheOnlyMode;
            _tileCache.AttachEnvironment(environment);
            foreach (var pattern in new[]
                     {
                         "https://tile.openstreetmap.org/*",

                         "https://s3.amazonaws.com/elevation-tiles-prod/*",
                         "https://tiles.maps.eox.at/*",
                         "https://demotiles.maplibre.org/*",
                     })
            {
                // MapLibre loads tiles via fetch/XHR, not only classic <img> — intercept All.
                core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All);
            }
            core.WebResourceRequested += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    if (_tileCache != null && _tileCache.Enabled)
                        await _tileCache.TryServeOrCacheAsync(args).ConfigureAwait(true);
                }
                catch
                {
                    /* cache miss / offline — let MapLibre retry or show blank tile */
                }
                finally
                {
                    deferral.Complete();
                }
            };

            core.PermissionRequested += (_, args) =>
            {
                if (args.PermissionKind == CoreWebView2PermissionKind.Geolocation)
                {
                    args.State = CoreWebView2PermissionState.Allow;
                }
            };

            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    PlaceholderText.Text = UserFacingErrors.SurfaceMapNavigationFailed();
                    StatusText.Text = "Map did not load";
                    OfflineBanner.Visibility = Visibility.Visible;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            };

            core.Navigate(SurfaceMapProjectBridge.EntryUri);
            PlaceholderText.Visibility = Visibility.Collapsed;
            _webViewInitialized = true;
            StatusText.Text = "Loading map…";
            LoadingText.Text = "Loading map tiles…";
        }
        catch (Exception)
        {
            PlaceholderText.Text = UserFacingErrors.SurfaceMapWebViewFailed();
            StatusText.Text = "WebView2 unavailable";
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private static string? ResolveSurfaceMapAssetsFolder()
    {
        var candidate = AppContentPaths.Assets("surface-map");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static bool IsAllowedNavigation(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return false;

        if (string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(u.Host, SurfaceMapProjectBridge.VirtualHost, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(u.Host, SurfaceMapProjectBridge.CacheVirtualHost, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            var host = u.Host;
            return host.Contains("openstreetmap.org", StringComparison.OrdinalIgnoreCase) ||
                   host.Contains("wmflabs.org", StringComparison.OrdinalIgnoreCase) ||
                   host.Contains("wikimedia.org", StringComparison.OrdinalIgnoreCase) ||
                   host.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
                   host.Contains("eox.at", StringComparison.OrdinalIgnoreCase) ||
                   host.Contains("arcgisonline.com", StringComparison.OrdinalIgnoreCase) ||
                   host.Contains("maptiler.com", StringComparison.OrdinalIgnoreCase) ||
                   host.Contains("unpkg.com", StringComparison.OrdinalIgnoreCase) ||
                   host.Contains("jsdelivr.net", StringComparison.OrdinalIgnoreCase) ||
                   host.Contains("maplibre.org", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json))
                return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl))
                return;
            var type = typeEl.GetString() ?? "";

            Dispatcher.Invoke(() =>
            {
                switch (type)
                {
                    case "ready":
                        _mapReady = true;
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        StatusText.Text = "Map ready";
                        PlaceholderText.Visibility = Visibility.Collapsed;
                        UpdateDeclinationBadge();
                        FlushPendingProject();
                        StartExpeditionSharePolling();
                        if (!NetworkInterface.GetIsNetworkAvailable())
                        {
                            OfflineBanner.Visibility = Visibility.Visible;
                            ApplyOfflineFallbackMode("Offline at startup — cache-only + performance mode");
                        }
                        RefreshOfflinePackStatusLabel();
                        if (!_fitSurveyOnReady)
                        {
                            _fitSurveyOnReady = true;
                            var sv = AppUiSettingsStore.LoadOrDefault().SurfaceMap;
                            var hasEntrance = Project?.Lat is { } ela && Project.Lon is { } elo
                                && ela is > -90 and < 90 && elo is > -180 and < 180
                                && !(Math.Abs(ela) < 1e-12 && Math.Abs(elo) < 1e-12);
                            var needsFit = !hasEntrance
                                || sv.Zoom < 4
                                || (Math.Abs(sv.CenterLat) < 1e-6 && Math.Abs(sv.CenterLon) < 1e-6);
                            if (needsFit)
                                SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson("""{"type":"fitSurvey"}""");
                        }
                        break;
                    case "mapError":
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        OfflineBanner.Visibility = Visibility.Visible;
                        PlaceholderText.Visibility = Visibility.Visible;
                        PlaceholderText.Text = root.TryGetProperty("error", out var mapErrEl)
                            ? (mapErrEl.GetString() ?? "Map failed to load")
                            : "Map failed to load";
                        StatusText.Text = "Map error";
                        break;
                    case "mapViewport":
                        if (root.TryGetProperty("zoom", out var zEl) && zEl.TryGetDouble(out var zv))
                            ZoomText.Text = $"Zoom {zv:F1}";
                        break;
                    case "cursorCoords":
                        if (root.TryGetProperty("lat", out var cla) && cla.TryGetDouble(out var clat) &&
                            root.TryGetProperty("lon", out var clo) && clo.TryGetDouble(out var clon))
                            CursorText.Text = $"Cursor: {clat:F5}°, {clon:F5}°";
                        else
                            CursorText.Text = "Cursor: —";
                        break;
                    case "mapState":
                        PersistMapState(root);
                        break;
                    case "status":
                        if (root.TryGetProperty("message", out var msg))
                        {
                            var text = msg.GetString() ?? StatusText.Text;
                            StatusText.Text = text;
                            if (text.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("error", StringComparison.OrdinalIgnoreCase))
                            {
                                OfflineBanner.Visibility = Visibility.Visible;
                            }
                        }
                        break;
                    case "exportPngResult":
                        if (root.TryGetProperty("dataUrl", out var dataUrlEl))
                            _pngExportTcs?.TrySetResult(dataUrlEl.GetString());
                        else if (root.TryGetProperty("error", out var errEl))
                            _pngExportTcs?.TrySetException(new InvalidOperationException(errEl.GetString() ?? "Export failed"));
                        else
                            _pngExportTcs?.TrySetResult(null);
                        break;
                    case "exportPackageResult":
                        _exportPackageTcs?.TrySetResult(root);
                        break;
                    case "measureResult":
                        if (root.TryGetProperty("label", out var ml) && !string.IsNullOrWhiteSpace(ml.GetString()))
                        {
                            MeasureText.Text = "Measure: " + ml.GetString();
                            MeasureText.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            MeasureText.Visibility = Visibility.Collapsed;
                        }
                        break;
                    case "coordsCopy":
                        if (root.TryGetProperty("text", out var ct))
                        {
                            try
                            {
                                Clipboard.SetText(ct.GetString() ?? "");
                                StatusText.Text = "Coordinates copied";
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }
                        break;
                    case "pinPlaced":
                        if (Project != null &&
                            root.TryGetProperty("kind", out var pk) &&
                            root.TryGetProperty("lat", out var pla) && pla.TryGetDouble(out var plat) &&
                            root.TryGetProperty("lon", out var plo) && plo.TryGetDouble(out var plon))
                        {
                            var kind = pk.GetString() ?? "";
                            var ls = plat.ToString("F6", CultureInfo.InvariantCulture);
                            var los = plon.ToString("F6", CultureInfo.InvariantCulture);
                            if (string.Equals(kind, "vehicle", StringComparison.OrdinalIgnoreCase))
                            {
                                Project.ReturnCarLat = ls;
                                Project.ReturnCarLon = los;
                            }
                            else if (string.Equals(kind, "base", StringComparison.OrdinalIgnoreCase))
                            {
                                Project.ReturnBaseLat = ls;
                                Project.ReturnBaseLon = los;
                            }
                            else if (string.Equals(kind, "entrance", StringComparison.OrdinalIgnoreCase))
                            {
                                Project.Lat = plat;
                                Project.Lon = plon;
                                UpdateCoordsLine();
                            }
                            QueueProjectPush();
                            MarkProjectDirty(
                                string.Equals(kind, "entrance", StringComparison.OrdinalIgnoreCase)
                                    ? "Entrance locked from map — Ctrl+S to save"
                                    : "Pin updated — Ctrl+S to save project");
                        }
                        break;
                    case "mapOffline":
                        OfflineBanner.Visibility = Visibility.Visible;
                        ApplyOfflineFallbackMode("Offline — cache-only + performance mode");
                        break;
                    case "elevationProfile":
                        ApplyElevationProfile(root);
                        break;
                }
            });
        }
        catch
        {
            /* ignore malformed messages */
        }
    }

    private void PersistMapState(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload))
            return;

        var all = AppUiSettingsStore.LoadOrDefault();
        var s = all.SurfaceMap;

        if (payload.TryGetProperty("hillshadeEnabled", out var hs) &&
            hs.ValueKind is JsonValueKind.True or JsonValueKind.False)
            s.HillshadeEnabled = hs.GetBoolean();
        if (payload.TryGetProperty("terrain3dEnabled", out var t3) &&
            t3.ValueKind is JsonValueKind.True or JsonValueKind.False)
            s.Terrain3dEnabled = t3.GetBoolean();
        if (payload.TryGetProperty("corridorOverlayEnabled", out var co) &&
            co.ValueKind is JsonValueKind.True or JsonValueKind.False)
            s.CorridorOverlayEnabled = co.GetBoolean();
        if (payload.TryGetProperty("copernicusDsmEnabled", out var cd) &&
            cd.ValueKind is JsonValueKind.True or JsonValueKind.False)
            s.CopernicusDsmEnabled = cd.GetBoolean();
        if (payload.TryGetProperty("lidarOverlayEnabled", out var lo) &&
            lo.ValueKind is JsonValueKind.True or JsonValueKind.False)
            s.LidarOverlayEnabled = lo.GetBoolean();
        if (payload.TryGetProperty("centerLat", out var clat) && clat.TryGetDouble(out var lat))
        {
            s.CenterLat = lat;
            UpdateOfflineCoverageHint(lat, s.CenterLon);
        }
        if (payload.TryGetProperty("centerLon", out var clon) && clon.TryGetDouble(out var lon))
        {
            s.CenterLon = lon;
            UpdateOfflineCoverageHint(s.CenterLat, lon);
        }
        if (payload.TryGetProperty("zoom", out var zoom) && zoom.TryGetDouble(out var z))
        {
            s.Zoom = z;
            ZoomText.Text = $"Zoom {z:F1}";
        }
        if (payload.TryGetProperty("bearing", out var br) && br.TryGetDouble(out var b))
            s.Bearing = b;
        if (payload.TryGetProperty("pitch", out var pi) && pi.TryGetDouble(out var p))
            s.Pitch = p;

        AppUiSettingsStore.Save(all);

        if (!_applyingSettings)
            SyncLayerCheckboxesFromSettings(s);
    }

    private void SyncLayerCheckboxesFromSettings(SurfaceMapPersistedState s)
    {
        _applyingSettings = true;
        try
        {
            HillshadeCheck.IsChecked = s.HillshadeEnabled;
            Terrain3dCheck.IsChecked = s.Terrain3dEnabled;
            CorridorCheck.IsChecked = s.CorridorOverlayEnabled;
            CopernicusCheck.IsChecked = s.CopernicusDsmEnabled;
            LidarCheck.IsChecked = s.LidarOverlayEnabled;
            EntrancePinCheck.IsChecked = s.EntrancePinEnabled;
            VehiclePinsCheck.IsChecked = s.VehiclePinsEnabled;
            LidarOpacitySlider.Value = s.LidarOpacity;
            LidarOpacityLabel.Text = $"{(int)Math.Round(s.LidarOpacity * 100)}%";
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    public void ReloadLayersFromSettings()
    {
        SyncLayerCheckboxesFromSettings(AppUiSettingsStore.LoadOrDefault().SurfaceMap);
        PushLayerStateToMap();
    }

    private void PushLayerStateToMap()
    {
        if (!_mapReady)
            return;

        var s = AppUiSettingsStore.LoadOrDefault().SurfaceMap;
        var msg = JsonSerializer.Serialize(new
        {
            type = "layers",
            payload = new
            {
                performanceMode = PerformanceToggle.IsChecked == true,
                hillshadeEnabled = s.HillshadeEnabled,
                terrain3dEnabled = s.Terrain3dEnabled,
                corridorOverlayEnabled = s.CorridorOverlayEnabled,
                copernicusDsmEnabled = s.CopernicusDsmEnabled,
                lidarOverlayEnabled = s.LidarOverlayEnabled,
                lidarOpacity = s.LidarOpacity,
                entrancePinEnabled = s.EntrancePinEnabled,
                vehiclePinsEnabled = s.VehiclePinsEnabled,
            },
        });
        SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson(msg);
    }

    private void ElevationCollapseToggle_Click(object sender, RoutedEventArgs e)
    {
        var expanded = ElevationCollapseToggle.IsChecked == true;
        ElevationPanelBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ElevationCollapseToggle.Content = expanded ? "▾" : "▸";
        ElevationCollapseToggle.ToolTip = expanded ? "Collapse elevation profile" : "Expand elevation profile";
    }

    private void ApplyElevationProfile(JsonElement root)
    {
        if (!root.TryGetProperty("ready", out var readyEl) || !readyEl.GetBoolean())
        {
            ElevationPanel.Visibility = Visibility.Collapsed;
            if (root.TryGetProperty("statusText", out var failStatus))
                ElevationStatusText.Text = failStatus.GetString() ?? "Elevation unavailable";
            return;
        }

        if (!root.TryGetProperty("distancesM", out var distEl) ||
            !root.TryGetProperty("elevationsM", out var elevEl))
        {
            ElevationPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var distances = distEl.EnumerateArray().Select(e => e.GetDouble()).ToList();
        var elevations = elevEl.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Null ? double.NaN : e.GetDouble())
            .Select(SanitizeElevationM)
            .ToList();
        if (distances.Count < 2 || elevations.Count(e => e is { } v && !double.IsNaN(v)) < 2)
        {
            ElevationPanel.Visibility = Visibility.Collapsed;
            ElevationStatusText.Text = root.TryGetProperty("statusText", out var st)
                ? st.GetString() ?? "Elevation unavailable"
                : "Elevation unavailable";
            return;
        }

        ElevationPanel.Visibility = Visibility.Visible;
        DrawElevationChart(distances, elevations);

        if (root.TryGetProperty("statusText", out var statusEl))
            ElevationStatusText.Text = statusEl.GetString() ?? "—";
        else
        {
            var valid = elevations.Where(e => e is { } v && !double.IsNaN(v)).Select(e => e!.Value).ToList();
            if (valid.Count >= 2)
            {
                var maxD = distances[^1];
                ElevationStatusText.Text =
                    $"Distance {(maxD / 1000).ToString("F2", CultureInfo.InvariantCulture)} km · elevation {valid.Min():F0}–{valid.Max():F0} m";
            }
        }
    }

    private static double? SanitizeElevationM(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return null;
        if (value is < -20000 or > 20000)
            return null;
        if (value is < -500 or > 9000)
            return null;
        return value;
    }

    private void DrawElevationChart(IReadOnlyList<double> distancesM, IReadOnlyList<double?> elevationsM)
    {
        ElevationChartCanvas.Children.Clear();
        var w = Math.Max(200, ElevationChartCanvas.ActualWidth > 0 ? ElevationChartCanvas.ActualWidth : 640);
        var h = ElevationChartCanvas.Height;
        ElevationChartCanvas.Width = w;

        var maxD = distancesM[^1];
        if (maxD < 10)
            return;

        var validElev = elevationsM
            .Select(e => e is double d ? SanitizeElevationM(d) : null)
            .Where(e => e.HasValue)
            .Select(e => e!.Value)
            .ToList();
        if (validElev.Count < 2)
            return;

        var minE = validElev.Min();
        var maxE = validElev.Max();
        var padE = Math.Max(5, (maxE - minE) * 0.1);
        var e0 = minE - padE;
        var e1 = maxE + padE;

        ElevationChartCanvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = 8, Y1 = h - 12, X2 = w - 8, Y2 = h - 12,
            Stroke = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3d)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        });

        var polyline = new System.Windows.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xaa)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        var started = false;
        for (var i = 0; i < elevationsM.Count; i++)
        {
            if (elevationsM[i] is not { } evv)
                continue;
            var x = 8 + (distancesM[i] / maxD) * (w - 16);
            var y = h - 12 - ((evv - e0) / (e1 - e0)) * (h - 24);
            if (!started)
            {
                polyline.Points.Add(new Point(x, y));
                started = true;
            }
            else
            {
                polyline.Points.Add(new Point(x, y));
            }
        }

        if (polyline.Points.Count >= 2)
            ElevationChartCanvas.Children.Add(polyline);
    }

    private void QueueProjectPush()
    {
        _pendingProject = Project;
        _pendingZipPath = ZipPath;
        _cloudAssetCacheDir = CloudAssetCacheDir;
        ExpeditionSharePanel.Project = Project;
        UpdateCoordsLine();
        UpdateDeclinationBadge();
        if (_mapReady)
            FlushPendingProject();
    }

    private void FlushPendingProject()
    {
        var core = SurfaceWebView?.CoreWebView2;
        if (core == null || !_mapReady)
            return;

        var settings = AppUiSettingsStore.LoadOrDefault().SurfaceMap;
        if (!_applyingSettings)
        {
            settings.HillshadeEnabled = HillshadeCheck.IsChecked == true;
            settings.Terrain3dEnabled = Terrain3dCheck.IsChecked == true;
            settings.CorridorOverlayEnabled = CorridorCheck.IsChecked == true;
            settings.CopernicusDsmEnabled = CopernicusCheck.IsChecked == true;
            settings.LidarOverlayEnabled = LidarCheck.IsChecked == true;
            settings.EntrancePinEnabled = EntrancePinCheck.IsChecked == true;
            settings.VehiclePinsEnabled = VehiclePinsCheck.IsChecked == true;
            settings.LidarOpacity = (float)LidarOpacitySlider.Value;
        }

        var mapState = _pendingProject == null
            ? WorkspaceSessionReset.FreshSurfaceMapState()
            : settings;

        var json = SurfaceMapProjectBridge.BuildProjectMessageJson(
            _pendingProject,
            _pendingZipPath,
            mapState,
            cloudAssetCacheDir: _cloudAssetCacheDir);
        core.PostWebMessageAsJson(json);
    }

    private void StartExpeditionSharePolling()
    {
        _expeditionShareTimer?.Stop();
        _expeditionShareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
        _expeditionShareTimer.Tick += (_, _) => _ = RefreshExpeditionSharesAsync();
        _expeditionShareTimer.Start();
        _ = RefreshExpeditionSharesAsync();
    }

    private async Task RefreshExpeditionSharesAsync()
    {
        if (_expeditionShareRefreshRunning || !_mapReady)
            return;
        var core = SurfaceWebView?.CoreWebView2;
        if (core == null)
            return;

        _expeditionShareRefreshRunning = true;
        try
        {
            var shares = await _expeditionShareRepository.LoadVisibleActiveSharesAsync().ConfigureAwait(true);
            var json = SurfaceMapProjectBridge.BuildExpeditionSharesMessageJson(shares);
            core.PostWebMessageAsJson(json);
        }
        catch
        {
            // optional layer — ignore when signed out or unsubscribed
        }
        finally
        {
            _expeditionShareRefreshRunning = false;
        }
    }

    /// <summary>Clears WebView overlays and host UI when the workspace is unloaded or replaced.</summary>
    public void ResetForProjectUnload()
    {
        _pendingProject = null;
        _pendingZipPath = null;
        _cloudAssetCacheDir = null;
        _fitSurveyOnReady = false;
        _measureActive = false;
        if (MeasureToggle != null)
            MeasureToggle.IsChecked = false;
        if (MeasureText != null)
            MeasureText.Visibility = Visibility.Collapsed;
        if (ElevationChartCanvas != null)
            ElevationChartCanvas.Children.Clear();
        ExpeditionSharePanel.Project = Project;
        UpdateCoordsLine();
        UpdateDeclinationBadge();
        PushEmptyProjectToMap();
    }

    private void PushEmptyProjectToMap()
    {
        var core = SurfaceWebView?.CoreWebView2;
        if (core == null || !_mapReady)
            return;

        var json = SurfaceMapProjectBridge.BuildProjectMessageJson(
            null,
            null,
            WorkspaceSessionReset.FreshSurfaceMapState());
        core.PostWebMessageAsJson(json);
    }

    private void UpdateCoordsLine()
    {
        var p = Project;
        if (p?.Lat is { } lat && p.Lon is { } lon && lat is > -90 and < 90 && lon is > -180 and < 180)
        {
            CoordsText.Text = $"{p.Name}: entrance {lat:F5}°, {lon:F5}°";
            EntranceHintText.Visibility = Visibility.Collapsed;
            EntranceHintPanel.Visibility = Visibility.Collapsed;
            return;
        }

        CoordsText.Text = p == null
            ? "No project loaded — open a backup with lat/lon on the active project."
            : $"{p.Name}: no entrance coordinates in project JSON.";
        EntranceHintText.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
        EntranceHintPanel.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
        EntranceHintText.Text =
            "Use Entrance GPS or Pick entrance on this map (or lock A1 on Android), then Ctrl+S to save.";
    }

    private void PreviewGreeceMap_Click(object sender, RoutedEventArgs e) =>
        SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson("""{"type":"fitSurvey"}""");

    private void UpdateDeclinationBadge()
    {
        var decl = Project?.SurveyCalibrationProfile?.MagneticDeclinationAppliedDeg;
        if (decl is { } d && Math.Abs(d) > 0.01f)
        {
            DeclinationBadge.Visibility = Visibility.Visible;
            DeclinationText.Text = $"Mag. decl. {Math.Abs(d):F1}° {(d >= 0 ? "E" : "W")}";
        }
        else
        {
            DeclinationBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void OfflineCacheToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings) return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.SurfaceMap.OfflineTileCacheEnabled = OfflineCacheCheck.IsChecked == true;
        AppUiSettingsStore.Save(all);
        if (_tileCache != null)
            _tileCache.Enabled = all.SurfaceMap.OfflineTileCacheEnabled;
    }

    private void CacheOnlyToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings) return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.SurfaceMap.CacheOnlyMode = CacheOnlyCheck.IsChecked == true;
        AppUiSettingsStore.Save(all);
        if (_tileCache != null)
            _tileCache.CacheOnlyMode = all.SurfaceMap.CacheOnlyMode;
        StatusText.Text = all.SurfaceMap.CacheOnlyMode
            ? "Cache-only mode — network tile fetches disabled"
            : "Network tile fetches allowed when cache misses";
    }

    private void LayerToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings)
            return;

        var all = AppUiSettingsStore.LoadOrDefault();
        all.SurfaceMap.HillshadeEnabled = HillshadeCheck.IsChecked == true;
        all.SurfaceMap.Terrain3dEnabled = Terrain3dCheck.IsChecked == true;
        all.SurfaceMap.CorridorOverlayEnabled = CorridorCheck.IsChecked == true;
        all.SurfaceMap.CopernicusDsmEnabled = CopernicusCheck.IsChecked == true;
        all.SurfaceMap.LidarOverlayEnabled = LidarCheck.IsChecked == true;
        all.SurfaceMap.EntrancePinEnabled = EntrancePinCheck.IsChecked == true;
        all.SurfaceMap.VehiclePinsEnabled = VehiclePinsCheck.IsChecked == true;
        all.SurfaceMap.LidarOpacity = (float)LidarOpacitySlider.Value;
        AppUiSettingsStore.Save(all);
        PushLayerStateToMap();
    }

    private void PerformanceToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingSettings) return;
        PushLayerStateToMap();
    }

    private void LidarOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_applyingSettings || LidarOpacityLabel == null) return;
        LidarOpacityLabel.Text = $"{(int)Math.Round(e.NewValue * 100)}%";
        var all = AppUiSettingsStore.LoadOrDefault();
        all.SurfaceMap.LidarOpacity = (float)e.NewValue;
        AppUiSettingsStore.Save(all);
        if (_mapReady)
        {
            SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "lidarOpacity",
                payload = new { opacity = e.NewValue },
            }));
        }
    }

    private void MeasureToggle_Click(object sender, RoutedEventArgs e)
    {
        _measureActive = MeasureToggle.IsChecked == true;
        if (_mapReady)
        {
            SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "measure",
                payload = new { active = _measureActive },
            }));
        }
        if (!_measureActive)
            MeasureText.Visibility = Visibility.Collapsed;
    }

    private void CopyCoords_Click(object sender, RoutedEventArgs e)
    {
        if (CopyCoordsButton.ContextMenu == null) return;
        CopyCoordsButton.ContextMenu.PlacementTarget = CopyCoordsButton;
        CopyCoordsButton.ContextMenu.IsOpen = true;
    }

    private void CopyCoordsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string src && _mapReady)
        {
            SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "getCoords",
                payload = new { source = src },
            }));
        }
    }

    private void Directions_Click(object sender, RoutedEventArgs e)
    {
        if (DirectionsButton.ContextMenu == null) return;
        DirectionsButton.ContextMenu.PlacementTarget = DirectionsButton;
        DirectionsButton.ContextMenu.IsOpen = true;
    }

    private void DirectionsGoogle_Click(object sender, RoutedEventArgs e)
    {
        if (Project?.Lat is { } lat && Project.Lon is { } lon)
        {
            var url = $"https://www.google.com/maps/dir/?api=1&destination={lat.ToString("F6", CultureInfo.InvariantCulture)},{lon.ToString("F6", CultureInfo.InvariantCulture)}&travelmode=driving";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private void DirectionsOsm_Click(object sender, RoutedEventArgs e)
    {
        if (Project?.Lat is { } lat && Project.Lon is { } lon)
        {
            var url = $"https://www.openstreetmap.org/directions?to={lat.ToString("F6", CultureInfo.InvariantCulture)}%2C{lon.ToString("F6", CultureInfo.InvariantCulture)}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private async void ExportPackage_Click(object sender, RoutedEventArgs e)
    {
        if (!_mapReady || SurfaceWebView?.CoreWebView2 == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "ZIP|*.zip",
            FileName = $"{Project?.Name ?? "surface"}_handoff.zip",
        };
        if (dlg.ShowDialog() != true) return;

        _exportPackageTcs = new TaskCompletionSource<JsonElement>();
        SurfaceWebView.CoreWebView2.PostWebMessageAsJson("""{"type":"exportPackage"}""");
        try
        {
            using var exportCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var root = await _exportPackageTcs.Task.WaitAsync(exportCts.Token).ConfigureAwait(true);
            var url = root.GetProperty("dataUrl").GetString()!;
            var png = Convert.FromBase64String(url["data:image/png;base64,".Length..]);
            var geo = root.TryGetProperty("geoJson", out var g) ? g.GetRawText() : "{}";
            var gpx = root.TryGetProperty("gpx", out var gx) ? gx.GetString() ?? "" : "";
            var td = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(td);
            await File.WriteAllBytesAsync(Path.Combine(td, "surface.png"), png);
            await File.WriteAllTextAsync(Path.Combine(td, "corridor.geojson"), geo);
            await File.WriteAllTextAsync(Path.Combine(td, "track.gpx"), gpx);
            if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
            ZipFile.CreateFromDirectory(td, dlg.FileName);
            Directory.Delete(td, true);
            StatusText.Text = "Package exported";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Export failed: " + ex.Message;
        }
        finally
        {
            _exportPackageTcs = null;
        }
    }

    private void FitSurvey_Click(object sender, RoutedEventArgs e) =>
        SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson("""{"type":"fitSurvey"}""");

    private void LayersToggle_Click(object sender, RoutedEventArgs e) =>
        LayersPanel.Visibility = LayersToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void SetEntranceGps_Click(object sender, RoutedEventArgs e) =>
        _ = PlacePinFromHostGpsAsync("entrance");

    private void PickEntranceMap_Click(object sender, RoutedEventArgs e) =>
        SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson("""{"type":"pinPick","payload":{"kind":"entrance"}}""");

    private void SetVehicleGps_Click(object sender, RoutedEventArgs e) =>
        _ = PlacePinFromHostGpsAsync("vehicle");

    private void PickVehicleMap_Click(object sender, RoutedEventArgs e) =>
        SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson("""{"type":"pinPick","payload":{"kind":"vehicle"}}""");

    private void SetBaseGps_Click(object sender, RoutedEventArgs e) =>
        _ = PlacePinFromHostGpsAsync("base");

    private void PickBaseMap_Click(object sender, RoutedEventArgs e) =>
        SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson("""{"type":"pinPick","payload":{"kind":"base"}}""");

    private async void LocateMe_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Reading Windows GPS…";
        var result = await ReferenceCatalogGeolocation.TryGetDeviceLocationAsync().ConfigureAwait(true);
        if (!result.Ok || result.Origin is null)
        {
            StatusText.Text = result.Error ?? "Live location unavailable.";
            return;
        }
        if (!string.IsNullOrWhiteSpace(result.Warning))
            StatusText.Text = result.Warning;
        else
            StatusText.Text = "Centered on my location";
        PostHostLocation(result.Origin.Lat, result.Origin.Lon, kind: "locate", zoom: 18);
    }

    private async Task PlacePinFromHostGpsAsync(string kind)
    {
        StatusText.Text = "Reading Windows GPS…";
        var result = await ReferenceCatalogGeolocation.TryGetDeviceLocationAsync().ConfigureAwait(true);
        if (!result.Ok || result.Origin is null)
        {
            StatusText.Text = result.Error ?? "GPS fix failed — pick on map instead";
            return;
        }
        PostHostLocation(result.Origin.Lat, result.Origin.Lon, kind: kind, zoom: 17);
        if (Project != null)
        {
            var ls = result.Origin.Lat.ToString("F6", CultureInfo.InvariantCulture);
            var los = result.Origin.Lon.ToString("F6", CultureInfo.InvariantCulture);
            if (string.Equals(kind, "vehicle", StringComparison.OrdinalIgnoreCase))
            {
                Project.ReturnCarLat = ls;
                Project.ReturnCarLon = los;
            }
            else if (string.Equals(kind, "base", StringComparison.OrdinalIgnoreCase))
            {
                Project.ReturnBaseLat = ls;
                Project.ReturnBaseLon = los;
            }
            else if (string.Equals(kind, "entrance", StringComparison.OrdinalIgnoreCase))
            {
                Project.Lat = result.Origin.Lat;
                Project.Lon = result.Origin.Lon;
                UpdateCoordsLine();
            }
            QueueProjectPush();
            MarkProjectDirty(
                kind switch
                {
                    "entrance" => "Entrance locked from GPS — Ctrl+S to save",
                    "vehicle" => "Vehicle park pin set from GPS — Ctrl+S to save",
                    _ => "Trailhead pin set from GPS — Ctrl+S to save",
                });
        }
        else
        {
            StatusText.Text = "Pin set on map (no project loaded)";
        }
    }

    private void PostHostLocation(double lat, double lon, string kind, double zoom)
    {
        if (SurfaceWebView?.CoreWebView2 == null) return;
        var json = JsonSerializer.Serialize(new
        {
            type = "hostLocation",
            payload = new { lat, lon, kind, zoom },
        });
        SurfaceWebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void MarkProjectDirty(string status)
    {
        StatusText.Text = status;
        if (Window.GetWindow(this)?.DataContext is ViewModels.MainViewModel vm)
            vm.MarkDirty(status);
    }

    private static bool IsValidMapCoord(double lat, double lon) =>
        lat is > -90 and < 90 && lon is > -180 and < 180 &&
        !(Math.Abs(lat) < 1e-12 && Math.Abs(lon) < 1e-12);

    private void ApplyOfflineFallbackMode(string statusMessage)
    {
        if (CacheOnlyCheck.IsChecked != true)
        {
            _applyingSettings = true;
            try
            {
                CacheOnlyCheck.IsChecked = true;
            }
            finally
            {
                _applyingSettings = false;
            }
            if (_tileCache != null)
                _tileCache.CacheOnlyMode = true;
            var all = AppUiSettingsStore.LoadOrDefault();
            all.SurfaceMap.CacheOnlyMode = true;
            AppUiSettingsStore.Save(all);
        }
        if (PerformanceToggle.IsChecked != true)
        {
            PerformanceToggle.IsChecked = true;
            PushLayerStateToMap();
        }
        StatusText.Text = statusMessage;
    }

    private void UpdateOfflineCoverageHint(double lat, double lon)
    {
        if (_offlinePackDownloading || OfflinePackStatusText == null)
            return;
        var meta = SurfaceMapOfflinePack.Read();
        if (meta == null)
        {
            RefreshOfflinePackStatusLabel();
            return;
        }
        if (!IsValidMapCoord(lat, lon))
        {
            RefreshOfflinePackStatusLabel();
            return;
        }
        OfflinePackStatusText.Text = meta.Covers(lat, lon)
            ? "Offline pack: " + meta.Label()
            : "Offline pack: " + meta.Label() + " — map center outside downloaded area";
    }

    private void RefreshOfflinePackStatusLabel()
    {
        if (OfflinePackStatusText == null)
            return;
        if (_offlinePackDownloading)
            return;
        var meta = SurfaceMapOfflinePack.Read();
        if (meta == null)
        {
            OfflinePackStatusText.Text = "Offline pack: none yet — download while online before field use";
            return;
        }
        var sv = AppUiSettingsStore.LoadOrDefault().SurfaceMap;
        if (IsValidMapCoord(sv.CenterLat, sv.CenterLon) && !meta.Covers(sv.CenterLat, sv.CenterLon))
        {
            OfflinePackStatusText.Text = "Offline pack: " + meta.Label() + " — map center outside downloaded area";
            return;
        }
        OfflinePackStatusText.Text = "Offline pack: " + meta.Label();
    }

    private void SetOfflineDownloadUiBusy(bool busy)
    {
        _offlinePackDownloading = busy;
        if (DownloadOfflineAreaButton != null)
            DownloadOfflineAreaButton.IsEnabled = !busy;
        if (CancelOfflineDownloadButton != null)
            CancelOfflineDownloadButton.IsEnabled = busy;
    }

    private void CancelOfflineDownload_Click(object sender, RoutedEventArgs e)
    {
        if (!_offlinePackDownloading)
            return;
        _offlinePackCts?.Cancel();
        StatusText.Text = "Cancelling offline download…";
    }

    private async void DownloadOfflineArea_Click(object sender, RoutedEventArgs e)
    {
        if (_tileCache == null)
        {
            StatusText.Text = "Tile cache not ready";
            return;
        }
        if (_offlinePackDownloading)
            return;

        double? lat = null;
        double? lon = null;
        // Prefer survey entrance, then device GPS, then last map center.
        if (Project?.Lat is { } pla && Project.Lon is { } plo && IsValidMapCoord(pla, plo))
        {
            lat = pla;
            lon = plo;
        }
        else
        {
            var gps = await ReferenceCatalogGeolocation.TryGetDeviceLocationAsync().ConfigureAwait(true);
            if (gps.Ok && gps.Origin != null)
            {
                lat = gps.Origin.Lat;
                lon = gps.Origin.Lon;
            }
        }

        if (lat is null || lon is null)
        {
            var sv = AppUiSettingsStore.LoadOrDefault().SurfaceMap;
            if (IsValidMapCoord(sv.CenterLat, sv.CenterLon))
            {
                lat = sv.CenterLat;
                lon = sv.CenterLon;
            }
        }

        if (lat is null || lon is null)
        {
            StatusText.Text = "Need GPS, entrance, or map center to download an offline area";
            MessageBox.Show(
                Window.GetWindow(this),
                "Set an entrance, get a GPS fix, or pan the map first, then download again.",
                "Download offline area",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var (south, west, north, east) = SurfaceMapOfflinePack.BoundingBoxAround(lat.Value, lon.Value);
        _offlinePackCts?.Cancel();
        _offlinePackCts = new CancellationTokenSource();
        SetOfflineDownloadUiBusy(true);
        StatusText.Text = "Downloading offline OSM + hillshade + DEM…";
        OfflinePackStatusText.Text = "Offline pack: downloading…";
        try
        {
            var progress = new Progress<(int Done, int Total, int Zoom)>(p =>
            {
                StatusText.Text = $"Offline tiles {p.Done}/{p.Total} (z{p.Zoom})";
                OfflinePackStatusText.Text = $"Offline pack: {p.Done}/{p.Total}";
            });
            var result = await _tileCache.PrefetchOsmAreaAsync(
                south, west, north, east,
                SurfaceMapOfflinePack.ZoomMin,
                SurfaceMapOfflinePack.ZoomMax,
                progress,
                _offlinePackCts.Token).ConfigureAwait(true);
            SurfaceMapOfflinePack.Write(south, west, north, east);
            RefreshOfflinePackStatusLabel();
            UpdateTileCacheSizeLabel();
            StatusText.Text = result.Fail == 0
                ? $"Offline area ready ({result.Ok} tiles) — enable Cache only for airplane mode"
                : $"Offline pack saved with {result.Fail} tile errors ({result.Ok} ok)";
            if (!_tileCache.CacheOnlyMode)
            {
                CacheOnlyCheck.IsChecked = true;
                var all = AppUiSettingsStore.LoadOrDefault();
                all.SurfaceMap.CacheOnlyMode = true;
                AppUiSettingsStore.Save(all);
                _tileCache.CacheOnlyMode = true;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Offline download cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Offline download failed: " + ex.Message;
        }
        finally
        {
            SetOfflineDownloadUiBusy(false);
            RefreshOfflinePackStatusLabel();
        }
    }

    private void FitEntrance_Click(object sender, RoutedEventArgs e) =>
        SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson("""{"type":"fitEntrance"}""");

    private async void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (!_mapReady || SurfaceWebView?.CoreWebView2 == null)
        {
            StatusText.Text = "Map not ready for export";
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export surface map PNG",
            Filter = "PNG image|*.png",
            FileName = $"{Project?.Name ?? "surface-map"}_surface.png",
        };
        if (dlg.ShowDialog() != true) return;

        _pngExportTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        SurfaceWebView.CoreWebView2.PostWebMessageAsJson("""{"type":"exportPng"}""");
        try
        {
            using var exportCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var dataUrl = await _pngExportTcs.Task.WaitAsync(exportCts.Token).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid PNG export data");

            var b64 = dataUrl["data:image/png;base64,".Length..];
            var bytes = Convert.FromBase64String(b64);
            await File.WriteAllBytesAsync(dlg.FileName, bytes).ConfigureAwait(true);
            StatusText.Text = "PNG exported";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Export failed: " + ex.Message;
        }
        finally
        {
            _pngExportTcs = null;
        }
    }

    private async void ReloadMap_Click(object sender, RoutedEventArgs e)
    {
        _mapReady = false;
        _fitSurveyOnReady = false;
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingText.Text = "Reloading map…";
        StatusText.Text = "Reloading…";
        OfflineBanner.Visibility = Visibility.Collapsed;
        if (SurfaceWebView?.CoreWebView2 != null)
            SurfaceWebView.CoreWebView2.Reload();
        else
            await EnsureWebViewAsync().ConfigureAwait(true);
    }

    private void UpdateTileCacheSizeLabel()
    {
        if (TileCacheSizeText == null)
            return;
        var bytes = _tileCache?.CurrentBytes ?? 0;
        var maxBytes = _tileCache?.MaxBytes ?? 128L * 1024 * 1024;
        TileCacheSizeText.Text = $"Cache: {SurfaceMapTileCacheService.FormatBytes(bytes)} / {SurfaceMapTileCacheService.FormatBytes(maxBytes)}";
    }

    private void ClearTileCache_Click(object sender, RoutedEventArgs e)
    {
        _tileCache?.ClearAll();
        SurfaceMapOfflinePack.Clear();
        RefreshOfflinePackStatusLabel();
        UpdateTileCacheSizeLabel();
        StatusText.Text = "Tile cache cleared";
    }

    private void OpenSurfaceMapInBrowser_Click(object sender, RoutedEventArgs e)
    {
        // Virtual host (caveai-surface.local) is not usable in an external browser — open Explore instead.
        string url;
        if (Project?.Lat is { } la && Project.Lon is { } lo
            && la is > -90 and < 90 && lo is > -180 and < 180
            && !(Math.Abs(la) < 1e-12 && Math.Abs(lo) < 1e-12))
        {
            url = PublicLibraryCatalog.WithEmbed(
                PublicLibraryCatalog.WebExploreMapUrl
                + $"&lat={la.ToString(CultureInfo.InvariantCulture)}"
                + $"&lon={lo.ToString(CultureInfo.InvariantCulture)}"
                + "&zoom=15&preset=terrain");
        }
        else
        {
            url = PublicLibraryCatalog.WebExploreMapUrlEmbedded;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open in browser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
