namespace CaveAiProForWindows.Services;

/// <summary>Guards opening accidentally huge files (browser download glitches, wrong file picked).</summary>
public static class BackupFileSizeLimits
{
    /// <summary>
    /// Maximum size for a single opened .json or .zip backup (512 MiB).
    /// Android CaveAI exports are typically far smaller; this is a safety rail only.
    /// </summary>
    public const long MaxBackupFileBytes = 512L * 1024 * 1024;

    /// <summary>Skip decoding individual map rasters larger than this (128 MiB).</summary>
    public const long MaxRasterDecodeFileBytes = 128L * 1024 * 1024;
}
