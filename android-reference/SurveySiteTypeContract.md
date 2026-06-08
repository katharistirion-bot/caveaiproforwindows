# Survey site type — cross-platform contract (CAVE / MINE / POTHOLE / SPRING)

**Scope:** CaveAI Pro **Android**, **CAVE AI PRO (Windows)**, **Public Library (web/Firestore)**.

Users must see on every cartography export whether the survey is a **cave (σπήλαιο)**, **mine (ορυχείο)**, **pothole (λάκκα)**, or **spring (πηγή)**.

---

## Canonical values

| Token (`surveySiteType` / normalized `KnownCave.type`) | English map label | Greek map label |
|------------------------------------------------------|-------------------|-----------------|
| `CAVE` | Cave | Σπήλαιο |
| `MINE` | Mine | Ορύχειο |
| `POTHOLE` | Pothole | Λάκκα |
| `SPRING` | Spring | Πηγή |

Android UI may use title case (`"Cave"`, `"Mine"`, …) in Cave Library; **backup JSON must store uppercase tokens** after normalization.

---

## JSON fields

### `data.json` → each `CaveProject`

| Key | Type | Notes |
|-----|------|-------|
| `surveySiteType` | string | Optional. One of `CAVE`, `MINE`, `POTHOLE`, `SPRING`. |

When omitted, Windows resolves from linked `cave_library.json` row (`linkedLibraryCaveId` or matching `name`) and defaults to `CAVE`.

Legacy alias keys (read-only on Windows): `siteType`, `caveType`, `type` inside project object.

### `cave_library.json` → each `KnownCave`

Existing field `type` (Android) — keep in sync with `CaveProject.surveySiteType` for linked surveys.

### Firestore `published_caves`

Existing field `caveType` — same token set.

---

## Android implementation checklist

1. Add `var surveySiteType: String = ""` to `CaveProject` (`CaveSurveyModels.kt`).
2. On project save / library sync: copy normalized type from linked `KnownCave.type` → `CaveProject.surveySiteType`.
3. Map raster exports (`MainActivityMapRasterExports.kt`, `Map3DObliqueExport.kt`, PDF): draw site label on title block.
4. Plan / section Compose canvas: include site label near project name (optional follow-up).

---

## Windows implementation

- `CaveProjectDocument.surveySiteType`
- `SurveySiteType` / `SurveySiteTypeResolver`
- Title plate on Plan / Section / print / SVG exports
- Enrichment on ZIP load from `cave_library.json`

Bump `surveyArchiveSchemaVersion` to **3** when Android starts writing `surveySiteType` routinely.
