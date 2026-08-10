using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.ExpeditionShare;

namespace CaveAiProForWindows.Services.SurfaceMap;

/// <summary>Builds the JSON payload posted from WebView2 host to MapLibre surface-map.html.</summary>
public static class SurfaceMapProjectBridge
{
    public const string VirtualHost = "caveai-surface.local";
    public const string CacheVirtualHost = "caveai-surface-cache.local";

    public static string EntryUri => $"https://{VirtualHost}/index.html";

    public static string BuildProjectMessageJson(
        CaveProjectDocument? project,
        string? zipPath,
        SurfaceMapPersistedState mapState,
        string? cloudProjectJsonStoragePath = null,
        string? cloudAssetCacheDir = null)
    {
        var payload = BuildPayload(project, zipPath, mapState, cloudProjectJsonStoragePath, cloudAssetCacheDir);
        var envelope = new SurfaceMapHostMessage("project", payload);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public static string BuildExpeditionSharesMessageJson(IReadOnlyList<ExpeditionShareDisplay.ExpeditionShareRow> shares)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var features = shares
            .Where(s => ExpeditionShareDisplay.IsVisibleOnMap(s, nowMs))
            .Where(s => s.Lat is >= -90 and <= 90 && s.Lon is >= -180 and <= 180)
            .Select(s =>
            {
                var display = ExpeditionShareDisplay.ComputeDisplayStatus(s, nowMs);
                var status = display == ExpeditionShareDisplay.DisplayStatus.Overdue ? "overdue" : "active";
                return new
                {
                    type = "Feature",
                    geometry = new { type = "Point", coordinates = new[] { s.Lon, s.Lat } },
                    properties = new
                    {
                        id = s.LeaderUid,
                        caveName = s.CaveName,
                        display = status,
                        fill = status == "overdue" ? "#ffb020" : "#ff6b35",
                        leaderDisplayName = ExpeditionShareDisplay.LeaderDisplayName(s),
                    },
                };
            })
            .ToList();

        var payload = new { type = "FeatureCollection", features };
        var envelope = new { type = "expeditionShares", payload };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public static SurfaceMapProjectPayload BuildPayload(
        CaveProjectDocument? project,
        string? zipPath,
        SurfaceMapPersistedState mapState,
        string? cloudProjectJsonStoragePath = null,
        string? cloudAssetCacheDir = null)
    {
        if (project == null)
        {
            return new SurfaceMapProjectPayload
            {
                MapState = ToMapStateDto(mapState),
            };
        }

        float? declination = project.SurveyCalibrationProfile?.MagneticDeclinationAppliedDeg;

        var lidar = TryBuildLidarOverlay(project, zipPath, cloudAssetCacheDir);
        string? lidarHint = null;
        if (lidar == null && project.SurfaceLidarRaster.ValueKind is JsonValueKind.Object)
            lidarHint = DescribeSurfaceLidarAvailability(project);

        var corridor = SurfaceMapCorridorGeometry.TryBuildCorridorDetailed(project);
        var hasEntrance = project.Lat is { } la && la is > -90 and < 90 &&
                          project.Lon is { } lo && lo is > -180 and < 180 &&
                          !(Math.Abs(la) < 1e-12 && Math.Abs(lo) < 1e-12);
        string? emptyHint = null;
        if (!hasEntrance)
        {
            emptyHint = corridor.Corridor != null
                ? "No entrance GPS — corridor is a preview near Athens. Use Entrance GPS / Pick entrance on Surface Map (or lock A1 on Android), then reload."
                : "Set entrance with Entrance GPS / Pick entrance on Surface Map (or lock A1 on Android), then reload.";
        }

        return new SurfaceMapProjectPayload
        {
            Name = project.Name ?? "",
            Lat = project.Lat,
            Lon = project.Lon,
            DeclinationDeg = declination,
            ReturnCar = TryReadReturnPin(project.ReturnCarLat, project.ReturnCarLon),
            ReturnBase = TryReadReturnPin(project.ReturnBaseLat, project.ReturnBaseLon),
            EmptyStateHint = emptyHint,
            SurfaceLidarRaster = lidar,
            LidarStatusHint = lidarHint,
            SurveyCorridor = corridor.Corridor,
            SurveyCorridorProvisional = corridor.IsProvisional ? true : null,
            MapState = ToMapStateDto(mapState),
        };
    }

    private static string DescribeSurfaceLidarAvailability(CaveProjectDocument project)
    {
        var root = project.SurfaceLidarRaster;
        if (root.ValueKind is not JsonValueKind.Object)
            return "No surface LiDAR in project.";

        if (!TryReadDouble(root, "southWestLat", out _) ||
            !TryReadDouble(root, "southWestLon", out _) ||
            !TryReadDouble(root, "northEastLat", out _) ||
            !TryReadDouble(root, "northEastLon", out _))
            return "Surface LiDAR bounds are missing or invalid in project JSON.";

        var imageUri = ReadString(root, "imageUri");
        if (string.IsNullOrWhiteSpace(imageUri))
            return "Surface LiDAR bounds are set but no image URI was found.";

        return "LiDAR image failed to load";
    }

    private static SurfaceMapPinDto? TryReadReturnPin(string? latRaw, string? lonRaw)
    {
        if (!TryParseCoord(latRaw, out var lat) || !TryParseCoord(lonRaw, out var lon))
            return null;
        if (Math.Abs(lat) < 1e-12 && Math.Abs(lon) < 1e-12)
            return null;
        return new SurfaceMapPinDto { Lat = lat, Lon = lon };
    }

    private static bool TryParseCoord(string? raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw.Trim(), "N/A", StringComparison.OrdinalIgnoreCase))
            return false;
        return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

    private static SurfaceMapLidarRasterDto? TryBuildLidarOverlay(
        CaveProjectDocument project,
        string? zipPath,
        string? cloudAssetCacheDir = null)
    {
        var root = project.SurfaceLidarRaster;
        if (root.ValueKind is not JsonValueKind.Object)
            return null;

        if (!TryReadDouble(root, "southWestLat", out var swLat) ||
            !TryReadDouble(root, "southWestLon", out var swLon) ||
            !TryReadDouble(root, "northEastLat", out var neLat) ||
            !TryReadDouble(root, "northEastLon", out var neLon))
            return null;

        if (neLat <= swLat || neLon <= swLon)
            return null;

        var imageUri = ReadString(root, "imageUri") ?? ReadString(root, "imageUrl");
        if (string.IsNullOrWhiteSpace(imageUri))
            return null;

        var opacity = 0.55f;
        if (TryReadDouble(root, "opacity", out var op))
            opacity = (float)Math.Clamp(op, 0, 1);

        string? webUrl = null;
        if (imageUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            imageUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            webUrl = imageUri.Trim();
        }
        else if (TryResolveLocalImage(project, zipPath, imageUri.Trim(), cloudAssetCacheDir, out var local) && local != null)
        {
            webUrl = TryPublishToCacheHost(project, local);
        }
        else if (!string.IsNullOrWhiteSpace(cloudAssetCacheDir))
        {
            var cloudCandidate = Path.Combine(cloudAssetCacheDir, imageUri.TrimStart('/', '\\'));
            if (File.Exists(cloudCandidate))
                webUrl = TryPublishToCacheHost(project, cloudCandidate);
        }

        if (webUrl == null && !string.IsNullOrWhiteSpace(zipPath))
        {
            foreach (var publishedName in new[] { "surface_lidar_overlay.jpg", "surface_lidar_overlay.png", "surface_lidar_overlay.webp" })
            {
                if (TryResolveLocalImage(project, zipPath, publishedName, cloudAssetCacheDir, out var publishedLocal) &&
                    publishedLocal != null)
                {
                    webUrl = TryPublishToCacheHost(project, publishedLocal);
                    if (webUrl != null) break;
                }
            }
        }

        if (webUrl == null)
            return null;
        return new SurfaceMapLidarRasterDto
        {
            ImageUrl = webUrl,
            SouthWestLat = swLat,
            SouthWestLon = swLon,
            NorthEastLat = neLat,
            NorthEastLon = neLon,
            Opacity = opacity,
        };
    }

    private static bool TryResolveLocalImage(
        CaveProjectDocument project,
        string? zipPath,
        string rawUri,
        string? cloudAssetCacheDir,
        out string? localPath)
    {
        localPath = null;
        var jsonDir = string.IsNullOrWhiteSpace(project.LoadedFromFile)
            ? null
            : Path.GetDirectoryName(project.LoadedFromFile);

        if (!string.IsNullOrWhiteSpace(cloudAssetCacheDir))
        {
            var cloudCandidate = Path.Combine(cloudAssetCacheDir, rawUri.TrimStart('/', '\\'));
            if (File.Exists(cloudCandidate))
            {
                localPath = cloudCandidate;
                return true;
            }
        }

        if (MapAssetOpener.TryEnsureLocalFilePath(rawUri, zipPath, out var resolved, out _, jsonDir) &&
            !string.IsNullOrWhiteSpace(resolved) &&
            File.Exists(resolved))
        {
            localPath = resolved;
            return true;
        }

        foreach (var candidate in CaveMapsMarkerPathResolver.EnumerateAbsoluteCandidates(rawUri, project, zipPath, jsonDir))
        {
            if (File.Exists(candidate))
            {
                localPath = candidate;
                return true;
            }
        }

        return false;
    }

    private static string? TryPublishToCacheHost(CaveProjectDocument project, string sourcePath)
    {
        try
        {
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaveAiProForWindows",
                "surface-map-cache");
            var key = SanitizeCacheKey(project.Name, project.LoadedFromFile, sourcePath);
            var folder = Path.Combine(cacheRoot, key);
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";
            var dest = Path.Combine(folder, "lidar" + ext.ToLowerInvariant());
            if (!File.Exists(dest) || new FileInfo(sourcePath).LastWriteTimeUtc > new FileInfo(dest).LastWriteTimeUtc)
                File.Copy(sourcePath, dest, overwrite: true);

            var rel = Path.GetRelativePath(cacheRoot, dest).Replace('\\', '/');
            return $"https://{CacheVirtualHost}/{rel}";
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeCacheKey(string? name, string? loadedFrom, string sourcePath)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "project" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(c, '_');

        var hash = (loadedFrom ?? "") + "|" + sourcePath;
        var shortHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(hash)))[..12];
        return $"{baseName}_{shortHash}";
    }

    private static SurfaceMapMapStateDto ToMapStateDto(SurfaceMapPersistedState s) => new()
    {
        HillshadeEnabled = s.HillshadeEnabled,
        Terrain3dEnabled = s.Terrain3dEnabled,
        CorridorOverlayEnabled = s.CorridorOverlayEnabled,
        CopernicusDsmEnabled = s.CopernicusDsmEnabled,
        LidarOverlayEnabled = s.LidarOverlayEnabled,
        LidarOpacity = s.LidarOpacity,
        EntrancePinEnabled = s.EntrancePinEnabled,
        VehiclePinsEnabled = s.VehiclePinsEnabled,
        CenterLon = s.CenterLon,
        CenterLat = s.CenterLat,
        Zoom = s.Zoom,
        Bearing = s.Bearing,
        Pitch = s.Pitch,
    };

    private static bool TryReadDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var prop))
            return false;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value))
            return double.IsFinite(value);
        if (prop.ValueKind == JsonValueKind.String &&
            double.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return double.IsFinite(value);
        return false;
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record SurfaceMapHostMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] SurfaceMapProjectPayload Payload);

public sealed class SurfaceMapProjectPayload
{
    public string? Name { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public float? DeclinationDeg { get; init; }
    public SurfaceMapLidarRasterDto? SurfaceLidarRaster { get; init; }
    public string? LidarStatusHint { get; init; }
    public SurfaceMapCorridorGeometry.GeoJsonFeatureCollection? SurveyCorridor { get; init; }
    /// <summary>True when corridor was anchored at the default preview location (no entrance GPS).</summary>
    public bool? SurveyCorridorProvisional { get; init; }
    public SurfaceMapPinDto? ReturnCar { get; init; }
    public SurfaceMapPinDto? ReturnBase { get; init; }
    public string? EmptyStateHint { get; init; }
    public SurfaceMapMapStateDto? MapState { get; init; }
}

public sealed class SurfaceMapPinDto
{
    public double Lat { get; init; }
    public double Lon { get; init; }
}

public sealed class SurfaceMapLidarRasterDto
{
    public string? ImageUrl { get; init; }
    public double SouthWestLat { get; init; }
    public double SouthWestLon { get; init; }
    public double NorthEastLat { get; init; }
    public double NorthEastLon { get; init; }
    public float Opacity { get; init; } = 0.55f;
}

public sealed class SurfaceMapMapStateDto
{
    public bool HillshadeEnabled { get; init; } = true;
    public bool Terrain3dEnabled { get; init; }
    public bool CorridorOverlayEnabled { get; init; } = true;
    public bool CopernicusDsmEnabled { get; init; }
    public bool LidarOverlayEnabled { get; init; } = true;
    public float LidarOpacity { get; init; } = 0.55f;
    public bool EntrancePinEnabled { get; init; } = true;
    public bool VehiclePinsEnabled { get; init; } = true;
    public double CenterLon { get; init; }
    public double CenterLat { get; init; }
    public double Zoom { get; init; } = 14;
    public double Bearing { get; init; }
    public double Pitch { get; init; }
}
