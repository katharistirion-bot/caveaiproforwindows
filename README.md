# CAVE AI PRO — PC companion

> **Install:** [docs/INSTALL.md](docs/INSTALL.md) — download from [caveaipro.com](https://www.caveaipro.com/#windows-download) (Windows companion for active subscribers).

Desktop companion for **CaveAI Pro (Android)**: loads the **same** speleological data the mobile app exports or saves, and processes it on PC (survey summary, shot CSV export, plan printing, Survex/Therion/DXF exports).

### Distribution (Play Store vs PC)

- **CaveAI Pro (Android):** distributed via **Google Play** (as defined by the publisher).
- **CAVE AI PRO (PC, this repo):** **not** on Google Play; runs on **Windows PC** (x64, Win 10/11). **Public download** from [caveaipro.com](https://www.caveaipro.com/#windows-download) (Velopack Setup.exe / MSI). **Microsoft Store** listing is in preparation — see `docs/MICROSOFT-STORE.md`. If you only have the mobile app from Play, you need a **separate** PC installer from the website or (when available) the Microsoft Store.
- Legal / store wording (English): `legal/00-DISTRIBUTION-PLATFORMS.md`.
- Build channels: `docs/INSTALL.md` — sideload (`package-release.ps1`) vs Store (`PublishProfile=MicrosoftStore-Win64`, `STORE_DISTRIBUTION`).

## Working with the Android app

| Android source | What the PC app does |
|----------------|----------------------|
| **Backup ZIP** (`Downloads/CaveAI/CaveAI_Backup_*.zip`) | Reads `data.json` inside the ZIP (same as `ProjectBackupZip` in the `CaveAIPro` repo). |
| **`caveai_database_v1.json`** (copied from app internal files) | Same structure: list of `CaveProject` objects in JSON. |

After opening: auto-select first project (if any) → summary → **Plan** tab (centerline + `vectorLines` as on Android) → **Print plan…** (landscape print preview) → **Export shots → CSV** (with LRUD columns), **Export traverse → Survex (.svx)**, or (ZIP only) **Extract photos from ZIP…**.

### ZIP integrity

After opening a CaveAI backup `.zip`, a status line compares SHA-256 hashes from `integrity_manifest.json` against ZIP contents (same as Android export).

### What the Windows app reads from a backup ZIP

When you open a **CaveAI backup `.zip`**, the Windows app reads **`data.json`** first (list of `CaveProject` as on Android). For **Save ZIP / export current cave only** on Android, the contract is **one project per ZIP** (not the full database); see `android-reference/MapsWindowsSync.md` §0. Other files stay inside the ZIP; Windows opens them **on demand** (e.g. map underlay, photo export, Explorer from the Maps tab). The list below is **per project / cave** as in the JSON (repeated for each project if the ZIP contains several).

#### Files inside the `.zip`

| File / folder | Role |
|---------------|------|
| **`data.json`** | All structural survey data (Gson format as on mobile). |
| **`photos/`** | Image files referenced in JSON as `photos/…` paths (mainly shot photos). |
| **`export_assets/`** | Other local files Android embeds in the ZIP (shot audio, Geo/Bio images, LIDAR/mesh, etc.) — `export_assets/…` paths in `data.json`. **Maps:** ideally each map in its **own subfolder** inside the ZIP (e.g. `export_assets/maps/MyCave__a1b2c3d4e5/000_plan_raster_89abcdef/basemap.tif`) so they do not collide; see `android-reference/CaveAiProBackupZipMapExport.kt`. **Android ↔ Windows map sync (backup `.zip` only):** `android-reference/MapsWindowsSync.md`. **For PC / file workflows:** prefer **raster** (`.png`, `.tif`, `.jpg`, `.webp`, `.bmp`); **PDF** (first page) is also supported as an underlay on Windows. |
| **`backup_manifest.json`** | Export metadata (version, time, current-project / all-projects mode). |
| **`integrity_manifest.json`** | SHA-256 per file in the ZIP; Windows verifies integrity. |
| **`map_inventory.json`** | *(Newer Android export)* Detailed map asset paths per cave — shown on the **Backup detail** tab. |
| **`README.txt`** | Brief explanation of backup structure. |

#### Logical sections in `data.json` per cave

Fields explicitly named in the Windows model (`CaveProjectDocument` / `ShotRecord`) plus **remaining JSON** (`ExtensionData`) — Android may write additional keys; the PC preserves and scans them where needed (maps, catalogs).

1. **Project identity & metadata** — `name`, `date`, `startTime`, `endTime`, entrance coords (`lat`, `lon`, `alt`), `shots`, `vectorLines`, `rocks`, `fieldCatalogEntries`, and via **ExtensionData** e.g. `exitLat`/`exitLon`/`exitAlt`, `trackPoints`, `linkedLibraryCaveId`, `surveyEventLog`, `vehiclePark*`, sketches, `mapSymbols`, `brackets`, etc. (as in Android `CaveSurveyModels.kt`).

2. **Shots (`shots[]`)** — Traverse geometry (`fromStation`, `toStation`, `distance`, `azimuth`, `clino`), LRUD, `notes`, `depth`, `symbol`, splays/radials. Also in the same JSON object: **`photos[]`**, **`audioMemoUri`**, BLE / manual environmental readings.

3. **Geo/Bio — rock samples (`rocks[]`)** — e.g. `imageUri`, `station`, analysis title/description, `planMapX` / `planMapY`.

4. **Field catalog / organisms (`fieldCatalogEntries[]`)** — Category, name, notes, scientific name, abundance, `photoReference`, map position.

5. **Cartography & plan** — `vectorLines` (plan/section/profile lines), map/overlay URIs in JSON (collected dynamically for the **Maps** tab).

#### What Windows does with this data

| Data / file | Windows usage |
|-------------|---------------|
| `data.json` (full project) | Load projects, survey summary, shot list, **Export shots → CSV**, **Export traverse → Survex (.svx)**. |
| `shots` + `vectorLines` | **Plan** / **Section** tabs (centerline + vectors), plan printing where available. |
| Paths `photos/…`, `export_assets/…`, `https://…` in JSON | **Maps** tab (asset list, Explorer, file export, **raster underlay** when decoded). |
| `rocks` + `fieldCatalogEntries` | **Cave registry** and **Bio/Mineral catalog**. |
| `integrity_manifest.json` + ZIP contents | **Integrity check** (status banner). |
| **`photos/`** folder only | **Extract photos from ZIP…** (copies `photos/**` to a folder you choose). `export_assets/**` is not copied by this command — use Maps tab / per-asset Extract. |

**Note:** Opening **only** `caveai_database_v1.json` without a ZIP means local files that were `content://` on mobile are not on PC — you need a **full ZIP** with `photos/` and `export_assets/` for the same file paths to work.

### Survex

The `.svx` file contains only **traverse** shots (`*data normal from to tape compass clino`) and a provisional `*fix` at station `0 0 0` — **verify** conventions/units before production use (Survex/Cavern).

## Build / Run

### Standard Windows install (MSI)

For a normal app install (no CMD window), build the installer and run the MSI:

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on the build machine.
2. Double-click **`BuildInstaller.bat`** (or `dotnet build installer\CaveAiProForWindows.Installer.wixproj -c Release`).
3. Run **`installer\bin\Release\CaveAiProForWindows-Setup.msi`** on a **Windows PC** (not from Google Play): installs to Program Files, Start menu shortcut, self-contained (no separate .NET on target). Uninstall via Settings → Apps.

**`run.bat`** launches the installed app (MSI or prior per-user install) without keeping a console open.

If the app does not start: try **`TryApp.bat`** (self-contained **folder**, not single-file). On error, check **`%LOCALAPPDATA%\CaveAiProForWindows\last-error.txt`** (full stack trace) and **`startup.log`** in the same folder. Re-run **`BuildInstaller.bat`** and reinstall MSI.

### Development from source

```bat
cd /d d:\caveaiproforwindows
dotnet build
dotnet run --project src\CaveAiProForWindows\CaveAiProForWindows.csproj
```

Alternatively: **`Install.bat`** / **`Install-SelfContained.bat`** (PowerShell) for per-user install without MSI.

### Release packaging (sideload)

Set `CAVEAIPRO_FIREBASE_API_KEY` (or run `tools/inject-firebase-config.ps1`), then publish and package:

```powershell
dotnet publish src\CaveAiProForWindows\CaveAiProForWindows.csproj -c Release -r win-x64 -p:PublishProfile=ReleaseSingleFile-Win64
.\tools\package-release.ps1 -Tag v1.3.0
```

See `docs/SECURITY.md` for Firebase config injection and `docs/INSTALL.md` for distribution channels.

## Code layout

- `Models/` — field subset compatible with Android Gson (`Shot`, `CaveProject`).
- `Services/ExplorationDataLoader.cs` — `*.json` or `*.zip`.
- `Services/ExplorationAnalytics.cs` — summary + CSV UTF-8 BOM.
- `Services/IntegrityVerifier.cs` — `integrity_manifest.json` verification.
- `Services/ZipPhotoExtractor.cs` — extract `photos/**` from ZIP.
- `Services/SurvexExporter.cs` — minimal `.svx` traverse centerline.
- `Services/SurveyStationGeometry.cs` — same station reduction as `calculateCaveCoordinates` (Android) + `vectorLines` for plan.
- `Views/PlanView.*` — 2D plan view.

## Roadmap: cave survey workstation (with CaveAI Pro — Android)

**Roles:** **mobile (`d:\caveaipro`)** remains the field recording hub (map, sketching, `vectorLines`, DXF, backup ZIP). **Windows** extends to a desktop workstation: same `data.json` / ZIP, summary, Survex/CSV, integrity, and progressively **plan + QC** on a large screen.

| Phase | Goal |
|-------|------|
| **1 — Data** | Full map-field reading from JSON (`vectorLines` already recognized; summary with line counts). Android: stable exports / schema version where needed. |
| **2 — PC plan** | 2D plan canvas (same survey frame as app): centerline from shots + `vectorLines` by `viewMode`. |
| **3 — Exports** | DXF/SVG aligned with `DxfExport.kt`; improved Survex (units, stations); optional Therion `.th` later. |
| **4 — QC** | Loop closure / length / hints aligned with Android logic where available. |

Near-term code ideas: Therion `.th`, richer `vectorLines`/`mapSymbols` import, DXF Windows↔Android alignment.

### CaveAI Pro (Android)

Main manual: `caveaipro` repo → `docs/CARTOGRAPHY_AND_USAGE_GUIDE.md` (survey frame, Map tab, exports). `VectorLine` / `CaveProject` model: `app/.../CaveSurveyModels.kt`.
