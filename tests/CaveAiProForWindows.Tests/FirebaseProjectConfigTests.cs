using System.Text.Json;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class FirebaseProjectConfigTests
{
    [TestMethod]
    public void Embedded_firebase_config_json_parses_and_is_usable()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "CaveAiProForWindows", "Assets", "DesktopAuth", "firebase-config.json");
        path = Path.GetFullPath(path);

        Assert.IsTrue(File.Exists(path), "Missing firebase-config.json at " + path);

        var json = File.ReadAllText(path);
        JsonDocument.Parse(json);

        var cfg = JsonSerializer.Deserialize<FirebaseWebClientConfig>(json);
        Assert.IsNotNull(cfg);
        Assert.AreEqual("caveaipro-5950e", cfg.ProjectId);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_API_KEY")))
            Assert.IsTrue(cfg!.IsUsable, "apiKey must be set when CAVEAIPRO_FIREBASE_API_KEY is provided");
        else
            Assert.IsFalse(cfg!.IsUsable, "committed firebase-config.json uses REPLACE_AT_BUILD until inject");
        Assert.AreEqual("caveaipro-5950e.firebaseapp.com", cfg.AuthDomain);
        Assert.AreEqual("caveaipro-5950e.firebasestorage.app", cfg.StorageBucket);
    }

    [TestMethod]
    public void ResolveWebApiKey_skips_REPLACE_placeholder_and_uses_observed_key()
    {
        var key = FirebaseProjectConfig.ResolveWebApiKey(
            null,
            "REPLACE_AT_BUILD",
            "AIzaSyExampleKey1234567890");
        Assert.AreEqual("AIzaSyExampleKey1234567890", key);
        Assert.IsFalse(FirebaseProjectConfig.IsUsableApiKey("REPLACE_AT_BUILD"));
    }

    [TestMethod]
    public void TryObserveWebApiKeyFromUri_reads_handler_query()
    {
        FirebaseProjectConfig.TryObserveWebApiKeyFromUri(
            "https://caveaipro-5950e.firebaseapp.com/__/auth/handler?apiKey=AIzaSyObservedFromWeb&providerId=google.com");
        Assert.AreEqual("AIzaSyObservedFromWeb", FirebaseProjectConfig.ObservedWebApiKey);
    }
}
