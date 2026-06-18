# Gemini / Cloud AI Usage Audit — Android (CaveAI Pro)

**Repo:** `D:\CaveAIPro`  
**Date:** 2026-06-17  
**Scope:** Factual inventory only — no removals in this pass. Web/Windows: **no Gemini** (confirmed). Strategy: gradual Android sunset, offline-first.

**Summary:** **12 feature-level call flows** invoke cloud AI; **3 shared transport helpers** plus **2 HTTP clients** implement the network layer. Assistant chat and HUD voice already have on-device fallbacks; geo/bio vision, field-photo screening, safety assessment, and trip-end narrative remain cloud-dependent or partially covered.

---

## Feature-level call sites

| # | File | Function / feature | What it does | Offline replacement | Sunset notes |
|---|------|-------------------|--------------|---------------------|--------------|
| 1 | `MainActivity.kt` | `buildAssistantChatReply` | In-app **CaveAI Assistant** multi-turn chat (telemetry + system prompt) | **covered** | Falls back to `buildOfflineChatAssistantReply` / `LocalCaveAiBrain` when offline, quota, or Gemini unavailable. UI now shows reply-source badge. |
| 2 | `MainActivity.kt` | `analyzeRockInCloud` | **Geo/Bio field sample** vision — title + description from photo | **partial** | No on-device vision classifier; user can still capture photos and edit text manually. Worker/UI retry via `rockNeedsGeminiCloudAnalysis`. |
| 3 | `MainActivity.kt` | `autoSyncPendingRocks` | Foreground **batch geo/bio sync** for pending rocks | **partial** | Orchestrates `analyzeRockInCloud`; same offline gap as #2. Schedules `GeologicalSamplesAutoSyncWorker` on transient failure. |
| 4 | `MainActivity.kt` | `resyncSingleRock` | Manual **per-rock resync** from rock data UI | **partial** | Same vision path as #2. |
| 5 | `MainActivity.kt` | `appendSurveyPhotoUris` | **Survey photo attach** — speleology relevance screen before staging | **partial** | Calls `screenImageUriForSpeleologyFieldUse`. Offline: blocked unless user allows unverified offline media; geo/bio context bypasses screen when offline. |
| 6 | `CaveFieldImageRelevanceScreening.kt` | `screenImageUriForSpeleologyFieldUse` | **Field photo moderation** (allowed / reason JSON) for cave-relevant content | **partial** | On-device `FieldImageQualityPreFilter` (blur/exposure) runs before upload; geo/bio samples can bypass when offline (`offlineUnverifiedBypass`). No local relevance model. |
| 7 | `MainActivityRockDataScreens.kt` | Rock capture / gallery flows | **Geo/Bio sample** attach + resync UI | **partial** | Multiple call sites to `screenImageUriForSpeleologyFieldUse` and `analyzeRockInCloud`. |
| 8 | `AdvancedCaveDialog.kt` | Cave photo pick | **Advanced cave** photo screening | **partial** | `screenImageUriForSpeleologyFieldUse` before accept. |
| 9 | `SafetyMissionControlViewModel.kt` | Safety run (`runGeminiAssistantChat`) | **Safety Mission Control** — weather + entrance risk narrative | **partial** | `SafetyAssessmentCache` serves last successful assessment offline; live refresh requires cloud. Assistant reads cache via `SafetyAssessmentCache.buildAssistantEntranceWeatherSystemNote`. |
| 10 | `MainActivityHudScreen.kt` | Post-shot **HUD voice** (TTS) | After anchoring a station, Gemini drafts a short spoken summary | **covered** | `buildOfflineTtsShotFallback` used when Gemini unusable or on failure path before cloud attempt. |
| 11 | `TripEndNarrativeGemini.kt` | `generateTripEndNarrativeWithGemini` | **Trip-end expedition narrative** from project digest | **none** | Fails with user message when cloud unavailable; no offline narrative generator. |
| 12 | `TripEndNarrativeDialog.kt` | Trip-end UI | Invokes #11 from dialog | **none** | Depends entirely on #11. |

---

## Background / worker call sites

| # | File | Function | What it does | Offline replacement | Sunset notes |
|---|------|----------|--------------|---------------------|--------------|
| 13 | `GeologicalSamplesAutoSyncWorker.kt` | `doWork` → `analyzeRockInCloudForWorker` | **WorkManager** batch geo/bio analysis (up to 8 pending rocks) | **partial** | Skips when `!isGeminiUsableFromClient`; same vision stack as #2. Uses `runRockVisionWithModelFallback` directly. |

---

## Shared transport & HTTP layer (not user-facing features)

| # | File | Symbol | Role | Offline replacement | Sunset notes |
|---|------|--------|------|---------------------|--------------|
| T1 | `CaveGeminiHelpers.kt` | `runGeminiAssistantChat` | Multi-turn **text chat** (proxy → REST → SDK fallback) | **n/a** | Used by #1 and #9. Remove after callers migrated. |
| T2 | `CaveGeminiHelpers.kt` | `runGeminiVisionForJsonObject` | **Vision → JSON** classifier | **n/a** | Used by #6. Model list: `GEMINI_SAMPLE_VISION_MODELS`. |
| T3 | `CaveGeminiHelpers.kt` | `runRockVisionWithModelFallback` | **Vision → rock title/description** | **n/a** | Used by #2 and #13. |
| T4 | `GeminiProxyClient.kt` | `postGenerateContent`, `buildChatBody`, `buildVisionBody`, `buildTextPromptBody` | **HTTPS proxy** to Generative Language API (release path) | **n/a** | Requires Firebase + App Check (`CaveFirebase.isReady`). |
| T5 | `GeminiAndroidRestClient.kt` | `postGenerateContent` | **Direct REST** with Android app restriction headers (debug / fallback) | **n/a** | Used when proxy not configured and debug API key present. |
| T6 | `TripEndNarrativeGemini.kt` | Inline `postGenerateContent` / `GenerativeModel` | Trip narrative (does not use T1) | **n/a** | Duplicate transport pattern; consolidate on sunset. |
| T7 | `MainActivityHudScreen.kt` | Inline `postGenerateContent` / `GenerativeModel` | HUD TTS (does not use T1) | **n/a** | Duplicate transport pattern. |

---

## Configuration, gates & state (no network call by themselves)

| File | Symbol | Purpose | Offline replacement | Sunset notes |
|------|--------|---------|---------------------|--------------|
| `MainActivity.kt` | `effectiveGeminiApiKey()` | Debug API key from `BuildConfig` | **n/a** | Release: empty key; proxy only. |
| `CaveGeminiHelpers.kt` | `isGeminiUsableFromClient` | Proxy + Firebase or debug key gate | **n/a** | Central gate for most features. |
| `CaveGeminiHelpers.kt` | `geminiApiKeyFromBuild`, `geminiUnavailableReasonForUser` | Key + user-facing unavailable text | **n/a** | |
| `CaveSurveyModels.kt` | `rockNeedsGeminiCloudAnalysis` | Pending rock filter (`!isAnalyzed && imageUri`) | **n/a** | Drives sync badges/counts in dashboard, settings, HUD. |
| `CaveAssistantAppHealthContext.kt` | Proxy configured line | Assistant **app health** introspection block | **n/a** | Informational for assistant context only. |
| `HomeDashboardScreen.kt`, `MainActivitySettingsScreen.kt`, `MainActivityMainAppContent.kt`, `CaveAiActiveProjectContext.kt`, `CaveAssistantContext.kt` | `rockNeedsGeminiCloudAnalysis` counts | **Pending geo/bio** UI indicators | **n/a** | Remove or repurpose when cloud vision sunsets. |
| `FieldImageQualityPreFilter.kt` | `evaluateFieldImageQuality` | Pre-cloud quality shield | **covered** | Keep — reduces need for cloud screening. |
| `FieldMediaOnlineGate.kt` / `FieldOperationsPrefs` | Offline media gates | Block or allow unverified offline photos | **covered** | Policy layer for #5–#8. |

---

## Call-site count (for sunset tracking)

| Category | Count |
|----------|------:|
| Feature-level flows that reach Gemini | **12** |
| Worker-only flows | **1** (included in #13 above; same vision as #2) |
| Shared transport helpers | **3** (T1–T3) |
| HTTP client entry points | **2** (T4–T5) + **2** inline duplicates (T6–T7) |

**Distinct network-invoking symbols:** `runGeminiAssistantChat`, `runGeminiVisionForJsonObject`, `runRockVisionWithModelFallback`, `GeminiProxyClient.postGenerateContent`, `GeminiAndroidRestClient.postGenerateContent`, `GenerativeModel.generateContent` (SDK, debug/fallback), plus inline HUD and trip-narrative calls.

---

## Offline-first coverage matrix

| Capability | Status | On-device path |
|------------|--------|----------------|
| Assistant Q&A | **covered** | `buildOfflineChatAssistantReply`, `LocalCaveAiBrain` |
| HUD shot voice | **covered** | `buildOfflineTtsShotFallback` |
| Geo/Bio photo analysis | **partial** | Manual fields; pending/retry state |
| Field photo relevance | **partial** | Quality pre-filter; offline bypass flags |
| Safety assessment | **partial** | `SafetyAssessmentCache` (stale OK) |
| Trip-end narrative | **none** | — |
| App health / pending counts | **n/a** | Telemetry only |

---

## Recommended sunset order (documentation only)

1. **Trip-end narrative** (#11–#12) — no offline path; lowest risk to drop or replace with template text.
2. **Field photo screening** (#6, #8) — tighten offline bypass + quality shield; optional local heuristics later.
3. **Geo/Bio vision** (#2–#4, #7, #13) — largest quota consumer; defer until on-device or manual-only workflow is product-accepted.
4. **Safety live refresh** (#9) — keep cache; optional rule-based summary.
5. **Assistant + HUD** (#1, #10) — already offline-first; remove cloud last for transparency (badges help during transition).

---

## Search anchors used

`GeminiProxyClient`, `runGeminiAssistantChat`, `runGeminiVisionForJsonObject`, `runRockVisionWithModelFallback`, `rockNeedsGeminiCloudAnalysis`, `effectiveGeminiApiKey`, `CaveFieldImageRelevanceScreening`, `analyzeRockInCloud`, `generateTripEndNarrativeWithGemini`, `postGenerateContent`, `GenerativeModel.generateContent`, `isGeminiUsableFromClient`.
