namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Injected into allowed web origins so Firebase ID tokens reach the host via postMessage
/// (explicit <see cref="DesktopAuthProtocol.TokenMessageType"/> or session Bearer forwarding).
/// </summary>
internal static class DesktopAuthBridgeScript
{
    /// <summary>Single-line script safe for <c>ExecuteScriptAsync</c>.</summary>
    public const string JavaScript = """
        (function(){
          if (window.__caveAiDesktopAuthBridgeV1) return;
          window.__caveAiDesktopAuthBridgeV1 = true;
          var TOKEN_TYPE = 'caveai-desktop-auth-token';
          var READY_TYPE = 'caveai-desktop-auth-ready';
          var lastDelivered = '';
          function post(obj) {
            try {
              if (window.chrome && window.chrome.webview)
                window.chrome.webview.postMessage(JSON.stringify(obj));
            } catch (e) {}
          }
          function deliver(idToken) {
            if (!idToken || typeof idToken !== 'string') return;
            if (idToken === lastDelivered) return;
            if (idToken.split('.').length !== 3) return;
            lastDelivered = idToken;
            post({ type: TOKEN_TYPE, idToken: idToken });
          }
          window.caveAiDesktopAuth = { deliverToken: deliver };
          window.addEventListener('message', function(ev) {
            var d = ev.data;
            if (!d) return;
            if (typeof d === 'string') { try { d = JSON.parse(d); } catch (e) { return; } }
            if (d.type === 'caveai-desktop-auth-token' && d.idToken)
              deliver(d.idToken);
          });
          function readAuthHeader(headers) {
            if (!headers) return null;
            try {
              if (headers.get) {
                var v = headers.get('Authorization') || headers.get('authorization');
                if (v && v.indexOf('Bearer ') === 0) return v.substring(7).trim();
              }
            } catch (e) {}
            return null;
          }
          var origFetch = window.fetch;
          if (typeof origFetch === 'function') {
            window.fetch = function(input, init) {
              try {
                var h = (init && init.headers) || (input && input.headers);
                var t = readAuthHeader(h);
                if (t) deliver(t);
              } catch (e) {}
              return origFetch.apply(this, arguments);
            };
          }
          var XHRSend = XMLHttpRequest.prototype.send;
          var XHRSetHeader = XMLHttpRequest.prototype.setRequestHeader;
          XMLHttpRequest.prototype.setRequestHeader = function(name, value) {
            try {
              this.__caveAiHeaders = this.__caveAiHeaders || {};
              this.__caveAiHeaders[name.toLowerCase()] = value;
            } catch (e) {}
            return XHRSetHeader.apply(this, arguments);
          };
          XMLHttpRequest.prototype.send = function() {
            try {
              var auth = this.__caveAiHeaders && this.__caveAiHeaders.authorization;
              if (auth && auth.indexOf('Bearer ') === 0) deliver(auth.substring(7).trim());
            } catch (e) {}
            return XHRSend.apply(this, arguments);
          };
          post({ type: READY_TYPE });
          if (/desktopAuth=v1/.test(window.location.search || '')) {
            function hookFirebase() {
              try {
                if (!window.firebase || !window.firebase.auth) return false;
                window.firebase.auth().onAuthStateChanged(function(u) {
                  if (u) u.getIdToken().then(deliver);
                });
                return true;
              } catch (e) { return false; }
            }
            if (!hookFirebase()) {
              var n = 0;
              var t = setInterval(function() { if (hookFirebase() || ++n > 120) clearInterval(t); }, 500);
            }
          }
        })();
        """;
}
