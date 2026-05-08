/**
 * CaveAI Pro — **reference** helpers to enrich `data.json` / Gson `CaveProject` JSON before ZIP backup.
 *
 * **Copy into the Android app** next to your backup serializer. Wire from `ProjectBackupZip` (or equivalent)
 * after you build the per-project `JSONObject` / model, **before** `toString()` and `integrity_manifest` hashing.
 *
 * Windows expects the keys documented in `DataJsonSurveySchemaV2.md` (camelCase, Gson default).
 */
package com.caveai.pro.backup.reference

import org.json.JSONArray
import org.json.JSONObject

/**
 * Merges enrichment into one serialized project object (not the outer `[ ]` array wrapper).
 * Only adds keys when values are non-null / non-empty so older readers stay stable.
 */
fun mergeSurveyArchiveV2Enrichment(
    project: JSONObject,
    device: ExportDeviceContext? = null,
    calibration: SurveyCalibrationProfileJson? = null,
    aiTags: List<SurveyAiClassificationJson>? = null,
    stationSnapshots: List<StationEnvironmentSnapshotJson>? = null,
) {
    // Caller should set `surveyArchiveSchemaVersion` to "2" (or bump) once when any v2 block is written.
    device?.toJson()?.let { project.put("exportDeviceContext", it) }
    calibration?.toJson()?.let { project.put("surveyCalibrationProfile", it) }
    if (!aiTags.isNullOrEmpty()) {
        val arr = JSONArray()
        aiTags.forEach { arr.put(it.toJson()) }
        project.put("surveyAiClassifications", arr)
    }
    if (!stationSnapshots.isNullOrEmpty()) {
        val arr = JSONArray()
        stationSnapshots.forEach { arr.put(it.toJson()) }
        project.put("stationEnvironmentSnapshots", arr)
    }
}

data class ExportDeviceContext(
    val manufacturer: String? = null,
    val model: String? = null,
    val device: String? = null,
    val androidSdkInt: Int? = null,
    val appVersionName: String? = null,
    val appVersionCode: Long? = null,
    val exportEngineVersion: String? = null,
) {
    fun toJson(): JSONObject {
        val o = JSONObject()
        manufacturer?.takeIf { it.isNotBlank() }?.let { o.put("manufacturer", it) }
        model?.takeIf { it.isNotBlank() }?.let { o.put("model", it) }
        device?.takeIf { it.isNotBlank() }?.let { o.put("device", it) }
        androidSdkInt?.let { o.put("androidSdkInt", it) }
        appVersionName?.takeIf { it.isNotBlank() }?.let { o.put("appVersionName", it) }
        appVersionCode?.let { o.put("appVersionCode", it) }
        exportEngineVersion?.takeIf { it.isNotBlank() }?.let { o.put("exportEngineVersion", it) }
        return o
    }
}

/** Opaque `compassCalibrationJson` object — build from your internal calibration DTO. */
data class SurveyCalibrationProfileJson(
    val profileId: String? = null,
    val profileName: String? = null,
    val calibratedAtUtcMs: Long? = null,
    val magneticDeclinationAppliedDeg: Double? = null,
    val tapeCalibrationScale: Double? = null,
    val compassCalibrationJson: JSONObject? = null,
) {
    fun toJson(): JSONObject {
        val o = JSONObject()
        profileId?.takeIf { it.isNotBlank() }?.let { o.put("profileId", it) }
        profileName?.takeIf { it.isNotBlank() }?.let { o.put("profileName", it) }
        calibratedAtUtcMs?.let { o.put("calibratedAtUtcMs", it) }
        magneticDeclinationAppliedDeg?.let { o.put("magneticDeclinationAppliedDeg", it) }
        tapeCalibrationScale?.let { o.put("tapeCalibrationScale", it) }
        compassCalibrationJson?.let { o.put("compassCalibrationJson", it) }
        return o
    }
}

data class SurveyAiClassificationJson(
    val entityType: String,
    val entityRef: String? = null,
    val label: String? = null,
    val labelLocale: String? = null,
    val confidence: Double? = null,
    val modelId: String? = null,
    val modelVersion: String? = null,
) {
    fun toJson(): JSONObject {
        val o = JSONObject()
        o.put("entityType", entityType)
        entityRef?.let { o.put("entityRef", it) }
        label?.let { o.put("label", it) }
        labelLocale?.let { o.put("labelLocale", it) }
        confidence?.let { o.put("confidence", it) }
        modelId?.let { o.put("modelId", it) }
        modelVersion?.let { o.put("modelVersion", it) }
        return o
    }
}

data class StationEnvironmentSnapshotJson(
    val stationName: String,
    val capturedAtUtcMs: Long? = null,
    val ambientBleTempCelsius: Double? = null,
    val manualAmbientTempCelsius: Double? = null,
    val ambientBleRelativeHumidityPct: Double? = null,
    val manualRelativeHumidityPct: Double? = null,
    val barometricPressureHpa: Double? = null,
    val co2Ppm: Double? = null,
    val notes: String? = null,
) {
    fun toJson(): JSONObject {
        val o = JSONObject()
        o.put("stationName", stationName)
        capturedAtUtcMs?.let { o.put("capturedAtUtcMs", it) }
        ambientBleTempCelsius?.let { o.put("ambientBleTempCelsius", it) }
        manualAmbientTempCelsius?.let { o.put("manualAmbientTempCelsius", it) }
        ambientBleRelativeHumidityPct?.let { o.put("ambientBleRelativeHumidityPct", it) }
        manualRelativeHumidityPct?.let { o.put("manualRelativeHumidityPct", it) }
        barometricPressureHpa?.let { o.put("barometricPressureHpa", it) }
        co2Ppm?.let { o.put("co2Ppm", it) }
        notes?.let { o.put("notes", it) }
        return o
    }
}

/**
 * **Per-shot** instrument QC — call for each leg/splay `JSONObject` in `shots[]` when serializing.
 */
fun mergeShotInstrumentQc(
    shot: JSONObject,
    measurementStartedUtcMs: Long? = null,
    measurementCompletedUtcMs: Long? = null,
    compassSampleVarianceDeg2: Double? = null,
    clinoSampleVarianceDeg2: Double? = null,
    compassStdDeg: Double? = null,
    clinoStdDeg: Double? = null,
    tapeStdM: Double? = null,
    sensorFusionQuality: Double? = null,
    horizontalPositionAccuracyM: Double? = null,
    verticalPositionAccuracyM: Double? = null,
) {
    measurementStartedUtcMs?.let { shot.put("measurementStartedUtcMs", it) }
    measurementCompletedUtcMs?.let { shot.put("measurementCompletedUtcMs", it) }
    compassSampleVarianceDeg2?.let { shot.put("compassSampleVarianceDeg2", it) }
    clinoSampleVarianceDeg2?.let { shot.put("clinoSampleVarianceDeg2", it) }
    compassStdDeg?.let { shot.put("compassStdDeg", it) }
    clinoStdDeg?.let { shot.put("clinoStdDeg", it) }
    tapeStdM?.let { shot.put("tapeStdM", it) }
    sensorFusionQuality?.let { shot.put("sensorFusionQuality", it) }
    horizontalPositionAccuracyM?.let { shot.put("horizontalPositionAccuracyM", it) }
    verticalPositionAccuracyM?.let { shot.put("verticalPositionAccuracyM", it) }
}

/**
 * **Per sketch polyline object** — merge into each `sketches[]` / `sectionSketches[]` element that carries a path.
 */
fun mergeSketchStrokeStyle(
    stroke: JSONObject,
    strokeWidthPx: Double? = null,
    strokeWidthSurveyM: Double? = null,
    strokeColorArgb: Long? = null,
    strokeColor: String? = null,
    layerIndex: Int? = null,
    layerZOrder: Int? = null,
    layerName: String? = null,
    brushProfile: String? = null,
    textAnnotations: JSONArray? = null,
) {
    strokeWidthPx?.let { stroke.put("strokeWidthPx", it) }
    strokeWidthSurveyM?.let { stroke.put("strokeWidthSurveyM", it) }
    strokeColorArgb?.let { stroke.put("strokeColorArgb", it) }
    strokeColor?.takeIf { it.isNotBlank() }?.let { stroke.put("strokeColor", it) }
    layerIndex?.let { stroke.put("layerIndex", it) }
    layerZOrder?.let { stroke.put("layerZOrder", it) }
    layerName?.takeIf { it.isNotBlank() }?.let { stroke.put("layerName", it) }
    brushProfile?.takeIf { it.isNotBlank() }?.let { stroke.put("brushProfile", it) }
    textAnnotations?.let { stroke.put("textAnnotations", it) }
}
