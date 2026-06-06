/**
 * CaveAI Pro — **tap-to-open** backup files from the system file manager / Downloads.
 *
 * Handles [Intent.ACTION_VIEW] when the user taps a `.json` or `.zip` CaveAI backup.
 * Reuses the same extension validation as [CaveAiBackupOpenDocumentLauncher].
 *
 * **Wire from MainActivity:**
 * ```
 * override fun onCreate(savedInstanceState: Bundle?) {
 *     super.onCreate(savedInstanceState)
 *     setContent { ... }
 *     handleViewIntent(intent)
 * }
 *
 * override fun onNewIntent(intent: Intent) {
 *     super.onNewIntent(intent)
 *     setIntent(intent)
 *     handleViewIntent(intent)
 * }
 *
 * private fun handleViewIntent(intent: Intent?) {
 *     CaveAiBackupViewIntentHandler.handle(
 *         activity = this,
 *         intent = intent,
 *         onAccepted = { uri -> backupImportViewModel.importBackup(uri) },
 *         onRejected = { message -> snackbar.show(message) },
 *     )
 * }
 * ```
 *
 * **Dependencies:** same as [CaveAiBackupOpenDocumentLauncher] (`activity-ktx`, your import ViewModel).
 */
package com.caveai.pro.importing

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.util.Log
import androidx.core.content.IntentCompat

object CaveAiBackupViewIntentHandler {

    private const val TAG = "CaveAiBackupView"

    /** Actions we accept from external apps / file pickers. */
    private val ACCEPTED_ACTIONS = setOf(
        Intent.ACTION_VIEW,
        Intent.ACTION_SEND, // some OEM “Share → Open with CaveAI” flows
    )

    /**
     * Parse [intent] and route a valid backup [Uri] to [onAccepted].
     *
     * @param consumeIntent When true (default), clears [Intent.ACTION_VIEW] data after handling so
     *   rotation / process death does not re-import the same file.
     */
    @JvmStatic
    @JvmOverloads
    fun handle(
        activity: Activity,
        intent: Intent?,
        onAccepted: (Uri) -> Unit,
        onRejected: (String) -> Unit = {},
        consumeIntent: Boolean = true,
    ) {
        if (intent == null) return

        val action = intent.action ?: return
        if (action !in ACCEPTED_ACTIONS) return

        val uri = extractBackupUri(intent)
        if (uri == null) {
            Log.w(TAG, "VIEW intent without usable data URI: action=$action extras=${intent.extras?.keySet()}")
            onRejected("No backup file in this link.")
            return
        }

        if (!CaveAiBackupOpenDocumentLauncher.isAcceptedBackupUri(activity, uri)) {
            Log.w(TAG, "Rejected external open: $uri")
            onRejected("Only CaveAI backup files (.json or .zip) can be opened.")
            return
        }

        if (!takePersistableReadPermission(activity, intent, uri)) {
            Log.w(TAG, "Could not persist read permission for $uri — import may fail on some providers.")
        }

        Log.i(TAG, "Opening backup from external intent: $uri")
        onAccepted(uri)

        if (consumeIntent) {
            intent.action = Intent.ACTION_MAIN
            intent.data = null
            intent.clipData = null
            intent.removeExtra(Intent.EXTRA_STREAM)
        }
    }

    /** True when [intent] looks like an external backup open (cheap guard for UI). */
    @JvmStatic
    fun isExternalBackupViewIntent(intent: Intent?): Boolean {
        if (intent == null) return false
        val action = intent.action ?: return false
        if (action !in ACCEPTED_ACTIONS) return false
        return extractBackupUri(intent) != null
    }

    private fun extractBackupUri(intent: Intent): Uri? {
        intent.data?.let { return it }

        // ACTION_SEND single stream
        IntentCompat.getParcelableExtra(intent, Intent.EXTRA_STREAM, Uri::class.java)?.let { return it }

        // Rare: clipData from some file managers
        intent.clipData?.let { clip ->
            if (clip.itemCount > 0) {
                clip.getItemAt(0)?.uri?.let { return it }
            }
        }

        return null
    }

    /**
     * Grants long-lived read access for `content://` URIs delivered via [Intent.FLAG_GRANT_READ_URI_PERMISSION].
     * No-op for `file://` (deprecated on modern Android but still seen on older devices).
     */
    private fun takePersistableReadPermission(context: Context, intent: Intent, uri: Uri): Boolean {
        if (uri.scheme != "content") return true

        val flags = intent.flags and (
            Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
            )

        return try {
            if (flags and Intent.FLAG_GRANT_READ_URI_PERMISSION != 0) {
                context.contentResolver.takePersistableUriPermission(
                    uri,
                    Intent.FLAG_GRANT_READ_URI_PERMISSION,
                )
            }
            true
        } catch (e: SecurityException) {
            // Provider did not set FLAG_GRANT_PERSISTABLE_URI_PERMISSION — one-shot grant may still work.
            Log.w(TAG, "takePersistableUriPermission failed for $uri", e)
            flags and Intent.FLAG_GRANT_READ_URI_PERMISSION != 0
        }
    }
}
