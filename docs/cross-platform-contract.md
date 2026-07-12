# Cross-platform contract (Android · Windows · Web)

Normative interoperability spec for **CaveAI Pro (Android)**, **CAVE AI PRO (Windows)**, and **caveaipro.com**. For Android ↔ Windows backup/sync details see [sync-contract.md](./sync-contract.md).

Site origin for share URLs: **`https://www.caveaipro.com`**

---

## Reference catalog CDN URLs

| Resource | URL |
|----------|-----|
| Meta | `https://www.caveaipro.com/data/reference-catalog-meta.json` |
| Search index | `https://www.caveaipro.com/data/reference-caves-search-index.json` |
| Country shards manifest | `https://www.caveaipro.com/data/reference-shards/manifest.json` |
| Country shard | `https://www.caveaipro.com/data/reference-shards/{country-slug}.json` |
| Featured | `https://www.caveaipro.com/data/featured-reference-caves.json` |

---

## `referenceCatalogLink` (survey project JSON)

Stored on each survey project in `data.json` under the key **`referenceCatalogLink`** (Windows ExtensionData; Android top-level Gson field).

```json
{
  "id": "osm-node-123456",
  "name": "Cave name",
  "country": "Greece",
  "lat": 39.0742,
  "lon": 21.8243,
  "refCode": "GR-001",
  "osmType": "node",
  "osmId": 123456,
  "linkedAt": "2026-06-15T12:00:00Z"
}
```

| Field | Type | Notes |
|-------|------|-------|
| `id` | string | Reference catalog pin id |
| `name` | string | Display name |
| `country` | string? | Country label |
| `lat`, `lon` | number | WGS84 |
| `refCode` | string? | Optional catalog code |
| `osmType` | string? | `node` / `way` / `relation` |
| `osmId` | number? | OSM id when known |
| `linkedAt` | ISO-8601 | When link was saved |

Related project fields (not inside `referenceCatalogLink`):

| Field | Purpose |
|-------|---------|
| `linkedLibraryCaveId` | Local Cave Library card id (Android/Windows); must match `KnownCave.id` for publish matching |
| `surveyArchiveSchemaVersion` | Survey archive version (currently `2`) |
| `windowsLastEditedAtMs` | Windows edit provenance in ExtensionData (optional) |

---

## Reference share URLs

```
https://www.caveaipro.com/cave/ref/{id}?country={country-slug}
```

- `{id}` — URL-encoded reference catalog id  
- `country` — optional hint for shard lookup (recommended)

### Start field survey (Android)

```
https://www.caveaipro.com/cave/ref/{id}?action=survey&country={country-slug}
```

- Android App Link / intent filter opens **Cave AI Pro** and creates or resumes a survey project with `referenceCatalogLink` set to `{id}`.
- Windows **Reference catalog** window opens this URL in the default browser for phone handoff (`ReferenceCatalogShareUrls.BuildSurveyStartUrl`).
- Web reference detail page exposes the same link as **Map with Cave AI Pro**.

Map deep link (legacy, still supported on Android):

```
https://www.caveaipro.com/map?ref=cave&id={id}
```

---

## Field trip share payload v1

Compact JSON for cross-app field trip handoff (web hash, Android/Windows clipboard URL).

```json
{
  "v": 1,
  "stops": [
    {
      "k": "r",
      "i": "reference-id",
      "n": "Stop name",
      "la": 39.07420,
      "lo": 21.82430,
      "c": "Greece"
    },
    {
      "k": "p",
      "i": "firestore-doc-id",
      "n": "Community cave",
      "la": 39.10000,
      "lo": 21.90000,
      "c": "Greece"
    }
  ]
}
```

| Stop field | Meaning |
|------------|---------|
| `k` | `"r"` = reference catalog, `"p"` = published community cave |
| `i` | Stop id (reference id or Firestore `published_caves` doc id) |
| `n` | Label (max 80 chars) |
| `la`, `lo` | Lat/lon rounded to 5 decimals |
| `c` | Country string |

### Share URLs

When JSON length ≤ **1800** characters:

```
https://www.caveaipro.com/map?view=fieldtrip#trip={base64-utf8-json}
```

When larger (web only — uses `localStorage` fallback):

```
https://www.caveaipro.com/map?view=fieldtrip&tripId={ft_id}
```

Implementation: `src/utils/fieldTripShare.js` (web), `FieldTripShare.kt` (Android), `FieldTripShareCodec.cs` (Windows).

---

## Explore terrain overlays (web map)

Canonical capability copy: website `src/data/exploreCapabilities.js` · UI: `MapExploreToolbar.jsx`, `ExploreCapabilitiesPanel.jsx`.

### Explore share URL contract (`view=explore`)

All Explore terrain handoffs use `/map?view=explore` with optional viewport and layer params.

Builder: website `src/utils/exploreMapShareUrl.js` → `buildExploreShareUrl` / `buildExploreViewportPath`  
Cave-centric helpers: website `src/utils/exploreMapLink.js` · Android: `ExploreMapUrls.kt` · Windows: `ReferenceCatalogShareUrls.cs`

| Param | Meaning |
|-------|---------|
| `view=explore` | Required — opens Explore terrain tab |
| `lat`, `lon`, `zoom` | Viewport center and zoom |
| `preset` | One of `terrain`, `discovery`, `light`, `speleo`, `speleo-full` — expands to layer set (see `EXPLORE_LAYER_PRESETS`) |
| `layers` | Comma-separated non-default flags: `hydrology=1,karst=1` (used when no preset, or Windows hydrology scout append) |
| `country` | Display country (web/Android) or slug (Windows, e.g. `greece`) |
| `filters` | Active filter chips, comma-separated |
| `embed=android` | Android WebView handoff (`exploreAndroidHandoff.js`) |
| `embed=windows` | Windows WebView2 handoff — App Check token bridge (`desktopAppCheckBridge.js`; see `docs/APPCHECK-WINDOWS.md`) |
| `ref`, `id`, `cave`, `name` | Pin handoff params for reference / community caves |

Golden test vectors (website exports JSON): `scripts/cross-platform-url-vectors.mjs` · run `npm run test:cross-platform-urls`. Mirror assertions in Android `ExploreMapUrlsTest.kt` and Windows `ReferenceCatalogTests.cs`.

### Static GeoJSON assets (`/data/explore/`)

| Kind | Path pattern | Countries (slug) |
|------|--------------|-------------------|
| Karst | `karst-{slug}.geojson` | Balkans + FR/ES/IT/GR (see website `exploreMapKarst.js`) |
| Hydrology (OSM) | `hydrology-{slug}.geojson` | `greece`, `italy`, `france`, `spain`, `croatia`, `slovenia` |
| Protected areas (OSM) | `natura-{slug}.geojson` | same six countries |
| Depression hints | `sinkhole-hints-{slug}.geojson` | `greece`, `italy` only |

Build: website `npm run build:explore-hydrology` · `npm run build:explore-natura` · validate: `npm run validate:explore-data`.

### Share URL layer keys (`exploreMapShareUrl.js`)

Boolean layer flags in `/map?view=explore` query (`layers=` param): only **non-default** values are encoded, e.g. `hydrology=1,karst=1`. Keys: `hillshade`, `copernicus`, `terrain3d`, `heatmap`, `karst`, `gaps`, `sinkholeHints`, `steepRelief`, `slopeZones`, `hydrology`, `naturaProtected`, `performanceMode`, `lightBasemap`, `pins`, `communityPins`, `detailOverlay`, etc. Presets: `terrain`, `discovery`, `light` (when preset is set, `layers=` is omitted — preset expands server-side).

Hydrology kind filters: `hydrologyKind_spring`, `hydrologyKind_sinkhole`, `hydrologyKind_cave_entrance`, `hydrologyKind_stream` (default all on).

### Cave AI map handoff

`/ai?explore=1&lat=&lon=&zoom=` — viewport context from Explore terrain (website `exploreMapAiHandoff.js`).

### Windows parity

Reference catalog and field trip planner open `https://www.caveaipro.com/map?view=explore` with terrain preset, hydrology scout layers, and field-trip viewport (`ReferenceCatalogShareUrls.cs`). Public Library Explore map WebView persists viewport URL including layer query params.

---

## Expedition share (subscriber map)

Opt-in team presence at a specific cave. **English UI only.** See website `docs/expedition-share-contract.md`.

- Collection: `expedition_shares/{leaderUid}`
- Read: active premium subscribers
- Write: team leader only (start / end sharing)
- Windows: active shares rendered on surface map layer

---

## Firestore `published_caves` optional fields

When publishing with a reference catalog match, all clients may set:

| Field | Type | Notes |
|-------|------|-------|
| `referenceCatalogId` | string | Reference pin id (≤ 200 chars) |
| `referenceCatalogCountry` | string | Country hint (≤ 120 chars) |

Set at create time from the linked `referenceCatalogLink` on the survey project (or user-selected suggestion). Not required for publish.

---

## Desktop Bridge ZIP

Android **Desktop Bridge** export (`mode: desktop_bridge_handoff`) includes:

- `data.json` — Gson array with `referenceCatalogLink`, `linkedLibraryCaveId`, `surveyArchiveSchemaVersion`
- `cave_library.json`, `backup_manifest.json`, `map_inventory.json`, media folders

Windows import preserves unknown Gson keys in `ExtensionData`. After Windows edit + Save:

- `windowsLastEditedAtMs` and `windowsEditorVersion` written to ExtensionData
- `referenceCatalogLink` preserved on round-trip

Android re-import of Windows-edited ZIP merges via `ZipPcEditedImportMerge` and preserves `referenceCatalogLink`.

---

## Public Library map depth pin colors

Reference catalog diamonds (and community dots where depth is known) use the same tier palette on **Web**, **Android**, and **Windows** native maps.

| Tier | Range (`depthM`) | Fill hex | Notes |
|------|------------------|----------|-------|
| Shallow | 0–50 m (exclusive upper bound at 50 → Medium) | `#1565c0` | Blue |
| Medium | 50–150 m | `#ef6c00` | Orange |
| Deep | >150 m | `#c62828` | Red |
| Unknown | missing, null, or ≤0 | `#ffb74d` | Amber reference diamond (community dots default to Shallow blue) |

Implementation:

- Web: `src/utils/mapDepthColors.js`, `CaveMap.jsx`, floating `CaveMapDepthLegend.jsx`
- Android: `PublicLibraryMapDepthColors.kt`, `PublicLibraryMapDepthLegend.kt`
- Windows: `ReferenceCatalogMapDepthColors.cs` (Reference catalog window, Plan/X-RAY reference overlays)

Reference **clusters** keep cyan bubble styling (`#00ffff`) for readability; only single pins are depth-tinted.

---

## Surface map (`surfaceMap`)

Shared MapLibre GL JS surface map for terrain around the cave entrance. **Phase 1** (Windows): embedded `Assets/surface-map/` in WebView2; Android uses OSM/Google in `SurfaceMapScreen.kt`. Web portal repo is out of scope until a sibling checkout exists.

### Host → map message (`type: "project"`)

Posted via WebView2 `PostWebMessageAsJson` when the active project changes.

```json
{
  "type": "project",
  "payload": {
    "name": "Cave name",
    "lat": 39.0742,
    "lon": 21.8243,
    "declinationDeg": 2.5,
    "surfaceLidarRaster": {
      "imageUrl": "https://caveai-surface-cache.local/project_abc/lidar.png",
      "southWestLat": 39.07,
      "southWestLon": 21.82,
      "northEastLat": 39.08,
      "northEastLon": 21.83,
      "opacity": 0.55
    },
    "surveyCorridor": {
      "type": "FeatureCollection",
      "features": [
        {
          "type": "Feature",
          "geometry": {
            "type": "LineString",
            "coordinates": [
              [21.8243, 39.0742],
              [21.8248, 39.0746],
              [21.8252, 39.0744]
            ]
          }
        }
      ]
    },
    "mapState": {
      "hillshadeEnabled": true,
      "terrain3dEnabled": false,
      "corridorOverlayEnabled": true,
      "centerLon": 21.8243,
      "centerLat": 39.0742,
      "zoom": 15,
      "bearing": 0,
      "pitch": 0
    }
  }
}
```

| Field | Type | Notes |
|-------|------|-------|
| `name` | string | Project display name |
| `lat`, `lon` | number? | Entrance WGS84 (from `data.json` `lat`/`lon`) |
| `declinationDeg` | number? | From `surveyCalibrationProfile.magneticDeclinationAppliedDeg` when set |
| `surfaceLidarRaster` | object? | Mirrors Android `SurfaceLidarRasterOverlay` bounds; `imageUrl` is https (remote) or virtual-host local cache |
| `surveyCorridor` | object? | GeoJSON `FeatureCollection` of traverse center-line `LineString` features in WGS84 `[lon, lat]` order; omitted when entrance coords or traverse legs are unavailable |
| `mapState` | object? | Persisted UI: layer toggles + camera (Windows `AppUiSettingsModel.surfaceMap`) |

### Map → host messages

| `type` | Purpose |
|--------|---------|
| `ready` | MapLibre loaded; host may push `project` |
| `mapState` | Camera/layer persistence (`payload` mirrors `mapState` above) |
| `status` | Human-readable status line (`message`) |

### Explore layer toggle sync (Windows)

| Surface | Behaviour |
|---------|-----------|
| **Public Library Explore map** (WebView) | On close, full viewport URL (including layer query params) is saved to `AppUiSettingsModel.exploreMapPersistedState.lastViewportUrl`. Next open restores layers via URL — no live sync with Surface tab. |
| **Surface map tab** (WPF checkboxes) | WPF → JS: `type: "layers"` on toggle. JS → WPF: `mapState` persists to disk and refreshes checkboxes via `SyncLayerCheckboxesFromSettings`. |
| **Cross-tab** | Explore web layers and Surface WPF toggles are **not** bidirectionally synced (by design — different hosts). |

Host may send `type: "layers"` (`hillshadeEnabled`, `terrain3dEnabled`, `corridorOverlayEnabled`) or `type: "fitEntrance"`.

### Survey corridor geometry (Phase 2)

Windows builds `surveyCorridor` from traverse legs (`toStation != "-"`):

1. Anchor first station at project entrance `lat`/`lon`.
2. For each leg, apply horizontal distance `distance * cos(clino)` along geographic bearing `azimuth + declinationDeg` (from `surveyCalibrationProfile.magneticDeclinationAppliedDeg`, else `0`).
3. Split connected traverse chains (Android `splitTraverseStationChains`) — one `LineString` feature per chain.
4. Coordinates are geodesic steps on WGS84 (Android `buildSurfaceStationCoordsGps`).

MapLibre renders the overlay as a teal line (`#00d4aa`) with green start / red end markers per chain when `mapState.corridorOverlayEnabled` is true (default).

### Tile sources (client-side URLs — no Firebase proxy)

| Layer | URL pattern |
|-------|-------------|
| OSM basemap | `https://tile.openstreetmap.org/{z}/{x}/{y}.png` |
| Hillshade | `https://tiles.wmflabs.org/hillshading/{z}/{x}/{y}.png` |
| DEM / 3D terrain | `https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png` (Terrarium encoding) |

MapLibre GL JS is loaded from CDN (`unpkg.com/maplibre-gl@4.7.1`) in v1 embedded assets.

### Android `surfaceLidarRaster` JSON (backup)

Same object as Android `CaveProject.surfaceLidarRaster`:

```json
{
  "imageUri": "maps/xray/surface_dsm.png",
  "southWestLat": 39.07,
  "southWestLon": 21.82,
  "northEastLat": 39.08,
  "northEastLon": 21.83,
  "opacity": 0.55
}
```

Windows resolves `imageUri` from backup ZIP / sibling files and serves via `caveai-surface-cache.local` virtual host when local.

### Phase 3 (not in scope)

- Firebase tile proxy
- Shared assets in caveaipro.com web repo

---

## Publish readiness (rules-only)

Shared checklist before Public Library publish (no LLM). Contract: `docs/publish-readiness-contract.json` (mirror of website repo). Windows evaluator: `Services/PublishReadiness/PublishReadinessEvaluator.cs`; confirm dialog: `Views/CloudPublishChecklistDialog.cs`. Publisher profile rule (`publisher_profile`) applies to `windows_cloud` when profile completeness is known; hard gate after sign-in: `Services/CloudPublish/CloudPublishProfileGate.cs`.

---

## User profile (publisher identity)

Subscriber profile at `users/{uid}` — required before first Public Library publish on web/Android; Windows owner sync skips the gate when `published_caves` already has publisher attribution fields.

- Contract mirror: website `docs/user-profile-contract.json`
- Windows: `Services/UserProfile/UserProfileService.cs` · `UserProfileDocument` · profile settings URL `https://www.caveaipro.com/account/profile`

---

## Firebase App Check (Windows REST)

| Client | Token source | REST header |
|--------|--------------|-------------|
| **Web** | `firebase/app-check` + reCAPTCHA v3 | SDK attaches automatically |
| **Android** | Play Integrity (+ debug in dev) | `X-Firebase-AppCheck` on proxy + SDK |
| **Windows** | WebView2 embed on `www.caveaipro.com` (`desktopAppCheckBridge.js`) | `X-Firebase-AppCheck` on `FirebaseRestClient` / `FirebaseCallableClient` when cached |

Windows has no native App Check provider. Tokens are bridged from the website session after sign-in or Public Library load. See `docs/APPCHECK-WINDOWS.md`. **Do not Enforce** Storage/Firestore until Metrics show valid Web tokens from Windows WebView traffic.

---

## Change process

1. Update all three clients when changing keys or URL formats.
2. Bump `surveyArchiveSchemaVersion` only for survey archive semantics (see [sync-contract.md](./sync-contract.md)).
3. Update Firestore rules in `D:\CaveAIPro\firebase\firestore.rules` when adding new `published_caves` fields.
4. Note changes in each repo's `CHANGELOG`.
