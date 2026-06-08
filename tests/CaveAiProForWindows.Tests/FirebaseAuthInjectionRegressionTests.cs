using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class FirebaseAuthInjectionRegressionTests
{
    [TestMethod]
    public void DocumentCreatedScript_checks_localhost_before_injected_flag()
    {
        var cfg = new FirebaseWebClientConfig
        {
            ApiKey = "AIzaSyTestKey123456789012345",
            AuthDomain = "caveaipro-5950e.firebaseapp.com",
            ProjectId = "caveaipro-5950e",
        };

        var script = FirebaseAuthInjectionScript.BuildDocumentCreatedScript(cfg);
        Assert.IsTrue(
            FirebaseAuthInjectionScript.ValidateDocumentCreatedScriptOrder(script),
            FirebaseAuthInjectionScript.HostBeforeInjectedFlagRule);
    }

    [TestMethod]
    public void DocumentCreatedScript_rejects_reversed_guard_order()
    {
        const string bad = """
            (function () {
              if (window.__CAVEAI_FIREBASE_CONFIG_INJECTED__) return;
              var host = (location && location.hostname) || '';
              if (host !== 'localhost') return;
            })();
            """;
        Assert.IsFalse(FirebaseAuthInjectionScript.ValidateDocumentCreatedScriptOrder(bad));
    }

    [TestMethod]
    public void BundledConfigGuard_rejects_placeholder_json()
    {
        Assert.IsFalse(FirebaseBundledConfigGuard.IsUsableJson("""
            {"apiKey":"REPLACE_AT_BUILD","projectId":"caveaipro-5950e"}
            """));
        Assert.IsTrue(FirebaseBundledConfigGuard.IsUsableJson("""
            {"apiKey":"AIzaSyExampleKey1234567890","projectId":"caveaipro-5950e"}
            """));
    }

    [TestMethod]
    public void PushConfigScript_dispatches_caveai_firebase_config_event()
    {
        var cfg = new FirebaseWebClientConfig { ApiKey = "AIzaSyPushKey", ProjectId = "caveaipro-5950e" };
        var script = FirebaseAuthInjectionScript.BuildPushConfigScript(cfg);
        StringAssert.Contains(script, "caveai-firebase-config");
        StringAssert.Contains(script, "AIzaSyPushKey");
    }
}
