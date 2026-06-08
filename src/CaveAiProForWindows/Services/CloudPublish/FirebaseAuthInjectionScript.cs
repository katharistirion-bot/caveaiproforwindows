using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Builds and validates WebView2 document-created scripts for bundled desktop auth.</summary>
public static class FirebaseAuthInjectionScript
{
    public const string VirtualHost = "localhost";

    /// <summary>
    /// Regression guard: host check must run before the one-shot injected flag so website navigations
    /// do not block config injection on bundled auth.html.
    /// </summary>
    public const string HostBeforeInjectedFlagRule =
        "localhost guard must precede __CAVEAI_FIREBASE_CONFIG_INJECTED__";

    public static string BuildDocumentCreatedScript(FirebaseWebClientConfig config)
    {
        var json = JsonSerializer.Serialize(config.ToFirebaseInitializeAppObject());
        return BuildDocumentCreatedScript(json, config.IsUsable);
    }

    public static string BuildDocumentCreatedScript(string firebaseConfigJson, bool isUsable)
    {
        return $$"""
            (function () {
              var host = (location && location.hostname) || '';
              if (host !== '{{VirtualHost}}') return;
              if (window.__CAVEAI_FIREBASE_CONFIG_INJECTED__) return;
              window.__CAVEAI_FIREBASE_CONFIG_INJECTED__ = true;
              window.__CAVEAI_FIREBASE_CONFIG__ = {{firebaseConfigJson}};
              window.__CAVEAI_FIREBASE_CONFIG_READY__ = {{(isUsable ? "true" : "false")}};
              try {
                window.dispatchEvent(new CustomEvent('caveai-firebase-config', {
                  detail: window.__CAVEAI_FIREBASE_CONFIG__
                }));
              } catch (e) {}
            })();
            """;
    }

    public static string BuildPushConfigScript(FirebaseWebClientConfig config)
    {
        var json = JsonSerializer.Serialize(config.ToFirebaseInitializeAppObject());
        return $$"""
            (function () {
              window.__CAVEAI_FIREBASE_CONFIG__ = {{json}};
              window.__CAVEAI_FIREBASE_CONFIG_READY__ = true;
              try {
                window.dispatchEvent(new CustomEvent('caveai-firebase-config', {
                  detail: window.__CAVEAI_FIREBASE_CONFIG__
                }));
              } catch (e) {}
            })();
            """;
    }

    /// <summary>Validates injection script ordering — returns false if the localhost regression is reintroduced.</summary>
    public static bool ValidateDocumentCreatedScriptOrder(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return false;

        var hostGuard = script.IndexOf("host !== '" + VirtualHost + "'", StringComparison.Ordinal);
        var injectedFlag = script.IndexOf("__CAVEAI_FIREBASE_CONFIG_INJECTED__", StringComparison.Ordinal);
        return hostGuard >= 0 && injectedFlag > hostGuard;
    }
}
