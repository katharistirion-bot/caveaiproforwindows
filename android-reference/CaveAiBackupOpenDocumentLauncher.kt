/**
 * CaveAI Pro — **Storage Access Framework** backup import (`.json` / `.zip` only).
 *
 * Copy into your Android app module (e.g. `feature-project` / `app`) and wire the launcher
 * wherever the user opens a project backup for import or merge.
 *
 * **Why these MIME types?**
 * - `application/json` / `application/zip` — canonical types for Gson exports and ZIP backups.
 * - `text/plain` — Samsung, Xiaomi, and other OEM file managers often mislabel `.json` as plain text.
 * - `application/octet-stream` — fallback when the provider cannot infer a specific type.
 *
 * SAF grays out entries that do not match [SAF_MIME_TYPES]. Because `text/plain` and
 * `application/octet-stream` are broad, always call [isAcceptedBackupUri] on the returned [Uri]
 * and reject picks whose display name does not end in `.json` or `.zip`.
 *
 * **Tap-to-open from file manager:** register [Intent.ACTION_VIEW] in AndroidManifest and route
 * through [CaveAiBackupViewIntentHandler] — see `AndroidTapToOpenBackup.md` in this folder.
 *
 * **Dependencies:** `androidx.activity:activity-ktx` (Activity Result API), `androidx.activity:activity-compose` for Compose.
 */
package com.caveai.pro.importing

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.OpenableColumns
import androidx.activity.result.contract.ActivityResultContracts

/** MIME filter for CaveAI backup import via [Intent.ACTION_OPEN_DOCUMENT]. */
object CaveAiBackupOpenDocumentLauncher {

    /** Passed to [ActivityResultContracts.OpenDocument] / [OpenMultipleDocuments] and [Intent.EXTRA_MIME_TYPES]. */
    @JvmField
    val SAF_MIME_TYPES: Array<String> = arrayOf(
        "application/json",
        "application/zip",
        "text/plain",
        "application/octet-stream",
    )

    private val ACCEPTED_EXTENSIONS = setOf("json", "zip")

    /**
     * Standalone [Intent] when not using Activity Result contracts (legacy `startActivityForResult`).
     * Sets [Intent.EXTRA_MIME_TYPES] and `type = "*/*"` as required by the Android docs.
     */
    fun createOpenBackupIntent(): Intent =
        Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "*/*"
            putExtra(Intent.EXTRA_MIME_TYPES, SAF_MIME_TYPES)
        }

    /** Activity Result contract — single backup file. */
    class OpenSingle : ActivityResultContracts.OpenDocument(SAF_MIME_TYPES)

    /** Activity Result contract — multiple backup files (Ctrl+pick on supported providers). */
    class OpenMultiple : ActivityResultContracts.OpenMultipleDocuments(SAF_MIME_TYPES)

    /**
     * Launch helper for an [ActivityResultContracts.OpenDocument] registration:
     * ```
     * val openBackup = registerForActivityResult(CaveAiBackupOpenDocumentLauncher.OpenSingle()) { uri ->
     *     if (uri != null && CaveAiBackupOpenDocumentLauncher.isAcceptedBackupUri(context, uri)) { ... }
     * }
     * // On button click:
     * CaveAiBackupOpenDocumentLauncher.launchOpenSingle(openBackup)
     * ```
     */
    fun launchOpenSingle(launcher: androidx.activity.result.ActivityResultLauncher<Array<String>>) {
        launcher.launch(SAF_MIME_TYPES)
    }

    fun launchOpenMultiple(launcher: androidx.activity.result.ActivityResultLauncher<Array<String>>) {
        launcher.launch(SAF_MIME_TYPES)
    }

    /** True when the picked file name ends with `.json` or `.zip` (case-insensitive). */
    fun isAcceptedBackupDisplayName(displayName: String?): Boolean {
        if (displayName.isNullOrBlank()) return false
        val ext = displayName.substringAfterLast('.', missingDelimiterValue = "").lowercase()
        return ext in ACCEPTED_EXTENSIONS
    }

    /**
     * Post-pick validation — required because [SAF_MIME_TYPES] includes loose OEM fallbacks.
     * Returns false for PDFs, images, etc. that slipped through as `text/plain` or `octet-stream`.
     */
    fun isAcceptedBackupUri(context: Context, uri: Uri): Boolean {
        val name = queryDisplayName(context, uri)
        return isAcceptedBackupDisplayName(name)
    }

    fun queryDisplayName(context: Context, uri: Uri): String? {
        context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)
            ?.use { cursor ->
                if (!cursor.moveToFirst()) return null
                val idx = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                if (idx < 0) return null
                return cursor.getString(idx)
            }
        return null
    }
}

/*
 * --- Compose wiring example ---
 *
 * @Composable
 * fun RememberOpenCaveAiBackupLauncher(
 *     onAccepted: (Uri) -> Unit,
 *     onRejected: () -> Unit = {},
 * ): () -> Unit {
 *     val context = LocalContext.current
 *     val launcher = rememberLauncherForActivityResult(CaveAiBackupOpenDocumentLauncher.OpenSingle()) { uri ->
 *         when {
 *             uri == null -> Unit
 *             CaveAiBackupOpenDocumentLauncher.isAcceptedBackupUri(context, uri) -> onAccepted(uri)
 *             else -> onRejected()
 *         }
 *     }
 *     return {
 *         CaveAiBackupOpenDocumentLauncher.launchOpenSingle(launcher)
 *     }
 * }
 *
 * --- Fragment / Activity wiring example ---
 *
 * private val openBackupLauncher = registerForActivityResult(CaveAiBackupOpenDocumentLauncher.OpenSingle()) { uri ->
 *     if (uri != null && CaveAiBackupOpenDocumentLauncher.isAcceptedBackupUri(this, uri)) {
 *         viewModel.importBackup(uri)
 *     }
 * }
 *
 * fun onOpenBackupClicked() {
 *     CaveAiBackupOpenDocumentLauncher.launchOpenSingle(openBackupLauncher)
 * }
 */
