# Replicate proxy (Firebase Cloud Functions)

Secure middleman between CaveAI Pro Windows and the Replicate API. The Windows app calls a **Firebase Callable** function with its Firebase Auth ID token; the function verifies entitlement in Firestore and forwards the prediction using a server-side Replicate API key.

## Firestore schema (subscription check)

### `user_entitlements/{uid}`

| Field | Type | Notes |
|-------|------|--------|
| `status` | string | Must be `ACTIVE` or `TRIALING` |
| `premiumCloudUntil` | timestamp | Must be in the future |
| `entitlementSource` | string | `PLAY_SUBSCRIPTION`, `INSTALL_GRACE`, or empty (legacy) |
| `graceDocId` | string | Required when `entitlementSource` is `INSTALL_GRACE` — 64-char lowercase hex |

### `app_install_grace/{graceDocId}`

| Field | Type | Notes |
|-------|------|--------|
| `firstSeenMs` | int64 | UTC epoch ms when install grace started; window is **30 days** |

### `replicate_proxy_rate/{uid}` (written by function)

| Field | Type | Notes |
|-------|------|--------|
| `windowStartMs` | int64 | Start of current rate-limit window |
| `requestCount` | int64 | Renders in window (max **20/hour**) |

## Deploy

```bash
cd firebase/functions
npm install
firebase functions:secrets:set REPLICATE_API_TOKEN   # paste Replicate r8_… key
cd ..
firebase deploy --only functions:replicateGenerativeMap
```

### Collaboration + push (comments)

Also deploy:

```bash
firebase deploy --only functions:shareProjectCollaboration,functions:postProjectComment,functions:listProjectComments,functions:registerDeviceToken
```

Firestore collections: `shared_projects/{id}`, `shared_projects/{id}/comments/{id}`, `user_devices/{uid}/tokens/{id}`.

**Security:** `listProjectComments` and `postProjectComment` require project membership; all collaboration callables re-check `user_entitlements`. Deploy **`firestore.rules`** (deny direct client access; callables use Admin SDK):

```bash
firebase deploy --only firestore:rules
```

Requires Firebase CLI logged in (`firebase login`) and Blaze plan (Callable + Secret Manager).

### Android vs Windows — do not break Android

This repo deploys **Windows-only** callables in **`us-central1`**. The **Android app** uses separate functions in **`europe-west1`** (`geminiProxy`, `provisionInstallGraceEntitlement`, `verifyPlaySubscription`) deployed from the Android/Firebase codebase — they are **not** in this repo.

**Never run** bare `firebase deploy --only functions` from this folder: Firebase CLI will try to **delete** the Android functions. Always use the selective command below.

**Safe deploy (Windows + collaboration only):**

```bash
cd firebase
firebase deploy --only functions:replicateGenerativeMap,functions:shareProjectCollaboration,functions:postProjectComment,functions:listProjectComments,functions:registerDeviceToken
```

After deploy, confirm Android entries still exist:

```bash
firebase functions:list
# expect geminiProxy, provisionInstallGraceEntitlement, verifyPlaySubscription → europe-west1
```

## Verify functions compile (no deploy)

From repo root:

```bash
cd firebase/functions
npm install
npm test
node --check index.js
node --check src/collaboration.js
node --check src/replicateProxy.js
```

Deploy only when credentials are configured (do not run in CI without secrets). Use **selective** deploy (see “Android vs Windows” above) — not bare `--only functions`.

## Local emulator (optional)

```bash
cd firebase/functions
npm install
export REPLICATE_API_TOKEN=r8_…   # or use secrets in emulator config
cd ..
firebase emulators:start --only functions
```

## Windows app authentication

1. **Startup:** `LoginWindow` signs in with Google via WebView2 (`Assets/DesktopAuth/auth.html`).
2. **Token:** Firebase ID token (JWT) is captured and stored in `%LocalAppData%\CaveAiProForWindows\auth-token.json` via `FirebaseAuthTokenStore`.
3. **Entitlement gate:** Same token is used at startup to read `user_entitlements/{uid}` (`SubscriptionEntitlementService`).
4. **AI Render:** `ReplicateCallableProxyClient` POSTs to `https://us-central1-{projectId}.cloudfunctions.net/replicateGenerativeMap` with `Authorization: Bearer {idToken}` and body `{ "data": { "modelVersion", "input" } }`.

Dev override: set `CAVEAIPRO_REPLICATE_BYOK=1` to use a local Replicate token (Credential Manager) instead of the cloud proxy.
