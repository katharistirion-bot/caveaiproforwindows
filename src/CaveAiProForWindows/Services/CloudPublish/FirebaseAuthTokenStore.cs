using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Services.Secrets;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Persists the last Firebase ID token under LocalApplicationData (DPAPI-encrypted).
/// Legacy plain <c>auth-token.json</c> files are migrated on first read.
/// </summary>
public static class FirebaseAuthTokenStore
{
    private const string StoreDirEnv = "CAVEAI_AUTH_TOKEN_STORE_DIR";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private static string StoreDirectory =>
        Environment.GetEnvironmentVariable(StoreDirEnv)
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaveAiProForWindows");

    private static string EncryptedStorePath => Path.Combine(StoreDirectory, "auth-token.dat");

    private static string LegacyPlainStorePath => Path.Combine(StoreDirectory, "auth-token.json");

    public static FirebaseIdToken? TryLoad()
    {
        try
        {
            if (TryLoadFromEncrypted(out var fromEncrypted))
                return fromEncrypted;

            if (TryLoadFromLegacyPlain(out var fromLegacy))
            {
                if (fromLegacy != null)
                    TrySave(fromLegacy);
                else
                    TryDeleteLegacyPlain();
                return fromLegacy;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static void TrySave(FirebaseIdToken token)
    {
        try
        {
            // Preserve an existing refresh token so Update()/silent refresh do not wipe desktop re-auth.
            var existingRefresh = TryLoadRefreshToken();
            if (!string.IsNullOrWhiteSpace(existingRefresh))
            {
                SaveWithRefreshToken(token, existingRefresh!);
                return;
            }

            var dir = StoreDirectory;
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var dto = new StoredTokenDto { Raw = token.Raw };
            var json = JsonSerializer.Serialize(dto, JsonOpts);
            var protectedBytes = UserScopedDpapiProtector.ProtectUtf8(json);
            File.WriteAllBytes(EncryptedStorePath, protectedBytes);
            TryDeleteLegacyPlain();
        }
        catch
        {
            /* best-effort */
        }
    }

    public static void TryClear()
    {
        try
        {
            if (File.Exists(EncryptedStorePath))
                File.Delete(EncryptedStorePath);
            TryDeleteLegacyPlain();
        }
        catch
        {
            /* best-effort */
        }
    }

    private static bool TryLoadFromEncrypted(out FirebaseIdToken? token)
    {
        token = null;
        if (!File.Exists(EncryptedStorePath))
            return false;

        var protectedBytes = File.ReadAllBytes(EncryptedStorePath);
        var json = UserScopedDpapiProtector.UnprotectUtf8(protectedBytes);
        if (string.IsNullOrWhiteSpace(json))
            return false;

        token = ParseStoredToken(json);
        return true;
    }

    private static bool TryLoadFromLegacyPlain(out FirebaseIdToken? token)
    {
        token = null;
        if (!File.Exists(LegacyPlainStorePath))
            return false;

        var json = File.ReadAllText(LegacyPlainStorePath);
        token = ParseStoredToken(json);
        return true;
    }

    private static FirebaseIdToken? ParseStoredToken(string json)
    {
        var dto = JsonSerializer.Deserialize<StoredTokenDto>(json, JsonOpts);
        if (dto == null || string.IsNullOrWhiteSpace(dto.Raw))
            return null;

        if (!FirebaseIdTokenParser.TryParse(dto.Raw, out var parsed) || parsed == null)
            return null;

        return parsed.IsUsable() ? parsed : null;
    }

    private static void TryDeleteLegacyPlain()
    {
        try
        {
            if (File.Exists(LegacyPlainStorePath))
                File.Delete(LegacyPlainStorePath);
        }
        catch
        {
            /* best-effort */
        }
    }

    /// <summary>Returns the persisted refresh token (regardless of whether the ID token is still usable).</summary>
    public static string? TryLoadRefreshToken()
    {
        try
        {
            if (File.Exists(EncryptedStorePath))
            {
                var protectedBytes = File.ReadAllBytes(EncryptedStorePath);
                var json = UserScopedDpapiProtector.UnprotectUtf8(protectedBytes);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var dto = JsonSerializer.Deserialize<StoredTokenDto>(json, JsonOpts);
                    if (!string.IsNullOrWhiteSpace(dto?.RefreshToken))
                        return dto.RefreshToken;
                }
            }

            // Encrypted store may exist without a refresh token (silent ID-token refresh).
            // Still read legacy auth-token.json so Gemini/silent refresh cannot wipe it.
            return TryLoadRefreshTokenFromLegacy();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryLoadRefreshTokenFromLegacy()
    {
        if (!File.Exists(LegacyPlainStorePath))
            return null;
        try
        {
            var json = File.ReadAllText(LegacyPlainStorePath);
            var dto = JsonSerializer.Deserialize<StoredTokenDto>(json, JsonOpts);
            return dto?.RefreshToken;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveWithRefreshToken(FirebaseIdToken token, string refreshToken)
    {
        try
        {
            var dir = StoreDirectory;
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var dto = new StoredTokenDto { Raw = token.Raw, RefreshToken = refreshToken };
            var json = JsonSerializer.Serialize(dto, JsonOpts);
            var protectedBytes = UserScopedDpapiProtector.ProtectUtf8(json);
            File.WriteAllBytes(EncryptedStorePath, protectedBytes);
            TryDeleteLegacyPlain();
        }
        catch
        {
            /* best-effort */
        }
    }

    private sealed class StoredTokenDto
    {
        public string? Raw { get; set; }
        public string? RefreshToken { get; set; }
    }
}
