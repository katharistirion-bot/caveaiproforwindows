# Local secrets (not committed)

## Android (Google Play app only)

```
tools/local/google-services.json
```

Copy from `D:\caveaipro\app\google-services.json` after Firebase Console updates.

## Web + Windows (Browser API key — NOT the Android key)

```
tools/local/firebase-web-config.json
```

Copy from `firebase-web-config.json.example` and paste the **Web app** `apiKey` from:

Firebase Console → Project settings → Your apps → **Web** app → SDK config.

Windows WebView sign-in and caveaipro.com both need this **Browser** key.
The Android `google-services.json` key is package-restricted and must **not** be used for web/Windows.

## Sync after rotation

```powershell
# Full stack: website + inject + Store MSIX (source auto-restores to REPLACE_AT_BUILD)
.\tools\sync-firebase-web-config.ps1 -InjectWindows -BuildWebsite -DeployWebsite -BuildStoreMsix

# MSIX only (same auto-restore)
.\tools\package-store-msix.ps1

# Verify (Android suffix may differ from Browser suffix — that is normal):
.\tools\verify-firebase-key-alignment.ps1
```

## Source vs shipped config

| Location | apiKey |
|----------|--------|
| `src/.../firebase-config.json` (git) | Always `REPLACE_AT_BUILD` |
| MSIX / publish output | Real Browser key (injected at build) |

Pack scripts (`package-store-msix.ps1`, `package-release.ps1`) inject before build and call `restore-firebase-config-placeholder.ps1` in `finally`. If you run `inject-firebase-config.ps1` manually, restore before committing:

```powershell
.\tools\restore-firebase-config-placeholder.ps1
```

## GitHub CI

Secret `CAVEAIPRO_FIREBASE_API_KEY` must be the **Browser** key (same as web config).
