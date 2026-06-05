/**
 * CaveAI Pro — **Desktop Bridge / Desktop Sync UI** (Jetpack Compose, Material 3).
 *
 * Copy into your Android module (e.g. `feature-project` / `app`) next to project detail /
 * export bottom-sheet composables.
 *
 * **Wire export:** from your ViewModel or screen call site, pass a lambda that invokes your
 * existing dispatcher, e.g.:
 * ```
 * enqueueCaveZipExport(CaveZipExportRequest.ProjectBackup(currentProjectOnly = true))
 * ```
 *
 * **Snackbar host:** Ensure the same composable subtree is under a [Scaffold] (or Modal bottom sheet host)
 * that provides [SnackbarHost] using this `snackbarHostState`, or hoist the state from your screen.
 *
 * **Dependencies** (module `build.gradle.kts`): Compose BOM + Material3 + icons extended:
 * ```kotlin
 * implementation(platform("androidx.compose:compose-bom:YOUR_BOM"))
 * implementation("androidx.compose.material3:material3")
 * implementation("androidx.compose.material:material-icons-extended")
 * ```
 *
 * Replace package / theme tokens with your app's conventions (`CaveAiTheme`, spacing, motion).
 */
package com.caveai.pro.ui.desktopsync

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CloudSync
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch

private const val WindowsExportSnackMessage =
    "Preparing Windows Export... Check your Downloads folder."

/** Prominent premium card for cave project detail screens or export sheets. */
@Composable
fun DesktopSyncSection(
    modifier: Modifier = Modifier,
    snackbarHostState: SnackbarHostState,
    /** Call your ZIP engine here, e.g. `enqueueCaveZipExport(CaveZipExportRequest.ProjectBackup(...))`. */
    onExportToWindowsPc: () -> Unit,
) {
    val scope = rememberCoroutineScope()

    Card(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.secondaryContainer.copy(alpha = 0.35f),
        ),
        border = BorderStroke(
            width = 1.dp,
            color = MaterialTheme.colorScheme.outline.copy(alpha = 0.28f),
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = 4.dp),
    ) {
        Column(Modifier.padding(horizontal = 20.dp, vertical = 18.dp)) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Icon(
                    imageVector = Icons.Outlined.CloudSync,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.secondary,
                    modifier = Modifier.size(26.dp),
                )
                Column(Modifier.weight(1f)) {
                    Text(
                        text = "Desktop Sync",
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurface,
                    )
                    Spacer(Modifier.height(4.dp))
                    Text(
                        text = "Exports a ZIP your Windows companion can open (single project)",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }

            Spacer(Modifier.height(18.dp))
            HorizontalDivider(color = MaterialTheme.colorScheme.outline.copy(alpha = 0.22f))

            Spacer(Modifier.height(14.dp))

            FilledTonalButton(
                onClick = {
                    onExportToWindowsPc()
                    scope.launch {
                        snackbarHostState.showSnackbar(
                            message = WindowsExportSnackMessage,
                            withDismissAction = true,
                            duration = SnackbarDuration.Long,
                        )
                    }
                },
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.filledTonalButtonColors(
                    containerColor = MaterialTheme.colorScheme.secondaryContainer,
                    contentColor = MaterialTheme.colorScheme.onSecondaryContainer,
                ),
            ) {
                Icon(
                    imageVector = Icons.Filled.Computer,
                    contentDescription = null,
                    modifier = Modifier.size(22.dp),
                )
                Spacer(Modifier.width(ButtonDefaults.IconSpacing))
                Text(
                    text = "Export to Windows PC",
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                )
            }

            Row(
                modifier = Modifier.padding(top = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Icon(
                    imageVector = Icons.Outlined.Info,
                    contentDescription = null,
                    modifier = Modifier.size(16.dp),
                    tint = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.85f),
                )
                Text(
                    text = "File is saved by your existing backup pipeline — usually Downloads/CaveAI.",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

/*
// --- Typical integration (project detail scaffold) ---
@Composable
fun CaveProjectDetailContent(
    projectId: String,
    vm: CaveProjectDetailViewModel, // exposes enqueueCaveZipExport or delegate
    snackbarHostState: SnackbarHostState,
) {
    Column(Modifier.verticalScroll(...) ... ) {
        // ...existing header / stats...
        DesktopSyncSection(
            snackbarHostState = snackbarHostState,
            onExportToWindowsPc = {
                vm.enqueueCaveZipExport(
                    CaveZipExportRequest.ProjectBackup(currentProjectOnly = true)
                )
                // Alternative if static: BackupExportDispatcher.enqueueCaveZipExport(...)
            },
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
        )
    }
}

// --- Typical integration (share / export ModalBottomSheet) ---
@Composable
fun ProjectExportSheet(
    snackbarHostState: SnackbarHostState,
    onDismiss: () -> Unit,
    onExportToWindowsPc: () -> Unit,
) {
    ModalBottomSheet(onDismissRequest = onDismiss, ...) {
        Column(Modifier.padding(20.dp).navigationBarsPadding(), verticalArrangement = spacedBy(16.dp)) {
            Text("Share & export", style = MaterialTheme.typography.titleLarge)
            DesktopSyncSection(
                snackbarHostState = snackbarHostState,
                onExportToWindowsPc = onExportToWindowsPc,
            )
        }
    }
}
*/
