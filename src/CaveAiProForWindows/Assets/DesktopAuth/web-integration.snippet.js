// CaveAI Pro — web integration snippet for desktop auth (embed=windows&desktopAuth=v1).
// After Firebase sign-in, call:
//   window.caveAiDesktopAuth?.deliverToken(await firebase.auth().currentUser.getIdToken());
//
// The desktop app injects window.caveAiDesktopAuth before your bundle runs when loaded in WebView2.

(function () {
  var DESKTOP_AUTH = /(?:^|[?&])desktopAuth=v1(?:&|$)/.test(window.location.search || '');

  function deliver(idToken) {
    if (!idToken) return;
    if (window.caveAiDesktopAuth && window.caveAiDesktopAuth.deliverToken) {
      window.caveAiDesktopAuth.deliverToken(idToken);
    }
  }

  if (!DESKTOP_AUTH) return;

  function hookFirebaseCompat() {
    try {
      if (!window.firebase || !window.firebase.auth) return false;
      var auth = window.firebase.auth();
      auth.onAuthStateChanged(function (user) {
        if (user) user.getIdToken().then(deliver);
      });
      return true;
    } catch (e) {
      return false;
    }
  }

  if (!hookFirebaseCompat()) {
    var attempts = 0;
    var timer = setInterval(function () {
      if (hookFirebaseCompat() || ++attempts > 120) clearInterval(timer);
    }, 500);
  }
})();
