using System.Text;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class FirebaseAuthTokenStoreTests
{
    private string? _tempDir;
    private string? _prevStoreDirEnv;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "caveai-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _prevStoreDirEnv = Environment.GetEnvironmentVariable("CAVEAI_AUTH_TOKEN_STORE_DIR");
        Environment.SetEnvironmentVariable("CAVEAI_AUTH_TOKEN_STORE_DIR", _tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("CAVEAI_AUTH_TOKEN_STORE_DIR", _prevStoreDirEnv);
        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void TrySave_and_TryLoad_roundtrip_encrypted()
    {
        var token = MakeUsableTestToken();
        FirebaseAuthTokenStore.TrySave(token);

        var loaded = FirebaseAuthTokenStore.TryLoad();
        Assert.IsNotNull(loaded);
        Assert.AreEqual(token.Raw, loaded!.Raw);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir!, "auth-token.dat")));
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir!, "auth-token.json")));
    }

    [TestMethod]
    public void TryLoad_migrates_legacy_plain_json_and_deletes_it()
    {
        var token = MakeUsableTestToken();
        var legacyPath = Path.Combine(_tempDir!, "auth-token.json");
        File.WriteAllText(legacyPath, $"{{\"Raw\":\"{token.Raw}\"}}");

        var loaded = FirebaseAuthTokenStore.TryLoad();
        Assert.IsNotNull(loaded);
        Assert.AreEqual(token.Raw, loaded!.Raw);
        Assert.IsFalse(File.Exists(legacyPath), "Legacy plain file should be removed after migration.");
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir!, "auth-token.dat")));
    }

    [TestMethod]
    public void Encrypted_file_is_not_plaintext_jwt()
    {
        var token = MakeUsableTestToken();
        FirebaseAuthTokenStore.TrySave(token);

        var bytes = File.ReadAllBytes(Path.Combine(_tempDir!, "auth-token.dat"));
        var asText = Encoding.UTF8.GetString(bytes);
        Assert.IsFalse(asText.Contains(token.Raw, StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryClear_removes_encrypted_and_legacy_files()
    {
        var token = MakeUsableTestToken();
        FirebaseAuthTokenStore.TrySave(token);
        File.WriteAllText(Path.Combine(_tempDir!, "auth-token.json"), "{}");

        FirebaseAuthTokenStore.TryClear();

        Assert.IsNull(FirebaseAuthTokenStore.TryLoad());
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir!, "auth-token.dat")));
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir!, "auth-token.json")));
    }

    private static FirebaseIdToken MakeUsableTestToken()
    {
        var exp = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payloadJson = $"{{\"sub\":\"test-user\",\"exp\":{exp},\"iat\":{iat}}}";
        var payloadB64 = Base64UrlEncode(payloadJson);
        var jwt = $"header.{payloadB64}.sig";

        Assert.IsTrue(FirebaseIdTokenParser.TryParse(jwt, out var parsed) && parsed != null);
        return parsed!;
    }

    private static string Base64UrlEncode(string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
