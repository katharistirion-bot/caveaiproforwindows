# Changelog



All notable changes to **CAVE AI PRO for Windows** are documented here.



Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).



## [1.5.14] - 2026-07-12

### Added

- **Explore terrain deep links** — reference catalog and field trip planner open `/map?view=explore` with viewport and layer params (`ReferenceCatalogShareUrls.cs`)
- **Explore compare terrain** — terrain preset parity with web/Android Explore handoff
- **Hydrology scout** — append `hydrology=1,karst=1` layer flags for website parity on Explore URLs
- **Expedition share** — active subscriber shares on surface map layer (Android/web contract parity)
- **Followed contributors** — Public Library followed publishers UI and in-app profile completion gate
- **Cross-platform URL tests** — golden vectors for Explore share URLs in `ReferenceCatalogTests.cs` (mirrors website `cross-platform-url-vectors.mjs`)

### Changed

- **CI** — optional unsigned Store MSIX pack job; fix invalid `secrets` condition on MSIX upload step

### Fixed

- **Surface map WebView** — harden against hangs; survey corridor geometry stability

## [1.5.13] - 2026-07-07

### Added

- **Firebase App Check (Windows)** — WebView2 bridge receives App Check JWT from `?embed=windows` website pages; `FirebaseRestClient` / `FirebaseCallableClient` attach `X-Firebase-AppCheck` when cached (`docs/APPCHECK-WINDOWS.md`)

### Security

- Block disallowed auth redirect navigation in Public Library WebView
- Redact fatal crash logs before write
- Require Firebase auth for field trip Firestore REST reads
- Block review MSIX profile by default in release packaging

### Fixed

- **Near Me** reference catalog — improve GPS accuracy for radius filter

## [1.5.12] - 2026-07-06

### Added

- **Cave AI web handoff** — open `caveaipro.com/ai` in the default browser with reference-catalog context (Android `isCaveAiWebDeepLink` parity); entry points from Main window and Reference Catalog

### Changed

- **Surface map** — bundled MapLibre assets synced from caveaipro.com website (viewport snapback, layer prefs)

### Fixed

- **SURVEY QC offline hints** — `OfflineBrainHintText` binding on Reference Catalog detail panel
- **MSIX manifest** — remove invalid duplicate file-type extensions and redundant location capability (Store packaging)

## [1.5.11] - 2026-07-02

### Fixed

- **Public Library WebView** — OAuth popups via `WebView2AuthPopupHost.WirePopupHandling`; drop custom User-Agent suffix that broke Google sign-in

## [1.5.10] - 2026-07-02

### Fixed

- **Release notes extraction** — match dated CHANGELOG headers (`## [x.y.z] - date`) when preparing GitHub Release body
- **Directory.Build.props** — align `Version` / assembly metadata with v1.5.10 release tag (was stuck at 1.5.5)

## [1.5.9] - 2026-07-02

### Fixed

- **Velopack packaging** — use vpk 1.2.0 `--shortcuts` flag only; drop unsupported `--shortcutLocations` and `--installDir` that broke Release workflow after MSI succeeded

## [1.5.8] - 2026-07-02

### Fixed

- **WiX MSI packaging** — trigger post-install launch from ExitDialog Finish via `Publish` instead of invalid `InstallUISequence/InstallFinalize` (WIX0094 on WiX v5)
- **Velopack packaging** — update `vpk pack` flags for CLI 1.2.0 (`--shortcuts`, drop removed `--installDir` / `--shortcutLocations`)

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

## [1.5.6] - 2026-07-02

### Fixed

- **Reference favorites parity** — `CaveFavoriteService` merges `users/{uid}/favorites` and `users/{uid}/saved_caves` on load; reference stars write to `saved_caves` (web/Android parity)
- **Release pipeline** — pass `CAVEAIPRO_FIREBASE_API_KEY` into the release test step after config inject
- **WiX MSI packaging** — use `Execute="immediate"` for post-install launch custom action (WiX v4+)

## [1.5.5] - 2026-06-30

### Added

- **Explore ↔ Surface layer sync** — hillshade / Copernicus toggles shared via `layers=` URL query and `SurfaceMapLayerPrefsSync`
- **`CloudCommandsViewModel`** — publish retry queue + local publish history slice extracted from `MainViewModel`
- **Command palette** additions: Open project, Surface map tab, Export plan DXF
- **Compare backups** overlay color legend (File A solid / File B semi-transparent)
- **Surface map** elevation panel collapse toggle
- **Field Trip Planner** map loading overlay, 20s watchdog, clearer WebView2 error fallback
- **MSI** optional “Launch CAVE AI PRO” checkbox on install finish dialog
- **`docs/WINDOWS_SMOKE_TEST.md`** — manual QA checklist for Windows releases
- **`tools/sync-surface-map-from-website.ps1`** — copy bundled surface-map from website repo
- Tests: `SurfaceMapLayerPrefsSyncTests`, `PublishedCaveSyncPayload` allow-list tests

### Changed

- **Tab groups** visual polish: spacing, hover/active states, tooltips
- **Status chip** click opens sign-in or LEGAL & SETTINGS when action is needed
- **Preferences** path validation and browse-folder defaults; missing folders highlighted
- **Dark theme** implicit `TextBox` / `ComboBox` styles in `Theme.xaml`
- **Surface map** bundled JS/CSS synced from caveaipro.com (corridor jitter, X-ray fixes)

### Fixed

- **Cloud publish Firestore PATCH** — owner sync now uses rule-safe keys only (`PublishedCaveSyncPayload`); gallery → `imageUrls`, AI map → `cartographyImageUrls`, survey → `surveyJsonUrl`, timestamp → `lastSyncedAtMs`
- **Project unload before open** — prevents stale workspace when opening a new backup while another project is loaded
- Explore map open no longer overwrites persisted viewport URL before navigation
- Field trip map placeholder stays visible during slow WebView2 init

### Previously in this release train

- **Field Trip Planner** MapLibre mini preview (WebView2 + OSM) replacing flat canvas schematic
- **Surface map** WPF elevation profile panel wired from `elevationProfile` bridge message
- **Main window tab groups**: Survey | Library | Publish | Settings (filters visible tabs; internal tab names unchanged)
- **Command palette** (`Ctrl+K`): categories, fuzzy scoring, 16 commands including retry publish
- **Compare backups** overlay diff mode (semi-transparent A+B on plan canvas)
- **Cloud publish retry queue** (`cloud-publish-retry-queue.json`) — retry failed uploads without full re-publish
- **Reference catalog** map pins use diamond markers (web/Android parity)
- **Surface PNG export** legend strip (entrance, corridor, LiDAR extent)
- `MainViewModel.CloudCommands` partial — cloud retry queue slice
- Tests: `CloudPublishRetryStoreTests`, `CompareBackupPlanPreviewTests`, `PlanExportGoldenTests`
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

- Explore layer sync documented in `docs/cross-platform-contract.md`; Surface map JS→WPF checkbox sync on `mapState`
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

