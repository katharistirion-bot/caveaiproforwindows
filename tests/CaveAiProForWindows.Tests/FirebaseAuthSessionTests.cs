using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class FirebaseAuthSessionTests
{
    private string? _tempDir;
    private string? _prevStoreDirEnv;
    private FirebaseIdToken? _restoredToken;

    [TestInitialize]
    public void SetUp()
    {
        _restoredToken = CloudPublishWebViewHost.TokenCache.Current;
        _tempDir = Path.Combine(Path.GetTempPath(), "caveai-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _prevStoreDirEnv = Environment.GetEnvironmentVariable("CAVEAI_AUTH_TOKEN_STORE_DIR");
        Environment.SetEnvironmentVariable("CAVEAI_AUTH_TOKEN_STORE_DIR", _tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        FirebaseAuthSession.SignOutLocal();
        if (_restoredToken != null)
            CloudPublishWebViewHost.TokenCache.Update(_restoredToken);

        Environment.SetEnvironmentVariable("CAVEAI_AUTH_TOKEN_STORE_DIR", _prevStoreDirEnv);
        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void SignOutLocal_clears_shared_cache_and_persisted_token()
    {
        var token = MakeUsableTestToken("user@example.com");
        CloudPublishWebViewHost.TokenCache.Update(token);

        Assert.IsNotNull(CloudPublishWebViewHost.TokenCache.TryGetUsableToken());
        Assert.AreEqual("user@example.com", FirebaseAuthSession.CurrentAccountEmail);

        FirebaseAuthSession.SignOutLocal();

        Assert.IsNull(CloudPublishWebViewHost.TokenCache.TryGetUsableToken());
        Assert.IsNull(FirebaseAuthSession.CurrentAccountEmail);
        Assert.IsNull(FirebaseAuthTokenStore.TryLoad());
    }

    [TestMethod]
    public void FirebaseSignOutScript_targets_firebase_auth_signOut()
    {
        StringAssert.Contains(FirebaseAuthSession.FirebaseSignOutScript, "firebase.auth().signOut()");
    }

    private static FirebaseIdToken MakeUsableTestToken(string email)
    {
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payloadJson = $$"""{"sub":"test-user","email":"{{email}}","exp":{{exp}}}""";
        var payloadB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadJson))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var raw = "hdr." + payloadB64 + ".sig";
        Assert.IsTrue(FirebaseIdTokenParser.TryParse(raw, out var parsed) && parsed != null);
        return parsed;
    }
}
