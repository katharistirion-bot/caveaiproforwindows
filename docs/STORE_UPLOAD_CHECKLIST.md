# Microsoft Store upload checklist

Generated: 2026-07-11  
Repo: `D:\caveaiproforwindows`  
Product ID: **9PPF3HPZRL21**

## MSIX status (this machine)

| Package | Path | Status |
|---------|------|--------|
| **Review (certification)** | `_store_out\CaveAiProForWindows-1.5.13-Store-Review-unsigned.msix` | Built 2026-07-11 |
| **Production (unsigned)** | `_store_out\CaveAiProForWindows-1.5.13-Store-unsigned.msix` | Built 2026-07-11 |

App version in `Directory.Build.props`: **1.5.13** (package identity version **1.5.13.0**).

GitHub sideload: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.5.13

See **`docs/STORE_UPLOAD_1.5.13.md`** for Partner Center steps and release notes.

Review build uses **`STORE_REVIEW_UNLOCKED`** via profile `MicrosoftStore-Review-Win64` (subscription gate disabled for cert testers).

### Build production MSIX (reproduce)

Firebase Browser API key ("CaveAI Pro Web") from `tools/local/firebase-web-config.json` (not committed) or env:

```powershell
cd D:\caveaiproforwindows
$cfg = Get-Content tools\local\firebase-web-config.json -Raw | ConvertFrom-Json
$env:CAVEAIPRO_FIREBASE_API_KEY = $cfg.apiKey
.\tools\package-store-msix.ps1
```

Or set `$env:CAVEAIPRO_FIREBASE_API_KEY` directly (Firebase Console → Project settings → Your apps → CaveAI Pro Web → Web API Key).

Expected output: `_store_out\CaveAiProForWindows-1.5.12-Store-unsigned.msix`

### Build review MSIX (certification)

```powershell
cd D:\caveaiproforwindows
$env:CAVEAIPRO_FIREBASE_API_KEY = '<Browser key from tools/local/firebase-web-config.json>'
.\tools\package-store-msix-review.ps1
```

Expected output: `_store_out\CaveAiProForWindows-<version>-Store-Review-unsigned.msix`

**Note:** Fixed em-dash encoding in `tools/package-store-msix.ps1` (2026-06-11) that previously caused PowerShell parse errors on this machine.

## Partner Center — certification (review MSIX)

- [ ] Review MSIX built locally (`package-store-msix-review.ps1` succeeded)
- [ ] Upload **`*-Store-Review-unsigned.msix`** to Partner Center → **Packages** (unsigned; Microsoft re-signs)
- [ ] Bump `<Version>` in `Directory.Build.props` if resubmitting after rejection
- [ ] Manifest identity matches Partner Center Product identity exactly (`store/Package.appxmanifest`)
- [ ] Declare **Run full trust** capability in Partner Center if not already approved
- [ ] Paste **Notes for certification** from `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`
- [ ] Do **not** sign the MSIX locally for Store submission

## After certification passes (production MSIX)

1. Run **`.\tools\package-store-msix.ps1`** (normal profile — **no** `STORE_REVIEW_UNLOCKED`).
2. Upload **`*-Store-unsigned.msix`** as the next Store update (same package identity; version bump as needed).
3. **Do not** leave the review build in production — it disables subscription enforcement.

```powershell
cd D:\caveaiproforwindows
$env:CAVEAIPRO_FIREBASE_API_KEY = '<Browser key from tools/local/firebase-web-config.json>'
.\tools\package-store-msix.ps1
```

## Production project build check

Full `dotnet build CaveAiProForWindows.sln -c Release` may still fail without Firebase env var (Obfuscar + `verify-firebase-config.ps1`). Store MSIX scripts inject config at pack time; use those scripts for Store artifacts.

For local Release builds without a Browser key, skip the MSBuild guard:

```powershell
dotnet build CaveAiProForWindows.sln -c Release -p:VerifyFirebaseConfig=false
```

See `docs/SECURITY.md` for `-AllowPlaceholder` on `tools/inject-firebase-config.ps1` (script lives under `tools/`, not `scripts/`).

## Blockers needing user action

| Blocker | Action |
|---------|--------|
| Firebase Browser API key | Set `$env:CAVEAIPRO_FIREBASE_API_KEY` before `package-store-msix.ps1` (see `tools/local/README.md`) |
| Partner Center upload | Sign in to [Microsoft Partner Center](https://partner.microsoft.com/dashboard); upload MSIX manually |
| Certification notes | Copy from `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md` |
| Production update after cert | Build with `package-store-msix.ps1` (non-review profile), upload unsigned MSIX |
| Version bump | Edit `Directory.Build.props` before resubmission if Partner Center requires higher version |
| Firestore rules (cross-platform publish fields) | Deploy from `D:\CaveAIPro\firebase` or `npm run deploy:rules` on website repo before relying on `referenceCatalogId` in production |

## Related docs

- `docs/MICROSOFT-STORE.md` — full MSIX pipeline
- `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md` — copy-paste certification notes
- `docs/CODE-SIGNING.md` — sideload signing (not used for Store upload)
- `tools/local/README.md` — local Firebase Browser key (gitignored)
