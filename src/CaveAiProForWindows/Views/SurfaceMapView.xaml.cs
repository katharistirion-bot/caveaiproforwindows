using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
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
    private CaveProjectDocument? _pendingProject;
    private string? _pendingZipPath;
    private string? _cloudAssetCacheDir;
    private SurfaceMapTileCacheService? _tileCache;
    private TaskCompletionSource<string?>? _pngExportTcs;

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
        await EnsureWebViewAsync().ConfigureAwait(true);
        UpdateCoordsLine();
        QueueProjectPush();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
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
            OfflineCacheCheck.Checked += OfflineCacheToggle_Changed;
            OfflineCacheCheck.Unchecked += OfflineCacheToggle_Changed;
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
            _tileCache.AttachEnvironment(environment);
            foreach (var pattern in new[]
                     {
                         "https://tile.openstreetmap.org/*",
                         "https://tiles.wmflabs.org/*",
                         "https://s3.amazonaws.com/elevation-tiles-prod/*",
                         "https://tiles.maps.eox.at/*",
                     })
            {
                core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.Image);
            }
            core.WebResourceRequested += async (_, args) =>
            {
                if (_tileCache != null && _tileCache.Enabled)
                    await _tileCache.TryServeOrCacheAsync(args).ConfigureAwait(false);
            };

            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    PlaceholderText.Text = "Surface map failed to load.";
                    StatusText.Text = "Navigation error";
                }
            };

            core.Navigate(SurfaceMapProjectBridge.EntryUri);
            PlaceholderText.Visibility = Visibility.Collapsed;
            _webViewInitialized = true;
            StatusText.Text = "Loading map…";
        }
        catch (Exception ex)
        {
            PlaceholderText.Text = "WebView2 could not start. Install Microsoft Edge WebView2 Runtime.";
            StatusText.Text = ex.Message;
        }
    }

    private static string? ResolveSurfaceMapAssetsFolder()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "Assets", "surface-map");
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
                        StatusText.Text = "Map ready";
                        PlaceholderText.Visibility = Visibility.Collapsed;
                        FlushPendingProject();
                        break;
                    case "mapState":
                        PersistMapState(root);
                        break;
                    case "status":
                        if (root.TryGetProperty("message", out var msg))
                            StatusText.Text = msg.GetString() ?? StatusText.Text;
                        break;
                    case "exportPngResult":
                        if (root.TryGetProperty("dataUrl", out var dataUrlEl))
                            _pngExportTcs?.TrySetResult(dataUrlEl.GetString());
                        else if (root.TryGetProperty("error", out var errEl))
                            _pngExportTcs?.TrySetException(new InvalidOperationException(errEl.GetString() ?? "Export failed"));
                        else
                            _pngExportTcs?.TrySetResult(null);
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
        if (payload.TryGetProperty("centerLon", out var clon) && clon.TryGetDouble(out var lon))
            s.CenterLon = lon;
        if (payload.TryGetProperty("centerLat", out var clat) && clat.TryGetDouble(out var lat))
            s.CenterLat = lat;
        if (payload.TryGetProperty("zoom", out var zoom) && zoom.TryGetDouble(out var z))
            s.Zoom = z;
        if (payload.TryGetProperty("bearing", out var br) && br.TryGetDouble(out var b))
            s.Bearing = b;
        if (payload.TryGetProperty("pitch", out var pi) && pi.TryGetDouble(out var p))
            s.Pitch = p;

        AppUiSettingsStore.Save(all);
    }

    private void QueueProjectPush()
    {
        _pendingProject = Project;
        _pendingZipPath = ZipPath;
        _cloudAssetCacheDir = CloudAssetCacheDir;
        UpdateCoordsLine();
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
        }

        var json = SurfaceMapProjectBridge.BuildProjectMessageJson(
            _pendingProject,
            _pendingZipPath,
            settings,
            cloudAssetCacheDir: _cloudAssetCacheDir);
        core.PostWebMessageAsJson(json);
    }

    private void UpdateCoordsLine()
    {
        var p = Project;
        if (p?.Lat is { } lat && p.Lon is { } lon && lat is > -90 and < 90 && lon is > -180 and < 180)
        {
            CoordsText.Text = $"{p.Name}: entrance {lat:F5}°, {lon:F5}°";
            EntranceHintText.Visibility = Visibility.Collapsed;
            return;
        }

        CoordsText.Text = p == null
            ? "No project loaded — open a backup with lat/lon on the active project."
            : $"{p.Name}: no entrance coordinates in project JSON.";
        EntranceHintText.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
        EntranceHintText.Text =
            "Set entrance lat/lon in project settings (or lock A1 GPS on Android), then reload the surface map.";
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
        AppUiSettingsStore.Save(all);

        if (_mapReady)
        {
            var msg = JsonSerializer.Serialize(new
            {
                type = "layers",
                payload = new
                {
                    hillshadeEnabled = all.SurfaceMap.HillshadeEnabled,
                    terrain3dEnabled = all.SurfaceMap.Terrain3dEnabled,
                    corridorOverlayEnabled = all.SurfaceMap.CorridorOverlayEnabled,
                    copernicusDsmEnabled = all.SurfaceMap.CopernicusDsmEnabled,
                    lidarOverlayEnabled = all.SurfaceMap.LidarOverlayEnabled,
                },
            });
            SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson(msg);
        }
    }

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
            var dataUrl = await _pngExportTcs.Task.ConfigureAwait(true);
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

    private void FitEntrance_Click(object sender, RoutedEventArgs e)
    {
        SurfaceWebView?.CoreWebView2?.PostWebMessageAsJson("""{"type":"fitEntrance"}""");
    }

    private async void ReloadMap_Click(object sender, RoutedEventArgs e)
    {
        _mapReady = false;
        StatusText.Text = "Reloading…";
        if (SurfaceWebView?.CoreWebView2 != null)
            SurfaceWebView.CoreWebView2.Reload();
        else
            await EnsureWebViewAsync().ConfigureAwait(true);
    }
}
