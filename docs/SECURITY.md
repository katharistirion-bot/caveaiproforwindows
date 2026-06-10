# Security — CAVE AI PRO for Windows

## Secrets in the repository

| Asset | In git? | Notes |
|-------|---------|-------|
| Firebase Web API key | **Placeholder only** (`REPLACE_AT_BUILD` in `firebase-config.json`) | Inject at build via `tools/inject-firebase-config.ps1` or `CAVEAIPRO_FIREBASE_API_KEY` |
| Replicate BYOK token | **No** | Windows Credential Manager (`ReplicateApiTokenStore`) |
| Firebase ID token | **No** | DPAPI file `%LOCALAPPDATA%\CaveAiProForWindows\auth-token.dat` |
| Code signing PFX | **No** | GitHub Secrets / local only (`CODE-SIGNING.md`) |
| OpenAI `sk-` keys | **No** | Not used in desktop app |

## Build-time injection (required for release / Store)

**Git source** (`src/.../firebase-config.json`) always stays `REPLACE_AT_BUILD`. Pack scripts inject the real Browser key, build, then **automatically restore** the placeholder in a `finally` block — you never need to hand-edit the source back.

Copy `tools/local/firebase-web-config.json.example` → `tools/local/firebase-web-config.json` and paste the **Web app** `apiKey` from Firebase Console.

```powershell
# Store MSIX (inject → publish → pack → restore source automatically)
.\tools\package-store-msix.ps1

# Or full Browser stack sync + MSIX
.\tools\sync-firebase-web-config.ps1 -InjectWindows -BuildStoreMsix
```

Sideload release packaging restores the same way:

```powershell
.\tools\package-release.ps1 -Tag v1.4.0
```

Manual inject (testing only — restore before commit):

```powershell
.\tools\inject-firebase-config.ps1
# ... build ...
.\tools\restore-firebase-config-placeholder.ps1
```

Or set the key explicitly:

```powershell
$env:CAVEAIPRO_FIREBASE_API_KEY = '<Firebase Web API key>'
.\tools\inject-firebase-config.ps1
.\tools\verify-firebase-config.ps1   # fails if REPLACE_AT_BUILD remains (publish output)
```

CI: `.github/workflows/release.yml` **requires** secret `CAVEAIPRO_FIREBASE_API_KEY`.

**Release guardrails (do not disable without reason):**

| Layer | What it prevents |
|-------|------------------|
| `tools/restore-firebase-config-placeholder.ps1` | Real Browser key left in git-tracked source after pack |
| `tools/verify-firebase-config.ps1` | Shipping `REPLACE_AT_BUILD` in publish/MSIX output |
| `build/VerifyFirebaseConfig.targets` | Release MSBuild output without injected config |
| `FirebaseAuthInjectionRegressionTests` | WebView injection script setting the one-shot flag on the website before `localhost` |
| Runtime `FirebaseHostingConfigFetcher` | Dev/sideload sign-in when inject was skipped (fetches public key from Hosting) |
| `package-release.ps1` / `package-store-msix.ps1` | Packaging without real key; both restore source in `finally` |

Store MSIX: `package-store-msix.ps1` injects, verifies publish output, packs MSIX, then restores placeholder.

Sideload releases: `package-release.ps1` injects, syncs `firebase-config.json` into publish output, verifies, then restores placeholder.

## Firebase API key rotation (Android + Web + Windows)

Firebase uses **two different API keys** in the same project:

| Key | Used by | Source file |
|-----|---------|-------------|
| **Android** | Google Play app | `google-services.json` |
| **Browser** | caveaipro.com + Windows WebView sign-in | `firebase-web-config.json` / Web app SDK config |

After deleting the old **Browser** key:

1. **Google Cloud Console** → APIs & Services → Credentials → **Create API key** (Browser).
2. Restrict **HTTP referrers**: `https://www.caveaipro.com/*`, `https://caveaipro-5950e.web.app/*`, `https://caveaipro-5950e.firebaseapp.com/*`, `http://localhost/*`.
3. Restrict **APIs**: Identity Toolkit, Token Service, Firebase Installations, and other Firebase APIs you use.
4. **Firebase Console** → Project settings → **Web app** → confirm SDK config shows the new Browser `apiKey`.
5. Save to `tools/local/firebase-web-config.json` (copy from `.example`).
6. Replace Android file: `D:\caveaipro\app\google-services.json` (if rotated separately).
7. Sync Browser stack:
   ```powershell
   .\tools\sync-firebase-web-config.ps1 -InjectWindows -BuildWebsite -DeployWebsite -BuildStoreMsix
   ```
8. Update GitHub secret `CAVEAIPRO_FIREBASE_API_KEY` with the **Browser** key.
9. New **Android AAB** to Play Store if `google-services.json` changed.
10. `.\tools\verify-firebase-key-alignment.ps1` — Browser rows must match; Android may differ.

**Do not** copy `google-services.json` `current_key` into website or Windows — it is Android-restricted.

## Firebase Console (operator checklist)

1. **API key restrictions** — limit to your app / referrers where possible.
2. **App Check** — enforce on Storage and Firestore when web traffic is stable.
3. **Firestore / Storage rules** — deny-by-default; users read only their entitlements.
4. **OAuth** — authorized domains include `caveaipro.com` and desktop redirect flows.

## Release build hardening

- **Obfuscar** on Release builds (`build/Obfuscation.targets`) — `Services.Auth`, subscription checks, cloud publish.
- **No dev bypasses in Release** — `CAVEAIPRO_SKIP_APP_LOCK` is Debug-only; `InstallationGuard` has no env bypass in Release.
- **startup.log** — WebView console output is **not** written in Release (may contain auth data). Email/subject metadata only.

## Distribution

- **Microsoft Store** — unsigned MSIX upload; Microsoft re-signs. `STORE_DISTRIBUTION` skips Velopack.
- **Sideload** — Velopack + optional Authenticode (`CODE-SIGNING.md`).

## Reporting issues

Report security concerns privately to the publisher (Georgios Kourentzis) before public disclosure.
