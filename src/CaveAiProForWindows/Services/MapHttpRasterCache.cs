using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Downloads map raster URLs once into LocalApplicationData so Plan/Section underlay and Explorer can use a local file.
/// </summary>
public static class MapHttpRasterCache
{
    private const long MaxDownloadBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> RasterExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".jp2", ".j2k",
    };

    /// <summary>Path extension suggests this is not a raw image (skip HEAD/GET probe).</summary>
    private static readonly HashSet<string> BlockedProbeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".html", ".htm", ".xml", ".json", ".txt", ".zip", ".gz", ".7z", ".rar",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".kml", ".kmz", ".gpx",
        ".js", ".css", ".wasm", ".exe", ".msi", ".apk",
    };

    public static bool IsLikelyRasterHttpUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        var t = StripQueryAndFragment(url.Trim());
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri))
            return false;
        if (!uri.IsAbsoluteUri)
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;
        var path = uri.AbsolutePath;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && RasterExtensions.Contains(ext);
    }

    /// <summary>
    /// True for http(s) URLs we may download for a raster underlay: known image extension, or extensionless/API-style path
    /// (verified via <c>Content-Type: image/*</c> on download).
    /// </summary>
    public static bool IsHttpRasterCacheCandidate(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        var t = url.Trim();
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;
        if (IsLikelyRasterHttpUrl(t))
            return true;
        var path = StripQueryAndFragment(uri.AbsolutePath);
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            return true;
        return !BlockedProbeExtensions.Contains(ext);
    }

    /// <summary>Downloads if missing; returns path to cached file.</summary>
    public static bool TryEnsureCachedFile(string url, out string? localPath, out string? errorMessage)
    {
        localPath = null;
        errorMessage = null;
        var norm = url.Trim();
        var extensionKnown = IsLikelyRasterHttpUrl(norm);
        if (!extensionKnown && !IsHttpRasterCacheCandidate(norm))
        {
            errorMessage = "Not an http(s) URL we can treat as a raster map (unknown path type).";
            return false;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)))[..16];
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaveAiProForWindows",
            "mapurlcache",
            hash);
        Directory.CreateDirectory(dir);

        var fileName = extensionKnown ? SafeFileNameFromUrl(norm) : "image.bin";
        var dest = Path.Combine(dir, fileName);
        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
        {
            localPath = dest;
            return true;
        }

        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All };
            using var client = new HttpClient(handler) { Timeout = HttpTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CaveAiProForWindows/1.0 (map cache; survey backup viewer)");

            using var resp = client.GetAsync(norm, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                errorMessage = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                return false;
            }

            var len = resp.Content.Headers.ContentLength;
            if (len > MaxDownloadBytes)
            {
                errorMessage = $"File too large ({len} bytes; max {MaxDownloadBytes}).";
                return false;
            }

            var media = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!string.IsNullOrEmpty(media))
            {
                var okImage = media.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                var okLoose = media.Contains("octet-stream", StringComparison.OrdinalIgnoreCase) ||
                              media.Contains("binary", StringComparison.OrdinalIgnoreCase);
                if (extensionKnown)
                {
                    if (!okImage && !okLoose)
                    {
                        errorMessage = $"Unexpected Content-Type: {media}";
                        return false;
                    }
                }
                else if (!okImage)
                {
                    errorMessage =
                        $"URL has no image file extension; server returned Content-Type: {media} (expected image/*).";
                    return false;
                }
            }
            else if (!extensionKnown)
            {
                errorMessage = "URL has no image extension and response had no Content-Type; refusing ambiguous download.";
                return false;
            }

            using var read = resp.Content.ReadAsStream();
            using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = read.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += n;
                if (total > MaxDownloadBytes)
                {
                    fs.Close();
                    try
                    {
                        File.Delete(dest);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    errorMessage = $"Download exceeded {MaxDownloadBytes} bytes.";
                    return false;
                }

                fs.Write(buffer, 0, n);
            }

            fs.Flush(true);
            localPath = dest;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            try
            {
                if (File.Exists(dest))
                    File.Delete(dest);
            }
            catch
            {
                /* ignore */
            }

            return false;
        }
    }

    private static string SafeFileNameFromUrl(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
                return "image.bin";
            var leaf = Path.GetFileName(u.AbsolutePath);
            leaf = StripQueryAndFragment(leaf);
            foreach (var c in Path.GetInvalidFileNameChars())
                leaf = leaf.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or "..")
                return "image.bin";
            return leaf.Length > 120 ? leaf[..120] : leaf;
        }
        catch
        {
            return "image.bin";
        }
    }

    private static string StripQueryAndFragment(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        var q = s.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
            s = s[..q];
        var h = s.IndexOf('#', StringComparison.Ordinal);
        if (h >= 0)
            s = s[..h];
        return s;
    }
}
