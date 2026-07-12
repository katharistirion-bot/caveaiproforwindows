# Microsoft Store submission checklist (CAVE AI PRO Windows)

Updated: 2026-06-30 · **Prepare only** — no automated Store upload.

## Distribution channels

| Channel | Artifact | Shortcuts / updates |
|---------|----------|---------------------|
| **Velopack** (sideload / GitHub Releases) | `CaveAiProForWindows-*-Setup.exe` + `velopack/` deltas | Desktop + Start Menu via `--shortcutLocations StartMenu,Desktop`; in-app auto-update via `AppUpdateService` |
| **WiX MSI** | `CaveAiProForWindows-*-Setup.msi` | Per-machine Start Menu + optional desktop shortcut; file associations + `caveaipro://` protocol |
| **Microsoft Store** | Unsigned MSIX from `tools/package-store-msix.ps1` | Store handles install, updates, and tiles; manifest includes `.json`/`.zip` associations and `caveaipro` protocol |

## Package identity

| Field | Value |
|-------|-------|
| Product name | CAVE AI PRO |
| Package family | See `store/Package.appxmanifest` (stamped at pack time) |
| Version | `Directory.Build.props` |
| Target | `net8.0-windows10.0.17763.0` |

## Pre-submission checklist

- [ ] `dotnet test` in repo root (400+ tests)
- [ ] Build unsigned MSIX: `tools/package-store-msix.ps1`
- [ ] Partner Center → Apps → CAVE AI PRO → Packages → upload new `.msix`
- [ ] Store listing screenshots + privacy policy URL: `https://www.caveaipro.com/privacy`
- [ ] Firestore rules deployed from **website** repo (`npm run deploy:rules`) — Windows repo rules are deny-all stub
- [ ] Sign-in + telemetry: confirm `desktop_client_telemetry` writes (see `docs/TELEMETRY_VERIFICATION.md`)
- [ ] Verify MSIX file-type associations and `caveaipro://explore?...` deep link on a clean VM

## Icons

Store package uses `store/Assets/` (`Square150x150Logo.png`, `Square44x44Logo.png`, `StoreLogo.png`). WiX and Velopack use the same application icon from the publish output (`CaveAiProForWindows.exe` embedded icon).

## Intro video gate

- **Default:** disabled — no bundled `Assets/Intro/intro.mp4` in repo; `IntroVideoGate.ShouldShowFirstRun` requires bundled video **and** `HasSeenIntroVideo == false`.
- **No Firebase Remote Config** — local settings only (`AppUiSettingsModel.HasSeenIntroVideo`).
- **Replay:** Help → Intro video (bypasses first-run gate).

## Generative map

Windows client does **not** call `replicateGenerativeMap`. Legacy callable remains in `firebase/functions` for Android only.

## Related docs

- `docs/MICROSOFT-STORE.md`
- `docs/STORE_UPLOAD_CHECKLIST.md`
- `docs/MANUAL_RELEASE_CHECKLIST.md`
- `docs/ARCHITECTURE.md`
