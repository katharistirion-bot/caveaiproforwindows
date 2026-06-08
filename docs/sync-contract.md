# Android ↔ Windows sync contract (index)

Normative summary for data exchanged between **CaveAI Pro (Android)** and **CAVE AI PRO (Windows)**. Detailed specs live under `android-reference/`.

## Schema versions

| Field | Value | Notes |
|-------|-------|-------|
| `surveyArchiveSchemaVersion` | `2` | Enriched `data.json` (v2 survey archive) |
| Site type field | `surveySiteType` | `CAVE` \| `MINE` \| `POTHOLE` \| `SPRING` (schema v3+) |

**Windows min:** 1.3.0 · **Android:** keep in sync when changing JSON or ZIP layout.

## Primary payloads

| Payload | Location | Contract doc |
|---------|----------|--------------|
| Project list / single project | `data.json` in ZIP or standalone JSON | [DataJsonSurveySchemaV2.md](../android-reference/DataJsonSurveySchemaV2.md) |
| Site type labels | `surveySiteType` on project | [SurveySiteTypeContract.md](../android-reference/SurveySiteTypeContract.md) |
| Backup ZIP maps | `export_assets/maps/…`, `map_inventory.json` | [MapsWindowsSync.md](../android-reference/MapsWindowsSync.md) |
| Desktop folder sync | Android backup folder ↔ PC | [AndroidDesktopAutoSync.md](../android-reference/AndroidDesktopAutoSync.md) |

## ZIP layout (backup)

```
CaveAI_Backup_*.zip
├── data.json                 # Gson project(s)
├── photos/
├── export_assets/
├── integrity_manifest.json   # SHA-256 per entry
├── backup_manifest.json
├── map_inventory.json        # optional (newer Android exports)
└── README.txt
```

Windows reads `data.json` first; map assets resolve from ZIP paths or sibling backup when opening exported JSON.

## Single-cave export rule

When Android exports **one cave** to ZIP, the archive contains **one project** in `data.json` (not the full multi-cave database). Windows Save/export follows the same rule.

## Windows persistence (not in ZIP)

| File | Purpose |
|------|---------|
| `%LOCALAPPDATA%\CaveAiProForWindows\ui-settings.json` | Android sync folder path, UI prefs |
| `recent.json` | Recent file paths |

Secrets (Replicate BYOK) use Windows Credential Manager — never written to survey backups.

## Change process

1. Update Android export/import and Windows loaders in the same PR/release when possible.
2. Bump `surveyArchiveSchemaVersion` or document a new field in `android-reference/`.
3. Add a row to this index and a note in `CHANGELOG.md`.
