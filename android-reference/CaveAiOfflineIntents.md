# Cave AI offline intents — shared contract (v4)

**Status:** Authoritative for on-device Q&amp;A. No Gemini, no web chat model.

**Implementations:**

- Android: `LocalCaveAiBrain.kt` → `coreSurveyIntentRegistry()`
- Windows: `CaveAiOfflineBrain.cs`
- Fixture: `cave-ai-offline-intents-fixture.json` (contract tests on Android + Windows)

English-only user-visible strings.

## Intent IDs

| Intent ID | Example user phrases | Answer pattern |
|-----------|---------------------|----------------|
| `depth` | "how deep", "max depth", "vertical span" | Max depth span vs entrance in metres (`~X.X m`), optional HUD/GNSS reminder |
| `traverse` | "traverse length", "how long is the survey", "total length" | Total traverse on mains in metres + main-leg count |
| `entrance_distance` | "return distance", "distance to entrance", "how far to exit", "egress" | Horizontal-ish distance toward entrance from telemetry/geometry; suggest Cave exit guide when applicable |
| `shot_count` | "how many shots", "shot count", "mains vs splays" | Total shots with mains and splays broken out |
| `pending_geo` | "pending samples", "geo sync", "field samples waiting" | Count of geo/bio samples awaiting field analysis/sync — **never** mention Gemini or cloud AI |
| `site_type` | "what kind of site", "site type", "is this a mine" | `SiteIdentity` summary line (Cave / Mine / Pothole / Spring) |
| `next_action` | "what should I do next", "next step", "what now" | Rule-based priority: pending geo → Geo/Bio tab; 0 shots + entrance GPS locked → HUD; trailing splays → tie main leg; else continue HUD/survey |
| `last_leg` | "last leg", "last main leg", "latest main" | Latest main leg from→to, tape m, clino ° |
| `volume` | "passage volume", "how big", "how large" | Rough LRUD × leg prism volume in m³ (indicative) |
| `clino` | "latest clino", "inclination", "slope" (not rope/SRT) | Latest main clino ° with station pair |

## Greeting (separate)

Greeting / thanks / overview queries are **not** intents — handled by `greetingReply`, `thanksReply`, and `overviewReply` before intent routing.

## Telemetry (Android)

`LocalCaveAiTelemetry` fields used by intents:

- `distEntranceM`, `exitEtaMin`, `caveExitGuideEnabled` — return / egress
- `pendingGeoSamples` — pending geo (mirrors `rockNeedsGeminiCloudAnalysis` count; offline text says "field sample" / "geo/bio sync")
- `entranceGpsLocked` — entrance A1 lock state for next-action when shot count is zero
- `siteIdentityLine` — site type intent

Windows derives pending geo from `rocks[]` in backup JSON and entrance lock from `lat`/`lon` on the project.

## Related contracts

- `site-identity-fixture.json` / `SiteIdentity.kt` / `SiteIdentity.cs`
- `CaveAiContextApiStub.md` (cloud stub defers to offline brain)
