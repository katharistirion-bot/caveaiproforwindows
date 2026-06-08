# Android ↔ Windows Desktop Auto-Sync

How CaveAI Pro on **Android** keeps the **Windows companion** up to date without manual File → Open.

## Android side (export)

1. In the Android app, use **Desktop Sync / Export to Windows PC** (see `DesktopSyncCompose.kt`).
2. Export writes to a shared folder, typically:
   - `Downloads/CaveAI/` on the phone (USB / MTP copy), or
   - `Documents/CaveAI/`, `OneDrive/CaveAI/`, `Google Drive/CaveAI/` when the user syncs that folder to the PC.

### Files Android should place in the sync folder

| File | Purpose |
|------|---------|
| `CaveAI_Backup_*.zip` | **Full survey** — preferred for Windows auto-reload (plan, maps, photos, integrity). |
| `caveai_database_v1.json` | Live database snapshot — AI Analytics notes/context. |
| `android_export.json` | Optional field observations export — AI Analytics. |

**Recommended workflow:** After each significant edit on Android, run **Export to Windows PC** (ZIP). The Windows app can auto-open the newest ZIP.

## Windows side (this repo)

### AI Analytics → Desktop Sync

| Setting | Effect |
|---------|--------|
| **Import Android Project…** | Pick the sync folder (persisted in `ui-settings.json`). |
| **Auto-sync when Android files change** | Watches `caveai_database_v1.json` + `android_export.json` → refreshes AI Analytics context. |
| **Auto-reload CaveAI_Backup_*.zip** | When a new/changed backup ZIP appears, reloads the **main survey workspace** (Plan, Section, 3D, etc.). |

Default sync folder candidates (auto-detected):

- `%USERPROFILE%\Downloads\CaveAI`
- `%USERPROFILE%\Documents\CaveAI`
- `%USERPROFILE%\OneDrive\CaveAI`
- `%USERPROFILE%\Google Drive\CaveAI`

### Main window watcher

On startup, Windows watches the sync folder for `CaveAI_Backup_*.zip` and reloads when **Auto-reload** is enabled.

When JSON sync files update, Windows also checks for a newer ZIP in the same folder.

## Settings storage

`%LocalAppData%\CaveAiProForWindows\ui-settings.json`:

```json
"androidSync": {
  "syncFolderPath": "C:\\Users\\…\\Downloads\\CaveAI",
  "autoSyncEnabled": true,
  "autoRunAnalysisOnSync": true,
  "autoRunAnalysisOnProjectLoad": true,
  "autoReloadBackupZip": true
}
```

## Cloud folder tip

If the user syncs `OneDrive/CaveAI` or `Google Drive/CaveAI` on both phone and PC, set that path once in **AI Analytics → Import Android Project** — both JSON and ZIP auto-sync work on the same directory.

## Android implementation checklist

- [ ] Export ZIP to `Downloads/CaveAI/CaveAI_Backup_{cave}_{timestamp}.zip`
- [ ] Optionally mirror `caveai_database_v1.json` to the same folder on export
- [ ] Document the folder path in the Desktop Sync UI on Android
