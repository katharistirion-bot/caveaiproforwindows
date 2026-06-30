using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.SurfaceMap;

public sealed class SurfaceMapTileCacheService : IDisposable
{
    private const long DefaultMaxBytes = 128L * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _fetchGate = new(4, 4);

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
        _maxBytes = Math.Max(32L * 1024 * 1024, maxBytes);
        Directory.CreateDirectory(_root);
        RebuildSizeIndex();
    }

    public bool Enabled { get; set; } = true;

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
               host.Contains("eox.at", StringComparison.OrdinalIgnoreCase);
    }

    public async Task TryServeOrCacheAsync(CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (!Enabled || _environment == null || string.IsNullOrWhiteSpace(args.Request?.Uri)) return;
        var uri = args.Request.Uri;
        if (!IsCacheableTileUrl(uri)) return;

        var path = PathForUri(uri);
        if (File.Exists(path))
        {
            TouchLru(path);
            args.Response = CreateFileResponse(_environment, path, uri);
            return;
        }

        try
        {
            await _fetchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = await Http.GetByteArrayAsync(uri).ConfigureAwait(false);
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
        catch { }
    }

    private static CoreWebView2WebResourceResponse CreateFileResponse(CoreWebView2Environment env, string path, string uri)
    {
        var stream = File.OpenRead(path);
        return env.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + GuessContentType(uri) + "\r\n");
    }

    private static CoreWebView2WebResourceResponse CreateStreamResponse(CoreWebView2Environment env, byte[] bytes, string uri)
    {
        var stream = new MemoryStream(bytes, writable: false);
        return env.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + GuessContentType(uri) + "\r\n");
    }

    private static string GuessContentType(string uri)
    {
        var lower = uri.ToLowerInvariant();
        if (lower.Contains(".jpg") || lower.Contains(".jpeg")) return "image/jpeg";
        if (lower.Contains(".webp")) return "image/webp";
        return "image/png";
    }

    private string PathForUri(string uri)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri))).ToLowerInvariant();
        var ext = GuessContentType(uri).Contains("jpeg") ? ".jpg" : ".png";
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