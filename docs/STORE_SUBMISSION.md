# Microsoft Store submission checklist (CAVE AI PRO Windows)

Generated: 2026-06-29 · **Prepare only** — no automated Store upload.

## Package identity

| Field | Value |
|-------|-------|
| Product name | CAVE AI PRO |
| Package family | See `store/Package.appxmanifest` (stamped at pack time) |
| Version | **1.5.5** (`Directory.Build.props`) |
| Target | `net8.0-windows10.0.17763.0` |

## Pre-submission checklist

- [ ] `dotnet test` in repo root
- [ ] Build unsigned MSIX: `tools/package-store-msix.ps1` (version from `Directory.Build.props`)
- [ ] Partner Center → Apps → CAVE AI PRO → Packages → upload new `.msix`
- [ ] Store listing screenshots + privacy policy URL: `https://www.caveaipro.com/privacy`
- [ ] Firestore rules deployed from **website** repo (`npm run deploy:rules`) — Windows repo rules are deny-all stub
- [ ] Sign-in + telemetry: confirm `desktop_client_telemetry` writes (see `docs/TELEMETRY_VERIFICATION.md`)

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
