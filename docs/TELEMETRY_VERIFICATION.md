# Desktop telemetry verification (Windows)

`ClientErrorTelemetryService` writes anonymised crash/error reports to Firestore collection **`desktop_client_telemetry`** when the user is signed in.

## Payload fields

| Field | Description |
|-------|-------------|
| `platform` | Always `windows` |
| `kind` | Error category (≤64 chars) |
| `messageHash` | SHA-256 of message (no raw PII) |
| `appVersion` | `AppMetadata.InformationalVersion` |
| `createdAtMs` | Unix ms |
| `stackPreview` | Optional redacted stack (≤4000 chars) |

## Rules (canonical — website repo)

Deploy from `D:\CaveAIpro website`:

```bash
npm run deploy:rules
```

Rules: create-only for authenticated users, `platform == 'windows'`, no read/update/delete from clients.

## How to verify

1. Sign in on Windows desktop build.
2. Trigger a handled error (or use dev harness if available).
3. Firebase Console → Firestore → `desktop_client_telemetry` → filter `platform == windows`.
4. Confirm new document with your `appVersion` and recent `createdAtMs`.

Local queue (offline): `%LOCALAPPDATA%\CaveAiProForWindows\telemetry-queue.jsonl` (max 40 lines).

## Windows repo stub

`firebase/firestore.rules` in this repo **denies all** writes — never deploy rules from Windows. `tools/verify-firestore-rules-stub.ps1` guards against accidental telemetry rules here.
