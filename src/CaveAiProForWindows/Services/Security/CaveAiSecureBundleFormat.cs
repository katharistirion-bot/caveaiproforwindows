namespace CaveAiProForWindows.Services.Security;

/// <summary>
/// Custom offline bundle format: AES-256-GCM encrypted ZIP payload with PBKDF2 key derivation.
/// File extension: <c>.caveai</c>
/// </summary>
public static class CaveAiSecureBundleFormat
{
    public const string FileExtension = ".caveai";

    /// <summary>8-byte magic + version byte.</summary>
    public static ReadOnlySpan<byte> Magic => "CAVEAI01"u8;

    public const byte FormatVersion = 1;

    public const int SaltBytes = 16;

    public const int NonceBytes = 12;

    public const int TagBytes = 16;

    public const int Pbkdf2Iterations = 120_000;

    public const int KeyBytes = 32;

    /// <summary>Inner ZIP must contain at least <c>data.json</c>.</summary>
    public const string PrimaryEntryName = "data.json";

    public const string ManifestEntryName = "bundle_manifest.json";
}
