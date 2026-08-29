using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.SurfaceMap;

public sealed class SurfaceMapTileCacheService : IDisposable
{
    private const long DefaultMaxBytes = 900L * 1024 * 1024;
    private static readonly HttpClient Http;
    private readonly SemaphoreSlim _fetchGate = new(4, 4);

    static SurfaceMapTileCacheService()
    {
        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        Http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "CaveAiProForWindows/1.0 (tile cache; https://www.caveaipro.com)");
    }

    private readonly string _root;
    private readonly long _maxBytes;
    private readonly ConcurrentDictionary<string, byte> _lru = new(StringComparer.Ordinal);
    private readonly object _sizeLock = new();
    private long _currentBytes;
    private CoreWebView2Environment? _environment;

    public SurfaceMapTileCacheService(string? root = null, long maxBytes = DefaultMaxBytes)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaveAiProForWindows",
            "surface-tile-cache");
        _maxBytes = Math.Max(64L * 1024 * 1024, maxBytes);
        Directory.CreateDirectory(_root);
        RebuildSizeIndex();
    }

    public bool Enabled { get; set; } = true;

    /// <summary>When true, cache misses do not hit the network (field / airplane mode).</summary>
    public bool CacheOnlyMode { get; set; }

    public long CurrentBytes => _currentBytes;

    public long MaxBytes => _maxBytes;

    public void AttachEnvironment(CoreWebView2Environment environment) => _environment = environment;

    public void ClearAll()
    {
        lock (_sizeLock)
        {
            _currentBytes = 0;
            _lru.Clear();
        }

        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
            Directory.CreateDirectory(_root);
        }
        catch
        {
            /* ignore */
        }
    }

    public static bool IsCacheableTileUrl(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
        if (!string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;
        var host = u.Host;
        return host.Contains("openstreetmap.org", StringComparison.OrdinalIgnoreCase) ||
               host.Contains("wmflabs.org", StringComparison.OrdinalIgnoreCase) ||
               host.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
               host.Contains("eox.at", StringComparison.OrdinalIgnoreCase) ||
               host.Contains("demotiles.maplibre.org", StringComparison.OrdinalIgnoreCase);
    }

    public async Task TryServeOrCacheAsync(CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (!Enabled || _environment == null || string.IsNullOrWhiteSpace(args.Request?.Uri)) return;
        var uri = args.Request.Uri;
        if (!IsCacheableTileUrl(uri)) return;

        var path = PathForUri(uri);
        if (File.Exists(path))
        {
            if (!TryOpenValidCacheFile(path, uri, out var cached))
            {
                try { File.Delete(path); } catch { /* ignore poison */ }
            }
            else
            {
                TouchLru(path);
                args.Response = CreateStreamResponse(_environment, cached, uri);
                return;
            }
        }

        if (CacheOnlyMode)
        {
            if (TryCacheMissPlaceholder(uri, cacheOnly: true, out var status, out var body, out var contentType))
                args.Response = CreateStreamResponse(_environment, body, status, contentType);
            return;
        }

        try
        {
            await _fetchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = await Http.GetByteArrayAsync(uri).ConfigureAwait(false);
                if (!IsValidCachedPayload(uri, bytes))
                    throw new InvalidDataException("Rejected non-tile payload for " + uri);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                RegisterFile(path, bytes.LongLength);
                EvictIfNeeded();
                args.Response = CreateStreamResponse(_environment, bytes, uri);
            }
            finally
            {
                _fetchGate.Release();
            }
        }
        catch
        {
            // Image tiles: 1x1 PNG. Glyphs/protobuf: do not substitute PNG (WebView crash).
            if (_environment != null
                && TryCacheMissPlaceholder(uri, cacheOnly: false, out var status, out var body, out var contentType))
                args.Response = CreateStreamResponse(_environment, body, status, contentType);
        }
    }

    /// <summary>
    /// OSM MAPNIK + hillshade + Terrarium DEM + MapLibre glyph ranges for a bbox
    /// into the same disk cache the WebView interceptor uses.
    /// </summary>
    public async Task<(int Ok, int Fail, int Total)> PrefetchOsmAreaAsync(
        double south,
        double west,
        double north,
        double east,
        int zoomMin,
        int zoomMax,
        IProgress<(int Done, int Total, int Zoom)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var urls = BuildOfflinePackUrls(south, west, north, east, zoomMin, zoomMax);
        var total = urls.Count;
        var ok = 0;
        var fail = 0;
        var done = 0;
        foreach (var uri in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = PathForUri(uri);
            if (File.Exists(path))
            {
                TouchLru(path);
                ok++;
            }
            else
            {
                try
                {
                    await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                        req.Headers.TryAddWithoutValidation("User-Agent", "CaveAiProForWindows/1.0 (offline pack; contact caveaipro.com)");
                        using var res = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                        res.EnsureSuccessStatusCode();
                        var bytes = await res.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                        if (!IsValidCachedPayload(uri, bytes))
                        {
                            fail++;
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                            RegisterFile(path, bytes.LongLength);
                            EvictIfNeeded();
                            ok++;
                        }
                    }
                    finally
                    {
                        _fetchGate.Release();
                    }
                }
                catch
                {
                    fail++;
                }

                // Be polite to public tile servers on actual network fetches.
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
            }

            done++;
            if (done % 8 == 0 || done == total)
            {
                var zGuess = zoomMin;
                try
                {
                    var parts = uri.Split('/');
                    if (parts.Length >= 2 && int.TryParse(parts[^3], out var zz))
                        zGuess = zz;
                }
                catch { }
                progress?.Report((done, total, zGuess));
            }
        }

        return (ok, fail, total);
    }

    /// <summary>OSM zMin–zMax, hillshade up to z15, Terrarium DEM up to z13, plus Open Sans glyph ranges.</summary>
    public static List<string> BuildOfflinePackUrls(
        double south,
        double west,
        double north,
        double east,
        int zoomMin,
        int zoomMax)
    {
        var urls = new List<string>();
        AppendXyzTiles(urls, "https://tile.openstreetmap.org/{z}/{x}/{y}.png", south, west, north, east, zoomMin, zoomMax);
        AppendXyzTiles(urls, "https://tiles.maps.eox.at/wmts/1.0.0/terrain-light/default/GoogleMapsCompatible/{z}/{y}/{x}.jpg", south, west, north, east, zoomMin, Math.Min(zoomMax, 15));
        AppendXyzTiles(urls, "https://tiles.maps.eox.at/wmts/1.0.0/copernicus_dsm_glo30/default/GoogleMapsCompatible/{z}/{y}/{x}.png", south, west, north, east, zoomMin, Math.Min(zoomMax, 15));
        AppendXyzTiles(urls, "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png", south, west, north, east, zoomMin, Math.Min(zoomMax, 13));
        urls.AddRange(GlyphPrefetchUrls);
        return urls;
    }

    private static readonly string[] GlyphPrefetchUrls =
    [
        "https://demotiles.maplibre.org/font/Open%20Sans%20Regular/0-255.pbf",
        "https://demotiles.maplibre.org/font/Open%20Sans%20Regular/256-511.pbf",
        "https://demotiles.maplibre.org/font/Open%20Sans%20Bold/0-255.pbf",
    ];

    private static void AppendXyzTiles(
        List<string> urls,
        string template,
        double south,
        double west,
        double north,
        double east,
        int zoomMin,
        int zoomMax)
    {
        for (var z = zoomMin; z <= zoomMax; z++)
        {
            var xMin = LonToTileX(west, z);
            var xMax = LonToTileX(east, z);
            var yMin = LatToTileY(north, z);
            var yMax = LatToTileY(south, z);
            if (xMin > xMax) (xMin, xMax) = (xMax, xMin);
            if (yMin > yMax) (yMin, yMax) = (yMax, yMin);
            for (var x = xMin; x <= xMax; x++)
            for (var y = yMin; y <= yMax; y++)
                urls.Add(template.Replace("{z}", z.ToString(CultureInfo.InvariantCulture))
                    .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
                    .Replace("{y}", y.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static readonly byte[] EmptyPng =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    };

    private static int LonToTileX(double lon, int z)
    {
        var n = 1 << z;
        var x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        return Math.Clamp(x, 0, n - 1);
    }

    private static int LatToTileY(double lat, int z)
    {
        var latClamped = Math.Clamp(lat, -85.05112878, 85.05112878);
        var latRad = latClamped * Math.PI / 180.0;
        var n = 1 << z;
        var y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        return Math.Clamp(y, 0, n - 1);
    }

    private static CoreWebView2WebResourceResponse CreateFileResponse(CoreWebView2Environment env, string path, string uri)
    {
        var stream = File.OpenRead(path);
        return env.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + GuessContentType(uri) + "\r\n");
    }

    private static CoreWebView2WebResourceResponse CreateStreamResponse(CoreWebView2Environment env, byte[] bytes, string uri)
    {
        return CreateStreamResponse(env, bytes, 200, GuessContentType(uri));
    }

    private static CoreWebView2WebResourceResponse CreateStreamResponse(
        CoreWebView2Environment env,
        byte[] bytes,
        int status,
        string contentType)
    {
        var stream = new MemoryStream(bytes, writable: false);
        var reason = status == 200 ? "OK" : "Not Found";
        return env.CreateWebResourceResponse(stream, status, reason, "Content-Type: " + contentType + "\r\n");
    }

    /// <summary>
    /// Fallback when a tile is missing. Image tiles get a 1x1 PNG so MapLibre keeps painting.
    /// Glyph/protobuf URLs must never receive PNG bytes — MapLibre parses them as PBF and the
    /// WebView page crashes.
    /// Returns false when the request should fall through to the network.
    /// </summary>
    public static bool TryCacheMissPlaceholder(
        string uri,
        bool cacheOnly,
        out int statusCode,
        out byte[] body,
        out string contentType)
    {
        contentType = GuessContentType(uri);
        var substitutableImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && !IsDemTileUrl(uri);
        if (substitutableImage)
        {
            statusCode = 200;
            body = EmptyPng;
            return true;
        }

        if (cacheOnly)
        {
            statusCode = 404;
            body = [];
            return true;
        }

        statusCode = 0;
        body = [];
        return false;
    }

    public static bool IsDemTileUrl(string uri)
    {
        var lower = uri.ToLowerInvariant();
        return lower.Contains("terrarium", StringComparison.Ordinal)
            || lower.Contains("elevation-tiles-prod", StringComparison.Ordinal);
    }

    public static bool IsValidCachedPayload(string uri, byte[] bytes)
    {
        if (bytes == null || bytes.Length < 8)
            return false;
        if (bytes[0] is (byte)'<' or (byte)'{')
            return false;

        var ct = GuessContentType(uri);
        if (ct.Contains("protobuf", StringComparison.OrdinalIgnoreCase))
        {
            // Old misses cached EmptyPng (67 bytes, PNG magic) as .pbf and crash MapLibre.
            if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return false;
            return bytes.Length >= 16;
        }
        if (ct.Contains("jpeg", StringComparison.OrdinalIgnoreCase))
            return bytes[0] == 0xFF && bytes[1] == 0xD8;
        if (ct.Contains("webp", StringComparison.OrdinalIgnoreCase))
            return bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I';
        if (ct.Contains("png", StringComparison.OrdinalIgnoreCase) || ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
                return false;
            // 1x1 EmptyPng is a valid PNG but poison for Terrarium DEM.
            if (IsDemTileUrl(uri) && bytes.Length < 100)
                return false;
            return true;
        }

        return true;
    }

    private static bool TryOpenValidCacheFile(string path, string uri, out byte[] bytes)
    {
        bytes = [];
        try
        {
            bytes = File.ReadAllBytes(path);
            return IsValidCachedPayload(uri, bytes);
        }
        catch
        {
            return false;
        }
    }

    public static string GuessContentType(string uri)
    {
        var lower = uri.ToLowerInvariant();
        if (lower.Contains(".pbf")) return "application/x-protobuf";
        if (lower.Contains(".json")) return "application/json";
        if (lower.Contains(".jpg") || lower.Contains(".jpeg")) return "image/jpeg";
        if (lower.Contains(".webp")) return "image/webp";
        if (lower.Contains(".css")) return "text/css";
        return "image/png";
    }

    private string PathForUri(string uri)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri))).ToLowerInvariant();
        var ct = GuessContentType(uri);
        var ext = ct.Contains("jpeg") ? ".jpg"
            : ct.Contains("protobuf") ? ".pbf"
            : ct.Contains("json") ? ".json"
            : ct.Contains("css") ? ".css"
            : ".png";
        return Path.Combine(_root, hash[..2], hash + ext);
    }

    private void RebuildSizeIndex()
    {
        long total = 0;
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            total += new FileInfo(file).Length;
            _lru[file] = 0;
        }
        _currentBytes = total;
    }

    private void RegisterFile(string path, long bytes)
    {
        _lru[path] = 0;
        lock (_sizeLock) { _currentBytes += bytes; }
    }

    private void TouchLru(string path)
    {
        _lru.TryRemove(path, out _);
        _lru[path] = 0;
    }

    private void EvictIfNeeded()
    {
        lock (_sizeLock)
        {
            while (_currentBytes > _maxBytes && _lru.Count > 0)
            {
                var oldest = _lru.Keys.FirstOrDefault();
                if (oldest == null) break;
                if (!_lru.TryRemove(oldest, out _)) continue;
                try
                {
                    if (!File.Exists(oldest)) continue;
                    var len = new FileInfo(oldest).Length;
                    File.Delete(oldest);
                    _currentBytes -= len;
                }
                catch { }
            }
        }
    }

    public void Dispose() { }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
