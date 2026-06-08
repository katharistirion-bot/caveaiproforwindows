# Android map sync contract (Windows companion)

English summary of how **CaveAI Pro (Android)** map geometry in backup `data.json` is parsed and rendered on **CAVE AI PRO (Windows)**. For ZIP map assets (rasters, inventory), see `android-reference/MapsWindowsSync.md`.

## View modes

| `viewMode` | Android tab | Windows entry points |
|------------|-------------|----------------------|
| `0` (or **omitted**) | Plan | `ParsePlanMapSymbols`, `ParsePlanVectorLines`, `PlanSceneBuilder` |
| `1` | Section / extended elevation | `ParseSectionMapSymbols`, `ExtendedElevationSceneBuilder`, `SectionSceneBuilder` |
| `3` | Long profile | `ParseVectorLinesForViewMode(..., 3)`, `LongProfileSceneBuilder` |

When `viewMode` is **omitted**, Windows treats the entry as **plan-only** (same rule as Android Gson filters).

- **`strokeColorArgb`** / **`strokeColor`** / **`colorArgb`**: optional packed `0xAARRGGBB` integer or `#RRGGBB` / `#AARRGGBB` hex string. Windows parser: `SurveyStationGeometry.ResolveStrokeColorArgb`; plan renderer: `PlanCanvasRenderer` uses exported colour when present, else Android-aligned defaults.

## `vectorLines[]`

Each element is an object with:

- **Points** (one of): `points`, `path`, `strokePoints`, `vertices`, `polyline`, `coordinates` (GeoJSON nested arrays), `coords`, `trail`, `trace` — or nested under `geometry`.
- **Type** (one of): `type`, `stroke`, `strokeType`, `kind`, `objectType`, `category`, `mapObjectType` (default `vec`).
- **`viewMode`**: optional number; omitted ⇒ plan.
- **`closed`**: optional boolean.
- **Sharp rendering**: `preferSharpPolyline` / `sharp`, or inferred from type tags containing `stroke`, `pen`, `wallOutline`, `sketchLayer`.

**Windows parser:** `SurveyStationGeometry.ParseVectorLinesForViewMode`.

**Windows renderer:** `PlanCanvasRenderer` — plan overlays use teal/cyan (`Map.VectorStroke`), section/profile types use blue/green profile strokes, wall outlines use wall brown. `PreferSharpPolyline` draws vertex-faithful polylines (no Catmull–Rom smoothing).

## `mapSymbols` / `mapObjects` / extension keys

Symbol stamps are collected from:

- Top-level: `mapSymbols`, `mapObjects`
- ExtensionData: `symbolsLayer`, `sketchObjects`, `planMapSymbols`, `planSymbols`, `sketchSymbols`, `mapStampSymbols`, `androidSymbols`, `planSymbolLayer`, …

**Position** (survey metres): `surveyX`+`surveyY`, `east`+`north`, `easting`+`northing`, `x`+`y`, or nested `position`.

**Identity** (aliases): `symbolId`, `symbol`, `type`, `icon`, `iconKey`, `iconUri`, `stamp`, `kind`.

**Scale:** dimensionless `scale`, `iconScale`, `size`, `stampScale`; metre footprint `widthSurveyM`, `scaleSurveyMetres`, `symbolSpanMetres`, …

**`mapObjects` mixed arrays:** entries with 2+ polyline vertices are strokes (parsed as sketches); single-anchor symbol rows are stamps.

**Windows parsers:** `ParsePlanMapSymbols`, `ParseSectionMapSymbols`, `ParsePlanSketches`.

**Windows visuals:** `AndroidMapSymbolVisualFactory` — diameter from `ScaleSurveyMetres` or `scale × DefaultSymbolWorldSpanMetres`, transformed with `PlanCanvasSurveyLayout.PxPerMetre`.

## Section sketches

`sectionSketches[]` supplies section-wall strokes. Plan-context section lines also appear in `vectorLines` with `viewMode: 1`.

## Round-trip (Windows → Android JSON)

`DesignLayerMapObjectsSerializer` writes Android-compatible `mapObjects` strokes (`kind`, `viewMode`, `points`, `sourceClient`) and symbol stamps (`surveyX`, `surveyY`, `symbolId`, `viewMode`, `scale`). Merged `sketches[]` keeps Android `sourceClient: CaveAiProAndroid` rows.

## Contract tests

- Fixture: `tests/CaveAiProForWindows.Tests/Fixtures/android-vector-map-symbols-sample.json`
- Fixtures (stroke colours / mapObjects round-trip): `android-vector-stroke-colors.json`, `android-mapobjects-strokes-roundtrip.json`
- Tests: `AndroidMapParityTests.cs` (viewMode filtering, point keys, mixed `mapObjects`, long-profile vectors, `strokeColorArgb` / hex colours, serializer re-parse)

## Related code

| Area | File |
|------|------|
| Parse / models | `Services/SurveyStationGeometry.cs` |
| Plan scene | `Services/PlanSceneBuilder.cs` |
| Section scene | `Services/ExtendedElevationSceneBuilder.cs` |
| Long profile | `Services/LongProfileSceneBuilder.cs` |
| Canvas draw | `Views/PlanCanvasRenderer.cs` |
| Symbol factory | `Services/AndroidMapSymbolVisualFactory.cs` |
| Symbol ID map | `Services/AndroidSketchSymbolKindMapper.cs` |
| Save design layer | `Services/Persistence/DesignLayerMapObjectsSerializer.cs` |
