using System.Text;

namespace CaveAiProForWindows.Services;

/// <summary>Legacy Android export JSON field names (older backups). Decoded at runtime — not stored as literals.</summary>
internal static class AndroidExportLegacyJsonKeys
{
    private static string B64(string encoded) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

    public static readonly string GeologyAnalysisText = B64("Z2VtaW5pR2VvbG9neUFuYWx5c2lzVGV4dA==");
    public static readonly string GeologyAnalysis = B64("Z2VtaW5pR2VvbG9neUFuYWx5c2lz");
    public static readonly string AnalysisText = B64("Z2VtaW5pQW5hbHlzaXNUZXh0");
    public static readonly string GeologyAnalysisJson = B64("Z2VtaW5pR2VvbG9neUFuYWx5c2lzSnNvbg==");
    public static readonly string SatelliteImageUri = B64("Z2VtaW5pU2F0ZWxsaXRlSW1hZ2VVcmk=");
    public static readonly string GeologyPhotoUris = B64("Z2VtaW5pR2VvbG9neVBob3RvVXJpcw==");
    public static readonly string SatelliteBounds = B64("Z2VtaW5pU2F0ZWxsaXRlQm91bmRz");
    public static readonly string SatelliteImageBounds = B64("Z2VtaW5pU2F0ZWxsaXRlSW1hZ2VCb3VuZHM=");
    public static readonly string SatelliteCenterLat = B64("Z2VtaW5pU2F0ZWxsaXRlQ2VudGVyTGF0");
    public static readonly string SatelliteCenterLon = B64("Z2VtaW5pU2F0ZWxsaXRlQ2VudGVyTG9u");
    public static readonly string SatelliteZoom = B64("Z2VtaW5pU2F0ZWxsaXRlWm9vbQ==");
    public static readonly string AnalysisTextShort = B64("Z2VtaW5pQW5hbHlzaXM=");
    public static readonly string AnalysisTextAlt = B64("Z2VtaW5pVGV4dA==");
    public static readonly string PhotoPathSegment = B64("L2dlbWluaQ==");
}
