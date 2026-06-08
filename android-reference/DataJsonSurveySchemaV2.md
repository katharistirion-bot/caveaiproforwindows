# CaveAI Pro — `data.json` survey enrichment (schema v2)

This document is the **cross-platform contract** between **CaveAI Pro (Android)** backup export and **CAVE AI PRO (Windows)**.  
Implement the fields on Android (Gson), then Windows maps them 1:1 via `System.Text.Json` on `CaveProjectDocument` / `ShotRecord` / nested DTOs.

**Versioning:** set top-level `surveyArchiveSchemaVersion` to `"2"` (or higher) when any v2 block is present. Windows accepts older archives unchanged.

---

## 1. Project — device & calibration & AI

| JSON key | Type | Description |
|----------|------|-------------|
| `exportDeviceContext` | object | Device / build identity for audit & support. |
| `surveyCalibrationProfile` | object | Active compass / sensor calibration snapshot used during capture. |
| `surveyAiClassifications` | array | Optional on-device AI labels (shots, stations, sketches, etc.). |
| `stationEnvironmentSnapshots` | array | Per-station environmental / QC samples (not necessarily tied to one leg). |

### `exportDeviceContext`

| Field | Type | Notes |
|-------|------|-------|
| `manufacturer`, `model`, `device` | string | `device` = marketing name if distinct from `model`. |
| `androidSdkInt` | number | Integer API level. |
| `appVersionName`, `appVersionCode` | string / number | Play build identity. |
| `exportEngineVersion` | string | Optional exporter module version. |

### `surveyCalibrationProfile`

| Field | Type | Notes |
|-------|------|-------|
| `profileId`, `profileName` | string | User-visible calibration preset. |
| `calibratedAtUtcMs` | number | When this profile was last fitted. |
| `magneticDeclinationAppliedDeg` | number | Declination applied at capture (degrees). |
| `compassCalibrationJson` | object | **Opaque** soft/hard iron, bias, temperature coeffs — structure is app-defined; Windows stores and displays raw JSON. |
| `tapeCalibrationScale` | number | Optional multiplicative tape correction (1.0 = nominal). |

### `surveyAiClassifications[]`

| Field | Type | Notes |
|-------|------|-------|
| `entityType` | string | e.g. `shot`, `station`, `sketchStroke`, `project`. |
| `entityRef` | string | Stable id: shot `id`, station name, sketch path index, etc. |
| `label`, `labelLocale` | string | Machine label + optional UI locale string. |
| `confidence` | number | 0…1 when available. |
| `modelId`, `modelVersion` | string | On-device model identity. |

---

## 2. Shots — telemetry, timestamps, instrument QC

All fields are **optional**; omit when unknown.

| JSON key | Type | Description |
|----------|------|-------------|
| `timestampUtcMs` | number | Primary capture instant (UTC ms) — **already** used on v1. |
| `measurementStartedUtcMs`, `measurementCompletedUtcMs` | number | Full measurement window (compass settle, tape, clino). |
| `compassSampleVarianceDeg2` | number | Sample variance of compass/azimuth during hold (deg²). |
| `clinoSampleVarianceDeg2` | number | Sample variance of clinometer during hold (deg²). |
| `compassStdDeg`, `clinoStdDeg` | number | Reported std dev or residual after calibration (degrees). |
| `tapeStdM` | number | Estimated tape reading std dev (metres). |
| `sensorFusionQuality` | number | 0…1 fused attitude quality if IMU-assisted. |
| `horizontalPositionAccuracyM`, `verticalPositionAccuracyM` | number | GNSS / fused position accuracy when tied to a fix. |

Existing v1 sensor fields (`ambientBleTempCelsius`, `manualAmbientTempCelsius`, humidity, CO₂, etc.) remain authoritative for **environment**.

---

## 3. Sketches / section sketches — rich stroke metadata

Each element of `sketches[]` / `sectionSketches[]` that represents a drawable stroke **may** include:

| Field | Type | Description |
|-------|------|-------------|
| `strokeWidthPx` | number | Screen stroke width in pixels at export resolution. |
| `strokeWidthSurveyM` | number | Optional world-space width (metres) if known. |
| `strokeColorArgb` | number | `0xAARRGGBB` packed integer. |
| `strokeColor` | string | Alternative `#RRGGBB` or `#AARRGGBB`. |
| `layerIndex`, `layerZOrder` | number | Ordering: higher draws on top. |
| `layerName` | string | User layer name. |
| `textAnnotations` | array | `{ "text", "surveyX", "surveyY", "z", "fontSizePx", "rotationDeg" }` |
| `brushProfile` | string | e.g. `pencil`, `ink`, `highlighter`. |

**Paths:** keep existing point arrays / polylines as today; add the metadata alongside so Windows and AI can consume both geometry and style.

---

## 5. X-ray / satellite backdrop (geo calibration)

Written during ZIP backup when an embedded X-ray Google Map snapshot is captured. Windows aligns the survey traverse to the satellite pixel grid using these fields.

| JSON key | Type | Description |
|----------|------|-------------|
| `xrayBackdropImageUri` | string | Relative path inside the backup ZIP (e.g. `maps/xray/_survey_capture_<project>_XRAY.png`) or HTTPS/local URI when saved from the map screen. |
| `xrayBackdropImageBounds` | object | WGS-84 geographic extent of the bitmap. |

### `xrayBackdropImageBounds`

Preferred shape (matches Windows fixture and `XRayBackdropMetadataParser`):

| Field | Type | Notes |
|-------|------|-------|
| `minLat`, `maxLat`, `minLon`, `maxLon` | number | South/north latitude and west/east longitude in degrees. |

Alternative shapes accepted on import (Windows only): `north`/`south`/`east`/`west` scalars, or `northEast` / `southWest` objects with `{ "lat", "lon" }`.

Bounds are captured from the live X-ray tab [GoogleMap] visible region at snapshot time (`projection.visibleRegion.latLngBounds`).

When either field is present, bump `surveyArchiveSchemaVersion` to **2**.

---

## 6. ZIP / integrity

No change to ZIP layout. After extending `data.json`, regenerate `integrity_manifest.json` and keep `backup_manifest.json` in sync.

See `CaveProjectDataJsonEnrichment.kt` for **merge-order** when patching a `JSONObject` before writing the ZIP.
