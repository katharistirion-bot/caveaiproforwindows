# Maps: Android ↔ Windows sync — **ZIP backup only**

**Scope:** This document covers **only** the **CaveAI Pro backup `.zip`** (the same file you open on Windows). It does **not** define a “`data.json` without ZIP” flow, or standalone map files from File → Open — those are separate workflows on the PC.

Goal: whatever Android **embeds in the ZIP** for cartography should **appear** on the **Maps** tab, **extract** from the ZIP (temp / Explorer), and — where it is raster/PDF — **work as a Plan/Section underlay** when the **same** `.zip` is open in the companion.

**Source of truth for ZIP maps:** `MapAssetsCollector` (paths inside the ZIP’s `data.json`), `ZipMapInventoryReader`, `MapAssetOpener`, `PlanMapUnderlayLoader`, `RasterImageDecoder`.

**Rich survey JSON (AI / QC / strokes):** `DataJsonSurveySchemaV2.md` + `CaveProjectDataJsonEnrichment.kt` — extends `data.json` beyond maps; Windows deserializes all new fields into `CaveProjectDocument` / `ShotRecord`.

---

## 0. Save ZIP — **only the cave that is open at that moment**

When the user **saves / exports a backup ZIP** for the **current** survey, the ZIP must contain **only that one cave/project** — not other surveys from the device.

| What | Requirement |
|------|-------------|
| **`data.json`** | Must contain **exactly one** entry in the top-level `CaveProject[]` array (or an equivalent single-project wrapper already supported by `ExplorationDataLoader`). **Not** a full export of every cave in the database inside this ZIP flow. |
| **`map_inventory.json`** | If present: `projects[]` must have **one** entry — the current `projectName` / `projectDate`. |
| **`photos/`**, **`export_assets/`** | Only files and paths that **belong** to this project (no photos/maps from other caves). |
| **`integrity_manifest.json`** | Must cover **exactly** the entries packed into this ZIP (no orphan hashes from other projects). |

If the Android product also supports an “export all” ZIP, that is a **separate** command/role; the **Save ZIP / backup current cave** flow is defined here as a **single-project archive** for clarity with the Windows companion.

---

## 1. ZIP and paths

- Windows loads projects from **`data.json` as a ZIP entry** (not from an external file for this contract).
- In `data.json` **inside the ZIP**, **all** map paths that point at a file **in the same ZIP** must be **relative** with forward slashes, e.g.  
  `export_assets/maps/MyCave__abc1234567/000_plan_raster_deadbeef/basemap.png`  
  **Not** `content://…` — Windows **does not** resolve those **from a ZIP**.
- ZIP entry names must **match** exactly (with `/`) what the JSON writes; prefer **case-sensitive** writers (Windows does a case-insensitive fallback where it can).
- Copy + path rewrite implementation: `CaveAiProBackupZipMapExport.kt` (`bundleMapsForZipProject`).
- **PC companion (Windows):** If `data.json` has a **leaf-only** TIFF (e.g. `0001_site.tif`) without a full path, `PlanMapUnderlayLoader` searches beside the opened `.json` or `.zip` under folders such as `cartography`, … (see code). For `maps/plan/`, `maps/section/` trees with a marker file **`_CaveAI_folder_marker.txt`** in the same folder as the rasters, `CaveMapsMarkerPathResolver` is used (recursive marker find + relative path join). Debug: `[MapsMarker]`, `[PlanMapUnderlay]`.

---

## 2. `map_inventory.json` (optional but recommended)

ZIP root file: **`map_inventory.json`** (or nested with leaf name `map_inventory.json` — Windows finds it via `NormalizeZipEntryPath`).

### JSON schema (strict — matches `ZipMapInventoryReader`)

```json
{
  "projects": [
    {
      "projectName": "Same string as CaveProject.name in data.json",
      "projectDate": "Same string as CaveProject.date in data.json",
      "maps": [
        {
          "slot": "human label, e.g. plan_raster",
          "pathInBackupOrUrl": "export_assets/maps/.../file.png or https://...",
          "storageKind": "zip | https | file"
        }
      ]
    }
  ]
}
```

- **`projectName` / `projectDate`**: must **match** the project in `data.json` (Windows does a case-insensitive compare of `projectName` to `CaveProjectDocument.Name`). Mismatch = inventory row **does not** bind to the cave on Plan.
- **`pathInBackupOrUrl`**: for local files inside the ZIP, same path as the ZIP entry.
- **`storageKind`**: informational for UI; for files inside the ZIP use `"zip"`.

In **`backup_manifest.json`**, set `"includes_map_inventory": true` when inventory is present (`BackupManifestReader` on Windows shows it).

---

## 3. `data.json` (ZIP entry) — cartography keys

These are **explicit** in `MapAssetsCollector` (ExtensionData / structures):

| Source | Description |
|--------|-------------|
| `publicLibraryCartographyUris` | Array of strings (URI/path per index). |
| `cartographyTlsMeshObjUri` | String (often `.obj`). |
| `surfaceLidarRaster` | Object/array — recursive string harvest. |
| `mapSymbols`, `sketches`, `sectionSketches`, `trackPoints` | Deep harvest paths (nested `uri`/`path`/image etc. — see `HarvestAssetStrings`). |
| `vectorLines` | Deep harvest (embedded refs if any). |
| `rocks[].imageUri` | For Maps when a full list is needed (and CSV report). |
| `fieldCatalogEntries[].photoReference` | If it looks like an asset path/URI. |

Additionally, **any** ExtensionData key that:

- contains `cartograph`, `raster`, `lidar`, `basemap`, `ortho`, `geojson`, `kml`, or `layer`+`map`, or
- matches `LooksLikeMapRelatedKey` (e.g. `…MapUri`, `plan…image…`),

is scanned with `CollectFromJsonValue` (strings, arrays, objects).

Finally, string values on keys ending in **`Uri` / `Url`** or containing a path with `/`, `http`, `file:`, etc. are added if they pass `IsProbableAssetPathOrUri`.

---

## 4. File formats inside the ZIP (underlay)

When the path points at a file **inside the open backup ZIP**, **Plan/Section underlay** uses `RasterImageDecoder`: PNG, JPEG, WebP, GIF, BMP, TIFF/GeoTIFF (many variants), **PDF (first page)**.

For a **smaller and more predictable** ZIP, prefer **PNG or TIFF** for basemap; PDF is allowed.

---

## 5. Android checklist (maps **inside the ZIP**)

1. [ ] On ZIP export: for each local map, **copy bytes** under `export_assets/maps/...`.
2. [ ] **Replace** in the Gson JSON every `content://` / internal path with the new `export_assets/maps/...` path.
3. [ ] (Recommended) **`map_inventory.json`** with `projects[]` as above, `includes_map_inventory` on the manifest.
4. [ ] **`integrity_manifest.json`** updated for all new entries.
5. [ ] **`projectName`/`projectDate`** in inventory = same as `data.json` for that cave.

If you follow the above, cartography **inside the ZIP** from Android **stays in sync** with what the Windows companion reads **when you open the `.zip`** — without changing the Gson model beyond paths + optional inventory.
