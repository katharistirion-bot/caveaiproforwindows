/**
 * CaveAI Pro — **ZIP backup only**: bundle map files for Windows (PC companion).
 *
 * Copy this file into the **CaveAI Pro Android** project and wire it from your existing
 * `ProjectBackupZip` / backup export pipeline.
 *
 * **Problem:** If `data.json` still contains `content://…` or device-only paths, CAVE AI PRO
 * on Windows cannot open those maps (Windows `MapAssetOpener` rejects `content:`).
 *
 * **Interchange format (office + Windows companion):** Prefer each bundled map as a **bitmap
 * raster** (`.png`, `.tif`/`.tiff`, `.jpg`, `.webp`, `.bmp`) — one file per map folder — so
 * archives are easy to open in viewers/GIS. The PC app uses **`RasterImageDecoder`** for
 * Plan/Section underlay (rasters + **PDF first page**). Full contract: `MapsWindowsSync.md`
 * in this folder.
 * Heavy vector/GIS-only sources (e.g. raw `.svg`/mesh) are awkward in a flat backup; **rasterize
 * or export to PDF on device** before `copyTo(zip)` when you want predictable ZIP contents.
 *
 * **Fix:** While building the backup `.zip`:
 * 1. Copy every local map / raster / mesh file referenced by the project into **`export_assets/…`**
 *    (forward slashes in JSON paths, same convention as README in `caveaiproforwindows`).
 * 2. **One map = one dedicated folder inside the ZIP** under `export_assets/maps/…` so files never
 *    share a single flat directory across maps or across caves (no accidental overwrites / confusion
 *    when browsing or extracting). Each project gets a **unique segment** (`name` + stable hash of
 *    `name|date`) so two different surveys that sanitize to the same ASCII name still land in
 *    different trees.
 * 3. **Rewrite** those references in the JSON you put in the ZIP to the new `export_assets/maps/…`
 *    paths (or another stable prefix under `export_assets/`).
 * 4. Optionally write **`map_inventory.json`** at the ZIP root — the Windows app reads it
 *    (`ZipMapInventoryReader`) for the Backup detail grid.
 * 5. Set **`backup_manifest.json` → `includes_map_inventory`: true** when you wrote the inventory.
 * 6. Regenerate **`integrity_manifest.json`** so every ZIP entry (including new `export_assets/**`)
 *    has a SHA-256 entry (`IntegrityVerifier` on Windows).
 *
 * This file does **not** compile standalone: replace placeholders (`YourCaveProject`, stream providers)
 * with your real models and storage APIs.
 */
package com.caveai.pro.backup.reference

import org.json.JSONArray
import org.json.JSONObject
import java.io.InputStream
import java.security.MessageDigest
import java.util.Locale
import java.util.UUID
import java.nio.charset.StandardCharsets
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

// --- JSON contract: map_inventory.json (must match Windows ZipMapInventoryReader) ---

data class MapInventorySlot(
    val slot: String,
    /** Path inside the ZIP, e.g. export_assets/maps/MyCave/0_basemap.tif — or https URL left as-is */
    val pathInBackupOrUrl: String,
    /** e.g. "zip", "https", "file" — informational for desktop */
    val storageKind: String,
)

data class MapInventoryProject(
    val projectName: String,
    val projectDate: String,
    val maps: List<MapInventorySlot>,
)

/**
 * Root object written as UTF-8 JSON next to `data.json` in the backup zip.
 */
data class MapInventoryJsonRoot(
    val projects: List<MapInventoryProject>,
) {
    fun toJsonObject(): JSONObject {
        val root = JSONObject()
        val arr = JSONArray()
        for (p in projects) {
            val o = JSONObject()
            o.put("projectName", p.projectName)
            o.put("projectDate", p.projectDate)
            val maps = JSONArray()
            for (m in p.maps) {
                val mo = JSONObject()
                mo.put("slot", m.slot)
                mo.put("pathInBackupOrUrl", m.pathInBackupOrUrl)
                mo.put("storageKind", m.storageKind)
                maps.put(mo)
            }
            o.put("maps", maps)
            arr.put(o)
        }
        root.put("projects", arr)
        return root
    }
}

/**
 * Resolves a map source on the device to bytes for bundling.
 * Implement with ContentResolver.openInputStream(uri) for content://, FileInputStream for files, etc.
 */
fun interface MapBytesProvider {
    fun openStream(): InputStream?
}

/**
 * **Call once per cave project** while exporting: copies map files into the ZIP under
 * [exportPrefix] and returns (1) JSON text for `data.json` with paths rewritten where possible,
 * (2) rows for `map_inventory.json`.
 *
 * @param gsonSerializedProjectJson UTF-8 JSON for one [CaveProject] as Gson would write it today
 *        (may contain content://, file://, or internal app paths).
 * @param mapSlots ordered list of human-readable slot names + how to read bytes + original URI string
 *        for string replacement in JSON (e.g. the exact content:// string Gson emitted).
 */
fun bundleMapsForZipProject(
    gsonSerializedProjectJson: String,
    projectName: String,
    projectDate: String,
    zip: ZipOutputStream,
    mapSlots: List<Triple<String, MapBytesProvider?, String /* originalUriOrPathInJson */>>,
    exportPrefix: String = "export_assets/maps",
): Pair<String, MapInventoryProject> {
    val inventorySlots = mutableListOf<MapInventorySlot>()
    var patchedJson = gsonSerializedProjectJson

    // Unique per (name, date) so two caves that share a sanitized name never share one folder.
    val projectSegment = projectMapsZipSegment(projectName, projectDate)

    mapSlots.forEachIndexed { index, (slot, provider, original) ->
        val t = original.trim()
        if (t.startsWith("http://", ignoreCase = true) || t.startsWith("https://", ignoreCase = true)) {
            inventorySlots.add(MapInventorySlot(slot = slot, pathInBackupOrUrl = t, storageKind = "https"))
            return@forEachIndexed
        }

        val p = provider ?: return@forEachIndexed
        val stream = p.openStream() ?: return@forEachIndexed
        stream.use { input ->
            val ext = guessExtensionFromUriOrPath(original)
            // One directory per map: 000_slotLabel_a1b2c3d4 / <leaf>.tif — never a single flat pile.
            val slotSlug = sanitizeSlotLabel(slot)
            val shortId = UUID.randomUUID().toString().take(8)
            val mapDir = "${index.toString().padStart(3, '0')}_${slotSlug}_$shortId"
            val leafFile = suggestedLeafFileName(original, ext)
            val zipInternalPath = "$exportPrefix/$projectSegment/$mapDir/$leafFile".replace('\\', '/')
            val entry = ZipEntry(zipInternalPath)
            zip.putNextEntry(entry)
            input.copyTo(zip)
            zip.closeEntry()

            // Rewrite every occurrence of the old URI/path so Windows MapAssetsCollector sees export_assets/
            if (original.isNotBlank()) {
                patchedJson = patchedJson.replace(original, zipInternalPath)
            }

            inventorySlots.add(
                MapInventorySlot(
                    slot = slot,
                    pathInBackupOrUrl = zipInternalPath,
                    storageKind = "zip",
                ),
            )
        }
    }

    return patchedJson to MapInventoryProject(projectName, projectDate, inventorySlots)
}

/** Sanitized project folder, unique per name + date (hash suffix disambiguates collisions). */
private fun projectMapsZipSegment(projectName: String, projectDate: String): String {
    val safe = projectName.replace(Regex("[^a-zA-Z0-9._-]+"), "_").trim('_').ifEmpty { "cave" }
    val h = sha256Hex("$projectName|${projectDate}".toByteArray(StandardCharsets.UTF_8)).take(10)
    return "${safe}__$h"
}

private fun sanitizeSlotLabel(slot: String, maxLen: Int = 40): String {
    var t = slot.replace(Regex("[^a-zA-Z0-9._-]+"), "_").trim('_')
    if (t.isEmpty()) t = "map"
    if (t.length > maxLen) t = t.substring(0, maxLen).trimEnd('_')
    return t
}

/** File name inside the per-map folder — derived from original when possible. */
private fun suggestedLeafFileName(original: String, ext: String): String {
    val withoutQuery = original.substringBefore('?')
    val last = withoutQuery.substringAfterLast('/').substringAfterLast('\\')
    val base = if (last.contains('.')) last.substringBeforeLast('.') else last
    var cleaned = base.replace(Regex("[^a-zA-Z0-9._-]+"), "_").trim('_')
    if (cleaned.isEmpty()) cleaned = "map"
    if (cleaned.length > 48) cleaned = cleaned.substring(0, 48).trimEnd('_')
    return "$cleaned$ext"
}

/** Prefer real GIS/map extensions; align with app `ExportZipBundledAssets` + PC `StandaloneMapFileSupport`. */
private fun guessExtensionFromUriOrPath(original: String): String {
    val withoutQuery = original.substringBefore('?')
    val dot = withoutQuery.lastIndexOf('.')
    if (dot <= 0 || dot >= withoutQuery.length - 1) return ".tif"
    val ext = withoutQuery.substring(dot).lowercase(Locale.US).take(12)
    val known = setOf(
        ".tif", ".tiff", ".geotiff", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif",
        ".pdf", ".svg", ".kml", ".kmz", ".gpx", ".dxf", ".obj", ".mtl", ".geojson",
    )
    return if (ext in known) ext else if (ext.matches(Regex("\\.[a-z0-9]{2,12}"))) ext else ".tif"
}

/**
 * SHA-256 hex lowercase — same as Windows [IntegrityVerifier].
 */
fun sha256Hex(bytes: ByteArray): String {
    val md = MessageDigest.getInstance("SHA-256")
    val digest = md.digest(bytes)
    return digest.joinToString("") { b -> "%02x".format(b.toInt() and 0xff) }
}

/*
 * ---------------------------------------------------------------------------
 * Integration checklist (Android `ProjectBackupZip` or equivalent):
 *
 * 0. **Single-cave Save ZIP (current survey only):** When the user exports the **currently open**
 *    cave only, the ZIP must contain **no other surveys**: `data.json` is a one-element project
 *    array (not the full device database), `map_inventory.json` has **one** `projects[]` row,
 *    and `photos/` / `export_assets/` entries belong **only** to that cave. See `MapsWindowsSync.md` §0.
 *    A separate "export all caves" product action (if any) is a different contract.
 *
 * 1. Serialize projects to JSON (as today). For each project, **before** writing `data.json`:
 *    - Enumerate map-related URIs your app already knows (plan raster, TLS mesh OBJ, library
 *      cartography files, LIDAR rasters, sketches on disk — mirror what the PC collects from
 *      Gson keys: see caveaiproforwindows `MapAssetsCollector` / README "Cartography").
 *    - For each local file, add a [MapBytesProvider] and call [bundleMapsForZipProject].
 *      Where the source is not already PNG/TIFF/JPEG/WebP/BMP/PDF, **decode and re-encode**
 *      (e.g. `Bitmap.compress(PNG)` / `PdfDocument` / your GIS pipeline) so the bytes written
 *      to the ZIP use an **interchange** extension from [guessExtensionFromUriOrPath]'s known set.
 *
 * 2. Write the returned patched JSON into the ZIP as **`data.json`** (array of projects).
 *
 * 3. If you collected `MapInventoryProject` for the cave(s) in **this** ZIP, write:
 *    `zip.putNextEntry(ZipEntry("map_inventory.json"))` + `MapInventoryJsonRoot(...).toJsonObject().toString()`
 *    (single-cave ZIP → exactly **one** project in `projects[]`).
 *
 * 4. In **`backup_manifest.json`**, set `"includes_map_inventory": true` when the inventory exists.
 *
 * 5. Build **`integrity_manifest.json`** after all entries are added: `"files": { "path/in/zip": "hex" }`
 *
 * Paths in JSON must use **forward slashes** (`export_assets/maps/...`) so they match ZIP entry names.
 *
 * Expected layout (each map isolated):  
 * `export_assets/maps/{CaveName__hash10}/{000_slotLabel_xxxxxxxx}/{file}.ext`
 * ---------------------------------------------------------------------------
 */
