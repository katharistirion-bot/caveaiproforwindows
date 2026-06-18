# Cave AI context cloud API — stub contract (not deployed)

**Status:** Specification only. Production Firebase `caveAiContext` endpoint is **deferred**. Offline-first brain (`LocalCaveAiBrain.kt` / Windows `CaveAiOfflineBrain.cs`) remains authoritative in the field.

## Purpose

Optional future endpoint to enrich signed-in clients with **non-secret** survey context from Firestore/Storage without replacing on-device telemetry.

## Request (POST `/v1/caveAiContext`)

```json
{
  "client": "android | web | windows",
  "clientVersion": "1.7.4",
  "uid": "<Firebase Auth uid>",
  "projectRef": {
    "sourceLocalCaveId": "uuid",
    "publishedDocId": "optional"
  },
  "siteIdentity": {
    "surveySiteType": "CAVE | MINE | POTHOLE | SPRING",
    "layer": "active_survey | community_published | reference_catalog"
  }
}
```

## Response (200)

```json
{
  "greetingVersion": "1",
  "siteSummaryLine": "English one-liner from SiteIdentity contract",
  "pendingGeoSamples": 0,
  "entitlements": {
    "cloudAi": true,
    "publicLibrary": true
  },
  "hints": ["optional English strings — max 3"]
}
```

## Security

- Requires Firebase Auth ID token (`Authorization: Bearer`).
- No raw survey JSON in response — counts and summary lines only.
- Rate limit per uid; no PII in logs.

## Client fallback

If endpoint unavailable (404/5xx/offline), clients **must** use:

- Android: `LocalCaveAiBrain` + `SiteIdentity.resolveActiveProject`
- Web: `siteIdentity.js` + `buildCaveAiHomeGreeting`
- Windows: `CaveAiOfflineBrain.Answer`

## Related contracts

- `SurveySiteTypeContract.md`
- `site-identity-fixture.json`
- `GreetingVersionContract.md`
