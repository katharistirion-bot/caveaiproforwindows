/**
 * CaveAI Pro — **MainActivity** wiring for tap-to-open backup files.
 *
 * Copy the relevant parts into your real `MainActivity.kt` (Compose or XML).
 * Replace `BackupImportViewModel` / `importBackup` with your existing import entry point
 * (the same function your SAF [CaveAiBackupOpenDocumentLauncher] picker calls).
 */
package com.caveai.pro.app

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.caveai.pro.importing.CaveAiBackupViewIntentHandler
import com.caveai.pro.ui.theme.CaveAiTheme
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.launch

// ─── 1. MainActivity ─────────────────────────────────────────────────────────

class MainActivity : ComponentActivity() {

    private val backupImportViewModel: BackupImportViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        setContent {
            val snackbarHostState = remember { SnackbarHostState() }

            CaveAiTheme {
                // Your NavHost / root composable …
                // CaveAiAppRoot(snackbarHostState = snackbarHostState)

                LaunchedEffect(Unit) {
                    backupImportViewModel.messages.collect { msg ->
                        snackbarHostState.showSnackbar(msg)
                    }
                }

                LaunchedEffect(Unit) {
                    backupImportViewModel.pendingImportUri.collect { uri ->
                        // Navigate to project screen / show import progress — app-specific
                        // navController.navigate("import?uri=${Uri.encode(uri.toString())}")
                    }
                }
            }
        }

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
            onRejected = { message -> backupImportViewModel.notifyUser(message) },
        )
    }
}

// ─── 2. ViewModel — route into your existing import pipeline ─────────────────

/**
 * Thin adapter: external VIEW intents and SAF picker results should both call [importBackup].
 *
 * Replace the body of [importBackup] with your real loader, e.g.:
 * - `ProjectBackupLoader.load(context, uri)` (JSON array or ZIP → extract data.json)
 * - `projectRepository.mergeOrReplaceFromBackup(uri)`
 * - same code path as `registerForActivityResult(CaveAiBackupOpenDocumentLauncher.OpenSingle()) { … }`
 */
class BackupImportViewModel(
    private val backupLoader: ProjectBackupLoader = ProjectBackupLoader(),
) : ViewModel() {

    private val _pendingImportUri = MutableSharedFlow<Uri>(extraBufferCapacity = 1)
    val pendingImportUri: SharedFlow<Uri> = _pendingImportUri

    private val _messages = MutableSharedFlow<String>(extraBufferCapacity = 1)
    val messages: SharedFlow<String> = _messages

    fun importBackup(uri: Uri) {
        viewModelScope.launch {
            runCatching {
                backupLoader.load(uri) // ← your existing import function
            }.onSuccess { projectId ->
                _pendingImportUri.emit(uri)
                _messages.emit("Opened backup successfully.")
                // Optionally: emit projectId and navigate to editor
            }.onFailure { e ->
                _messages.emit(e.message ?: "Could not open backup.")
            }
        }
    }

    fun notifyUser(message: String) {
        viewModelScope.launch { _messages.emit(message) }
    }
}

/** Placeholder — swap for your production import service. */
class ProjectBackupLoader {
    suspend fun load(uri: Uri): String {
        // Example pseudocode:
        // when (uri.displayNameExtension()) {
        //     "zip" -> zipBackupImporter.import(uri)
        //     "json" -> jsonBackupImporter.import(uri)
        //     else -> error("Unsupported")
        // }
        return "project-id"
    }
}

// ─── 3. Fragment-based MainActivity (if not using Compose) ───────────────────
/*
class MainActivity : AppCompatActivity() {

    private val backupImportViewModel: BackupImportViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
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
            onRejected = { Toast.makeText(this, it, Toast.LENGTH_LONG).show() },
        )
    }
}
*/
