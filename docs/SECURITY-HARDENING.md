# Security hardening — Cave AI Pro ecosystem

Canonical copy: **`D:\caveaiproforwindows\docs\SECURITY-HARDENING.md`**

Keep platform-specific secrets docs in each repo (`docs/SECURITY.md` on Windows, website `docs/CAVE_AI_SETUP.md`, Android `keystore.properties.example`), but treat this file as the cross-platform operator checklist.

---

## Repository layout

| Repo | Path | Role |
|------|------|------|
| Windows | `D:\caveaiproforwindows` | Desktop companion; short Firestore stub only |
| Android | `D:\CaveAIPro` | Play app; canonical rules live under `firebase/` |
| Web | `D:\CaveAIpro website` | Hosting + canonical rules mirror under `firebase/` |

**Windows must never replace website/Android `firestore.rules` with its 37-line stub.** Deploy rules from website or Android only.

---

## Secrets hygiene

| Secret | Where it lives | Git? |
|--------|----------------|------|
| Firebase **Browser** API key | Website `src/firebase.js`, Windows inject at build | Browser key is public; restrict in Cloud Console |
| Firebase **Android** API key | `app/google-services.json` | **Never** commit |
| Replicate BYOK | Windows Credential Manager | No |
| Firebase ID token | `%LOCALAPPDATA%\CaveAiProForWindows\auth-token.dat` (DPAPI) | No |
| Website App Check / admin | `.env.local` (gitignored) | No |
| Android signing | `keystore.properties`, `upload-keystore.jks` | No |

**Pre-release audit (all three repos on disk):**

```powershell
cd D:\caveaiproforwindows
.\tools\security-audit.ps1
```

**Rules parity (website ↔ Android):**

```powershell
.\tools\verify-firestore-rules-canonical.ps1
```

---

## Firebase rules (Firestore + Storage)

- **Canonical:** `CaveAIpro website/firebase/firestore.rules` and `CaveAIPro/firebase/firestore.rules` must stay byte-identical (normalized line endings).
- **Storage:** `firebase/storage.rules` — same rule in both repos.
- **Windows stub:** `caveaiproforwindows/firebase/firestore.rules` — deny-by-default; not deployed to production.

**Deploy (operator — only after verify PASS and staging check):**

```powershell
cd "D:\CaveAIpro website"
npx -y firebase-tools@latest deploy --only firestore:rules,storage
# or
npm run deploy:rules
```

Do **not** deploy from Windows repo. Review Console diff before applying.

---

## API key separation (Browser vs Android)

| Key | Suffix (project) | Restrict to |
|-----|------------------|-------------|
| Browser | `…iFyD8` | HTTP referrers: `https://www.caveaipro.com/*`, Firebase Hosting domains, `http://localhost/*` |
| Android | `…RRRmA0` | Android app SHA-1 + package `com.caveaipro.app` |

Never copy `google-services.json` `current_key` into website or Windows build config.

Rotation checklist: see `docs/SECURITY.md` (Windows) § Firebase API key rotation.

---

## App Check (operator)

1. Firebase Console → **App Check** → register Web (reCAPTCHA v3), Android (Play Integrity), optional debug tokens for dev.
2. Website: set `VITE_FIREBASE_APPCHECK_RECAPTCHA_SITE_KEY` in `.env.local`; prod build via `check-production-env.mjs`.
3. Start with **Monitoring**, then enforce on **Storage** and **Firestore** when clients send valid tokens.
4. Android Gemini proxy already sends App Check headers (`GeminiProxyAppCheckHeader.kt`).

---

## Website HTTP security

Configured in `firebase.json` hosting headers:

- `Content-Security-Policy` — Firebase, Google Auth, OSM tiles, Font Awesome CDN
- `Strict-Transport-Security` — 1 year + preload
- `Permissions-Policy` — disables camera/mic/USB; geolocation self only
- Existing: `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options`

Vite production build uses module scripts only (no inline script hash requirements).

---

## Windows data protection

| Asset | Protection |
|-------|------------|
| `auth-token.dat` | DPAPI `CurrentUser` + app entropy (`UserScopedDpapiProtector`); legacy plain `auth-token.json` migrated on read |
| `startup.log` | Release: WebView console not logged; lines redacted for email/JWT/API keys before write |
| Diagnostic ZIP | `DiagnosticBundleExporter` redacts logs, paths, tokens; never includes `auth-token.dat` |

**DPAPI limits:** tokens are recoverable by the same Windows user profile on the same machine; not portable across users/PCs; not a substitute for short JWT TTL and server-side revocation.

---

## Android network + Gemini pins

- **Release:** `network_security_config.xml` — `cleartextTrafficPermitted="false"`, system CAs only.
- **Debug:** separate config allows user CAs for proxy tooling; cleartext still off.
- **Optional Gemini cert pins:** set in `local.properties` or env:

  ```properties
  GEMINI_CERT_PINS=sha256/AAAAAAAA...,sha256/BBBBBBBB...
  ```

  Obtain pins via OpenSSL (see comment in `app/build.gradle.kts`). Empty pins log a release warning; direct REST still uses HTTPS + system trust store.

---

## Backup ZIP integrity

| Platform | Export | Import verify |
|----------|--------|---------------|
| Android | Writes `integrity_manifest.json` (SHA-256 per entry) | `BackupZipIntegrityVerifier` before restore |
| Windows | Same manifest layout (`IntegrityVerifier`) | `IntegrityVerifier.VerifyZip` on open |

Older ZIPs without a manifest skip verification with a warning (backward compatible).

---

## CI security gates

| Repo | Gate |
|------|------|
| Windows | `.github/workflows/ci.yml` → `tools/security-audit.ps1` |
| Website | `.github/workflows/ci.yml` → `npm run security:scan` |

Full cross-repo rules check requires sibling clones on the operator machine (not in single-repo CI checkout).

---

## Operator checklist (release)

- [ ] `verify-firestore-rules-canonical.ps1` PASS
- [ ] `security-audit.ps1` PASS (0 FAIL)
- [ ] Cloud Console API restrictions reviewed
- [ ] App Check enforcement plan documented
- [ ] Keystore passwords not placeholder (`keystore.properties`)
- [ ] `.env.local` / `local.properties` not committed
- [ ] Firestore/Storage deploy only from website or Android after review

---

## Reporting

Report security issues privately to Georgios Kourentzis before public disclosure.
