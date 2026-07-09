# Firebase App Check — Windows desktop companion

Minimal path for **CaveAI Pro for Windows** to send `X-Firebase-AppCheck` on Firestore and Storage REST calls before Firebase Console **Enforce** is enabled.

Web and Android already mint App Check tokens (reCAPTCHA v3 and Play Integrity). Windows uses **WebView2 + the live website** — no Firebase C# SDK.

---

## Current architecture

| Layer | Role |
|-------|------|
| **Website** (`src/firebase.js`) | `initializeAppCheck` + reCAPTCHA v3 (prod) or debug token (dev) |
| **Website bridge** (`src/utils/desktopAppCheckBridge.js`) | When `?embed=windows` or `desktopAuth=v1`, delivers JWT to WebView2 |
| **WebView2 injected script** (`DesktopAuthBridgeScript.cs`) | `window.caveAiDesktopAppCheck.deliverToken` → postMessage |
| **C# cache** (`FirebaseAppCheckTokenCache`) | Thread-safe JWT holder shared with REST client |
| **REST client** (`FirebaseRestClient`, `FirebaseCallableClient`) | Adds `X-Firebase-AppCheck` when cache has a usable token |

**Until Enforce:** missing App Check header is a no-op (requests still succeed). After Enforce, REST calls without a valid token return HTTP 403.

---

## Token flow (production)

1. User opens Public Library web map or Push to Cloud sign-in (`?embed=windows&desktopAuth=v1`).
2. Website loads with production App Check (`VITE_FIREBASE_APPCHECK_RECAPTCHA_SITE_KEY`).
3. `ensureDesktopAppCheckBridge()` calls `getToken(appCheck)` and posts JWT to the host.
4. `DesktopAuthWebViewBridge` stores token in `CloudPublishWebViewHost.AppCheckTokenCache`.
5. `FirebaseRestClient` attaches header on Storage upload, Firestore PATCH/GET, etc.
6. Token auto-refreshes every ~50 minutes while the embed page stays loaded.

Push to Cloud / sign-in WebView also sends `caveai-desktop-appcheck-request` after navigation so the page re-delivers a fresh token.

---

## Dev / staging (debug tokens)

1. Firebase Console → **App Check** → **Manage debug tokens** → add a UUID.
2. Website `.env.local`: `VITE_FIREBASE_APPCHECK_DEBUG_TOKEN=<uuid>`
3. Load the website in Windows WebView2 (Public Library or auth) — bridge delivers minted JWTs the same way.

Do **not** paste the debug UUID into REST headers directly; the JS SDK exchanges it for a short-lived JWT.

---

## What is not implemented yet

- Standalone App Check page for REST-only flows with no prior WebView session (future `appcheck.html` + reCAPTCHA in bundled auth host).
- Play Integrity / DeviceCheck (Android/iOS only).
- Custom App Check provider + backend token exchange.

---

## Verification (operator)

1. Build website with App Check key set; deploy or run prod build locally.
2. Windows: sign in via Public Library or Push to Cloud.
3. Firebase Console → **App Check** → **Metrics** → confirm **CaveAI Pro Web** shows valid tokens (Windows traffic counts as Web app).
4. Enable **Monitoring** on Storage/Firestore first; after 24–48h of healthy metrics, set **Enforce**.
5. Re-test Push to Cloud, favorites sync, survey cloud import, Public Library download.

---

## Code map

| File | Purpose |
|------|---------|
| `Services/CloudPublish/FirebaseAppCheckTokenCache.cs` | Shared cache |
| `Services/CloudPublish/FirebaseAppCheckHeader.cs` | Header helper |
| `Services/CloudPublish/DesktopAuthBridgeScript.cs` | JS bridge (auth + App Check) |
| `Services/CloudPublish/DesktopAuthProtocol.cs` | `caveai-desktop-appcheck-token` message |
| `Services/CloudPublish/FirebaseRestClient.cs` | Attaches header on all Firebase REST calls |
| Website `src/utils/desktopAppCheckBridge.js` | Mints and delivers token from embed pages |

See also: website `docs/PRODUCTION_CHECKLIST.md` § App Check, `docs/STORAGE_WEB_CORS_APP_CHECK.md`, `docs/SECURITY-HARDENING.md`.
