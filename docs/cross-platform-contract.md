# Cross-platform contract (Android · Windows · Web)

Normative interoperability spec for **CaveAI Pro (Android)**, **CAVE AI PRO (Windows)**, and **caveaipro.com**. For Android ↔ Windows backup/sync details see [sync-contract.md](./sync-contract.md).

Site origin for share URLs: **`https://www.caveaipro.com`**

---

## Reference catalog CDN URLs

| Resource | URL |
|----------|-----|
| Meta | `https://www.caveaipro.com/data/reference-caves-meta.json` |
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

## Change process

1. Update all three clients when changing keys or URL formats.
2. Bump `surveyArchiveSchemaVersion` only for survey archive semantics (see [sync-contract.md](./sync-contract.md)).
3. Update Firestore rules in `D:\CaveAIPro\firebase\firestore.rules` when adding new `published_caves` fields.
4. Note changes in each repo's `CHANGELOG`.
