# Tap-to-open CaveAI backups from the Android file manager

When a user taps a `.zip` or `.json` CaveAI backup in Downloads / Files, Android should offer **CaveAI Pro** and open the project **without** the manual SAF picker.

This folder contains copy-paste reference code aligned with [`CaveAiBackupOpenDocumentLauncher.kt`](CaveAiBackupOpenDocumentLauncher.kt).

---

## Files to copy into the Android app module

| File | Destination (example) |
|------|------------------------|
| `CaveAiBackupViewIntentHandler.kt` | `app/src/main/java/.../importing/` |
| `AndroidManifest-backup-view-intent-filters.xml` | Merge into `AndroidManifest.xml` |
| `MainActivity-backup-view-intent.kt` | Merge into your real `MainActivity.kt` |

---

## Step 1 — `AndroidManifest.xml`

On your **launcher activity** (`MainActivity`):

1. Set `android:exported="true"` (required for external `ACTION_VIEW`, API 31+).
2. Prefer `android:launchMode="singleTask"` so a second tap calls `onNewIntent` instead of stacking activities.
3. Paste the `<intent-filter>` blocks from [`AndroidManifest-backup-view-intent-filters.xml`](AndroidManifest-backup-view-intent-filters.xml).

Minimal example:

```xml
<activity
    android:name=".MainActivity"
    android:exported="true"
    android:launchMode="singleTask"
    android:theme="@style/Theme.CaveAiPro"
    android:windowSoftInputMode="adjustResize">

    <!-- Existing launcher -->
    <intent-filter>
        <action android:name="android.intent.action.MAIN" />
        <category android:name="android.intent.category.LAUNCHER" />
    </intent-filter>

    <!-- Paste all VIEW / SEND filters from AndroidManifest-backup-view-intent-filters.xml here -->

</activity>
```

Add to `res/values/strings.xml`:

```xml
<string name="open_caveai_backup">Open CaveAI backup</string>
```

### Why multiple intent-filters?

| Scheme | MIME / path | Reason |
|--------|-------------|--------|
| `content` | `application/zip` | Standard ZIP backup |
| `content` | `application/json` | Standard JSON export |
| `content` | `text/plain`, `application/octet-stream` | Samsung / Xiaomi mislabel `.json` |
| `file` | `application/*` + `pathSuffix=".zip"` / `.json` | Legacy file explorers |

`content://` URIs do not support reliable `pathPattern`; **always validate the display name** in code (see handler below).

---

## Step 2 — Activity interception (`MainActivity.kt`)

Handle the intent in **both** `onCreate` (cold start) and `onNewIntent` (app already running):

```kotlin
override fun onCreate(savedInstanceState: Bundle?) {
    super.onCreate(savedInstanceState)
    setContent { /* … */ }
    handleViewIntent(intent)
}

override fun onNewIntent(intent: Intent) {
    super.onNewIntent(intent)
    setIntent(intent)
    handleViewIntent(intent)
}

private fun handleViewIntent(intent: Intent?) {
    CaveAiBackupViewIntentHandler.handle(
        activity = this,
        intent = intent,
        onAccepted = { uri -> backupImportViewModel.importBackup(uri) },
        onRejected = { message -> /* Snackbar / Toast */ },
    )
}
```

Full Compose + ViewModel example: [`MainActivity-backup-view-intent.kt`](MainActivity-backup-view-intent.kt).

---

## Step 3 — Routing to your existing import function

`CaveAiBackupViewIntentHandler` does **not** parse JSON/ZIP itself. It:

1. Accepts `ACTION_VIEW` (and optional `ACTION_SEND`).
2. Reads `intent.data` (or `EXTRA_STREAM`).
3. Rejects non-`.json` / non-`.zip` picks via `CaveAiBackupOpenDocumentLauncher.isAcceptedBackupUri`.
4. Calls `takePersistableUriPermission` when the provider allows it.
5. Invokes your `onAccepted(uri)` callback.

**Wire `onAccepted` to the same code as the manual picker:**

```kotlin
// SAF picker (existing)
private val openBackupLauncher = registerForActivityResult(
    CaveAiBackupOpenDocumentLauncher.OpenSingle()
) { uri ->
    if (uri != null && CaveAiBackupOpenDocumentLauncher.isAcceptedBackupUri(this, uri)) {
        viewModel.importBackup(uri)   // ← same function
    }
}

// External tap (new)
CaveAiBackupViewIntentHandler.handle(
    activity = this,
    intent = intent,
    onAccepted = { uri -> viewModel.importBackup(uri) },  // ← same function
    onRejected = { … },
)
```

Inside `importBackup(uri)` you typically:

- Open `contentResolver.openInputStream(uri)` (or `file://` via `uri.path`).
- If `.zip` → read `data.json` entry + optional `map_inventory.json` (see [`MapsWindowsSync.md`](MapsWindowsSync.md)).
- If `.json` → parse Gson `CaveProject[]` and load into your project repository.
- Navigate to the survey editor for the imported cave.

---

## Step 4 — Verify on device

1. Export a backup ZIP from CaveAI Pro → Downloads.
2. Open **Files** / **My Files** → tap the `.zip`.
3. Confirm **Open with → CaveAI Pro** (or CaveAI opens directly if it is the default).
4. App should import without showing the document picker.
5. Repeat with `.json` and with the app **already in the foreground** (tests `onNewIntent`).

### adb smoke test

```bash
adb shell am start -a android.intent.action.VIEW \
  -d "content://com.android.externalstorage.documents/document/primary%3ADownload%2Fcave_backup.zip" \
  -t application/zip \
  com.caveai.pro/.MainActivity
```

Adjust package/activity and URI to match your device.

---

## Security notes

- Extension validation prevents PDFs/images mislabeled as `text/plain` from importing.
- The handler clears `intent.data` after success so configuration changes do not re-trigger import.
- Do not add a catch-all `*/*` intent-filter without post-validation — Play policy and user trust.

---

## Windows companion parity

The same backup files opened via tap-to-open on Android are the files the Windows app loads via **File → Open** (`.json` / `.zip`). See the main repo `CaveAiBackupFileDialogFilters.cs` and `ExplorationDataLoader`.
