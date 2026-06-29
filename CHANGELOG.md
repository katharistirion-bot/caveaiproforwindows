# Changelog



All notable changes to **CAVE AI PRO for Windows** are documented here.



Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).



## [1.5.1] - 2026-06-15

### Added

- Reference catalog v2: Near me radius slider (5–200 km), multi-token search, Surveyed/Sparse badges, force refresh, copy share link
- Reference vs community compare window (Tools menu + catalog detail)
- Publish / re-publish reference catalog match suggestions before Cloud Publish
- Offline reference catalog shard cache (last 3 country shards) with offline banner
- X-RAY reference pin overlay from cached index (toolbar + LEGAL & SETTINGS)
- Lazy tab init for PLAN, 3D MODEL, and X-RAY tabs
- Field trip PDF itinerary export
- Android sync v3: "Open backup?" prompt on new ZIP; same-project-name conflict dialog
- Batch QC HTML summary export
- Light analytics counters (catalog, field trip, X-RAY pins) in diagnostic bundle
- About dialog: trial days remaining + Manage subscription (Google Play)

### Changed

- Premium first-run experience: cinematic splash, intro video window, onboarding wizard
- Onboarding copy refined for a more professional desktop-companion tone
- Post sign-in setup wizard matches new onboarding visual language
- Account banner shows trial days remaining prominently

## [1.5.4] - 2026-06-21

### Added

- **Blocking legal disclaimer on first launch** (parity with Android and web): modal EULA gate before main window; re-prompts when document version changes
- **FCRPA / protected cave data clause** (section 13C) in Windows EULA — aligned with Android and web legal text

### Changed

- LEGAL & SETTINGS tab: document version metadata synced to `LegalTexts.DocumentVersion` (1.3); accept checkbox uses "I Agree and Accept" wording

## [1.5.3] - 2026-06-20

### Added

- **Docked Cave AI assistant** panel (on-device Q&A; Help → Cloud AI on web when signed in)
- **Similar caves nearby** in Reference Catalog detail
- **Per-cave favorites** (signed-in Firestore + local cache; My favorites filter)
- **Offline trip narrative** intent in on-device Cave AI brain (parity with Android)

### Fixed

- Reference catalog share URL fallback via country shard loader
- Offline brain greetings and trip/expedition report routing

## [Unreleased]

### Added

- Reference survey handoff: catalog **Map with Cave AI Pro** opens browser survey URL; Windows can link/resume projects via `referenceCatalogLink` metadata
- Cross-platform contract doc (`docs/cross-platform-contract.md`) for catalog URLs, share links, and publish fields
- Cloud Publish passes `referenceCatalogId` / `referenceCatalogCountry` when project is linked to a reference pin
- **Client error telemetry**: anonymised crash/error reports to Firestore `desktop_client_telemetry` when signed in
- **MSI / portable update banner** when GitHub has a newer build
- **Survex export verification** dialog before saving `.svx`
- **Raster decode size limit** (128 MiB per map file)
- Tests: `ClientErrorTelemetryTests`, `LoadFromPathsWorkerTests`
- `docs/LOCALIZATION.md`

### Changed

- Reference catalog detail: copy share link and survey-start URL aligned with web/Android `?action=survey` contract
- **Legal texts v1.4**: removed cloud AI Render; documents on-device Cave AI and optional error telemetry
- **MainViewModel**: update banner, footer status (`RefreshFooterStatus`), design-from-survey command; removed cloud AI Render / GenerativeMap code paths
- **README** / **MICROSOFT-STORE.md** updated for v1.5.4 workstation scope

### Fixed

- Privacy summary (Store channel) no longer references removed AI Render feature

## [1.5.0] - 2026-06-15

### Added

- Native **Reference Cave Catalog** panel (cached index from caveaipro.com, country filter, search, map clusters, featured caves, country-shard detail load)
- **Field Trip Planner** (stops from catalog, reorder, GPX/KML/text export, Google Maps directions, LocalAppData persistence)
- **Reference ↔ survey linking** on project load (name + distance match; metadata on project)
- Post-sign-in wizard (Android sync folder, sample survey, tour)
- Account banner with entitlement summary and Google Play manage link
- About dialog subscription status + Android app link
- Android auto-sync v2 tray badge on new `CaveAI_Backup_*.zip`
- **Batch survey QC** folder scan with unified CSV/TXT report
- Load progress overlay with cancel for large ZIP open
- Autosave drafts to LocalAppData on timer
- Help → Export diagnostic bundle (logs + redacted ui-settings)
- Cloud publish checklist dialog (QC, legal terms, photo count)
- Collaboration unread taskbar badge refresh
- Long profile → SVG export

### Changed

- Help → Public Cave Library opens native reference catalog; WebView moved to separate menu item
- Clearer subscription-required messaging with Play Store link on login denial
- Production Store MSIX version 1.5.0 (`MicrosoftStore-Win64`)

## [1.4.3] - 2026-06-07

### Changed

- Production Store build after Microsoft certification: Google sign-in and subscription gate restored (`MicrosoftStore-Win64`; review profile unchanged for future resubmissions)
- `appsettings.json` test mode disabled; dev settings no longer copied into Release / Store MSIX



## [1.4.2] - 2026-06-13

### Changed

- Version bump for Microsoft Store Partner Center submission (1.4.2)



## [1.4.1] - 2026-06-13

### Changed

- Version bump for Microsoft Store Partner Center submission (1.4.1)



## [1.4.0] - 2026-06-07



### Added

- First-run cinematic intro video after sign-in (skip, mute, replay from Help)

- Publication sheet window — composite plan, elevation, optional 3D overview, legend, and metadata export



### Fixed

- Microsoft Store MSIX now publishes self-contained .NET 8 runtime (`includedFrameworks`) so clean Windows PCs do not prompt for a separate .NET install



## [1.3.0] - 2026-06-07



### Added

- Help → Open diagnostic folder

- Legal terms version tracking (re-accept when EULA document version changes)

- Velopack auto-update bootstrap and InstallationGuard support for Setup.exe installs

- DPAPI encryption for stored Firebase auth token

- Android ↔ Windows sync contract index (`docs/sync-contract.md`)

- Cross-platform survey site type (Cave / Mine / Pothole / Spring) on maps and exports

- Recent files: remove, rename, delete from list



### Fixed

- 3D MODEL tab (sidebar, orbit, clean defaults)

- Velopack update channel and dynamic toolbar version display



### Security

- Release builds no longer honor dev bypass environment variables

- Firebase `listProjectComments` requires project membership

- Collaboration callables re-check premium entitlement server-side



## [1.2.1] - 2026-06-07



### Added

- Cross-platform survey site type on maps and exports

- Recent files: remove, rename, delete from list

- 3D MODEL tab fixes



### Fixed

- Velopack update channel and dynamic toolbar version display



## [1.2.0] - 2026-06-01



### Added

- Windows companion MVP: plan/section, exports, integrity checks, cloud publish hooks



[Unreleased]: https://github.com/katharistirion-bot/caveaiproforwindows/compare/v1.5.0...HEAD

[1.5.0]: https://github.com/katharistirion-bot/caveaiproforwindows/compare/v1.4.3...v1.5.0

[1.4.3]: https://github.com/katharistirion-bot/caveaiproforwindows/compare/v1.4.2...v1.4.3

[1.4.2]: https://github.com/katharistirion-bot/caveaiproforwindows/compare/v1.4.1...v1.4.2

[1.4.1]: https://github.com/katharistirion-bot/caveaiproforwindows/compare/v1.4.0...v1.4.1

[1.4.0]: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.4.0

[1.3.0]: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.3.0

[1.2.1]: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.2.1

[1.2.0]: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.2.0

