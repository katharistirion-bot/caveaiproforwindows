# Cave AI home greeting — version alignment (Android + web)

When the **home greeting card** content or dismiss behavior changes materially, reset user dismiss state on both platforms.

## Android

- Dismiss TTL and version gate: `CaveAiGreetingDismissPrefs` (`CaveAI_Prefs`).
- **Version key:** `cave_ai_home_greeting_dismissed_version_code` stores `BuildConfig.VERSION_CODE`.
- **Source of truth:** `gradle.properties` → `caveApp.versionCode` (e.g. `85`).

Bump `caveApp.versionCode` on Play releases when greeting UX changes; existing dismiss records auto-expire on the new code.

## Web

- Dismiss TTL and version gate: `src/utils/caveAiHomeGreetingDismiss.js`.
- **Version key:** `CAVE_AI_HOME_GREETING_VERSION` (string, e.g. `'1'`).
- Storage keys: `cave_ai_home_greeting_dismissed*`.

Bump `CAVE_AI_HOME_GREETING_VERSION` when greeting copy/chips/behavior changes on the web landing page.

## Cross-platform checklist

1. Change greeting on Android **and/or** web.
2. If behavior/copy is shared intent, bump **both** Android `versionCode` (release) and web `CAVE_AI_HOME_GREETING_VERSION`.
3. Document the bump in release notes — users may see the greeting once after update.

## Not in scope

- Active-project HUD greeting (`CaveAiActiveProjectContext`) — per-project dismiss, not version-gated.
- Light analytics counters (`CaveAI_LightAnalytics`) — see `CaveAiLightAnalyticsExport.dumpCountsJson()`.
