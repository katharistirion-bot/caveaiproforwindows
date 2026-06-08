namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Runs after bundled auth.html loads (post-Google redirect) to finish Firebase sign-in and deliver the ID token.
/// </summary>
internal static class DesktopAuthCompletionScript
{
    public const string JavaScript = """
        (function () {
          function sleep(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }
          return new Promise(function (resolve) {
            (async function () {
              try {
                if (typeof window.caveAiWaitForFirebaseConfig === 'function')
                  await window.caveAiWaitForFirebaseConfig(8000);
                for (var attempt = 0; attempt < 40; attempt++) {
                  if (window.firebase && firebase.apps && firebase.apps.length) break;
                  await sleep(250);
                }
                if (!window.firebase || !firebase.apps || !firebase.apps.length) {
                  resolve('no-firebase');
                  return;
                }
                if (!firebase.apps.length) {
                  var cfg = window.__CAVEAI_FIREBASE_CONFIG__;
                  if (cfg && cfg.apiKey) firebase.initializeApp(cfg);
                }
                var auth = firebase.auth();
                var result = await auth.getRedirectResult();
                if (result && result.error) {
                  resolve('redirect-error:' + (result.error.message || result.error.code || 'unknown'));
                  return;
                }
                var user = (result && result.user) ? result.user : auth.currentUser;
                if (!user) {
                  await sleep(500);
                  result = await auth.getRedirectResult();
                  if (result && result.error) {
                    resolve('redirect-error:' + (result.error.message || result.error.code || 'unknown'));
                    return;
                  }
                  user = (result && result.user) ? result.user : auth.currentUser;
                }
                if (!user) {
                  resolve('no-user');
                  return;
                }
                var token = await user.getIdToken();
                window.__CAVEAI_DESKTOP_ID_TOKEN__ = token;
                if (window.caveAiDesktopAuth && window.caveAiDesktopAuth.deliverToken)
                  window.caveAiDesktopAuth.deliverToken(token);
                resolve('token-delivered');
              } catch (e) {
                resolve('exception:' + (e && e.message ? e.message : String(e)));
              }
            })();
          });
        })();
        """;
}
