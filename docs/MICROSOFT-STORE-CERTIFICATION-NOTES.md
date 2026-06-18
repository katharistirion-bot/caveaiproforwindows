# Microsoft Store — Notes for Certification (copy-paste)

Product ID: **9PPF3HPZRL21**  
Package: **Cave AI Pro** (Windows Desktop Companion)

---

## Store review MSIX (subscription bypass — certification only)

Use this **temporary** build so Microsoft reviewers can test all features **without** a Google test account or Firestore entitlement. After certification **passes**, upload the **normal** MSIX from `package-store-msix.ps1` (subscription gate restored).

### Build review MSIX

```powershell
$env:CAVEAIPRO_FIREBASE_API_KEY = '<your-api-key>'
.\tools\package-store-msix-review.ps1
```

Output: `_store_out\CaveAiProForWindows-<version>-Store-Review-unsigned.msix`

Compile flag: **`MICROSOFT_TEST_MODE`** (alias **`STORE_REVIEW_UNLOCKED`**, via publish profile `MicrosoftStore-Review-Win64` only). The app shows a banner: *Microsoft certification test mode — offline demo (no sign-in or Firebase)*, skips Google sign-in on launch, blocks cloud/Firebase features, and auto-loads bundled demo survey data.

### runFullTrust justification (Partner Center)

When Partner Center asks **Why do you need runFullTrust?**, paste:

```
Cave AI Pro is a full-trust Windows desktop companion for professional cave surveyors (WPF / .NET 8, EntryPoint Windows.FullTrustApplication). We require runFullTrust because core features use standard Win32 desktop APIs unavailable to sandboxed Store apps: (1) open/save/export survey projects via normal file dialogs and user-chosen folders; (2) interactive plan/section views and QC tools via full-trust WPF; (3) optional cloud sync for subscribers via WebView2 + Firebase; (4) self-contained .NET runtime with Windows.Desktop execution model. The app does not modify system settings or install drivers — it is a productivity tool for field survey data.
```

Also declare **Run full trust** under App capabilities before Submit.

### After certification passes

1. Run `.\tools\package-store-msix.ps1` (normal profile — **no** `STORE_REVIEW_UNLOCKED`).
2. Upload that MSIX as the next Store update (same package identity, version bump).
3. **Do not** leave the review build in production — it disables subscription enforcement.

### Normal production Store build

```powershell
$env:CAVEAIPRO_FIREBASE_API_KEY = '<your-api-key>'
.\tools\package-store-msix.ps1
```

Output: `_store_out\CaveAiProForWindows-<version>-Store-unsigned.msix`

---

## Copy-paste: Notes for Certification (review MSIX)

Paste into Partner Center when submitting the **review** MSIX:

```
--- BEGIN NOTES FOR CERTIFICATION ---

Product ID: 9PPF3HPZRL21
App: Cave AI Pro — Windows Desktop Companion (full-trust desktop, WPF + WebView2).

THIS SUBMISSION IS A STORE REVIEW BUILD
- Google sign-in, Firebase, and subscription checks are disabled for Microsoft certification testing only.
- Launch the app — the main window opens directly with bundled demo survey "Certification Demo Cave" (banner: "Microsoft certification test mode — offline demo (no sign-in or Firebase)").
- No test Google account, password, or internet connection is required for this submission.
- Cloud menus (Public Library, Publish to Cloud, collaboration, batch AI) show an informational message instead of contacting Firebase.
- The production Store update after certification will restore Google sign-in and subscription verification.

TECHNICAL NOTES
- Self-contained .NET 8 desktop app (runtime bundled in MSIX; no separate .NET install).
- Uses Microsoft Edge WebView2 (Evergreen) where web content is shown.
- Capabilities: internetClient, runFullTrust (full-trust desktop survey tool).

TEST STEPS
1. Install and launch Cave AI Pro.
2. Confirm the review banner appears, demo cave "Certification Demo Cave" loads automatically, and the Plan / Survey QC tabs show traverse data without sign-in.
3. Optional: File → Open to load another survey backup, or explore local export menus (Survex / Therion / DXF).

CONTACT
Developer: Georgios Kourentzis
Support: https://www.caveaipro.com/

--- END NOTES FOR CERTIFICATION ---
```

---

## Legacy: test account path (normal Store build)

If you submit the **normal** MSIX (`package-store-msix.ps1`) instead of the review build, use the section below (Google test account + Firestore entitlement).

Use this document when resubmitting in Partner Center → **Submission options** → **Notes for certification**. Paste the block below and replace the placeholders marked `[FILL IN]`.

---

## BEFORE you resubmit (developer checklist)

Complete these steps **before** uploading the MSIX and submitting for certification:

1. **Create a dedicated Google test account** (do not use your personal account):
   - Example: `caveaipro.msstore.review@gmail.com` (any unused Gmail is fine)
   - Set a strong password you can share with Microsoft reviewers

2. **Provision Firestore entitlement** for that account (choose **one** method):

   ### Option A — Firebase Console (recommended, fastest for reviewers)

   1. Sign in to [Firebase Console](https://console.firebase.google.com/) → project **`caveaipro-5950e`**
   2. Open **Authentication** → **Users** → **Add user** with the test Gmail (or sign in once on the Windows app to create the user, then find their UID)
   3. Copy the user's **UID** (e.g. `AbCdEf1234567890`)
   4. Open **Firestore Database** → collection **`user_entitlements`**
   5. Create document ID = **UID** with these fields:

   | Field | Type | Value |
   |-------|------|--------|
   | `status` | string | `ACTIVE` |
   | `entitlementSource` | string | `PLAY_SUBSCRIPTION` |
   | `premiumCloudUntil` | timestamp | **1 year in the future** (e.g. 2027-06-10) |

   6. Save. No `app_install_grace` record is needed for `PLAY_SUBSCRIPTION`.

   ### Option B — Android app + Play license tester

   1. In [Google Play Console](https://play.google.com/console/) → **Setup** → **License testing**, add the test Gmail as a license tester
   2. Install **CaveAI Pro** on an Android device/emulator, sign in with the test account
   3. Start a subscription or free trial (license testers are not charged)
   4. Wait for the Android app to sync — this writes `user_entitlements/{uid}` via the `verifyPlaySubscription` Cloud Function
   5. Confirm the Firestore document exists before resubmitting

   **Cross-platform note:** Play license testers work on Windows **only after** the entitlement document exists in Firestore for that Google account's Firebase UID. Installing/signing in on Android is the usual way to create it; Option A skips Android.

3. **Verify the test account on Windows** (sideload or previous Store build):
   - Launch app → **Sign in with Google** → use the test account
   - Main window should open (not "Subscription required")
   - If you see "Subscription required", entitlement is missing or expired — fix Firestore before resubmitting

4. **Rebuild Store MSIX** after any code changes:

   **Certification (review build — no test account):**

   ```powershell
   $env:CAVEAIPRO_FIREBASE_API_KEY = '<your-api-key>'
   .\tools\package-store-msix-review.ps1
   ```

   **Production Store build (after cert passes):**

   ```powershell
   $env:CAVEAIPRO_FIREBASE_API_KEY = '<your-api-key>'
   .\tools\package-store-msix.ps1
   ```

   Upload the new unsigned `.msix` from `_store_out\`.

---

## Copy-paste: Notes for Certification

Paste everything between the lines into Partner Center.

```
--- BEGIN NOTES FOR CERTIFICATION ---

Product ID: 9PPF3HPZRL21
App: Cave AI Pro — Windows Desktop Companion for cave surveyors (full-trust desktop, WPF + WebView2).

TECHNICAL NOTES
- Self-contained .NET 8 desktop app (runtime bundled in MSIX; no separate .NET install).
- Uses Microsoft Edge WebView2 (Evergreen) for Google sign-in; preinstalled on most Windows 11 PCs.
- Requires internet for Google sign-in and subscription verification (Firebase / Firestore).
- Capabilities: internetClient, runFullTrust (full-trust desktop survey tool).

SIGN-IN AND SUBSCRIPTION GATE (IMPORTANT)
- The app requires Google sign-in plus an active CaveAI Pro subscription or trial.
- Google sign-in via WebView2 is working when you reach the post-sign-in screen.
- If you see "Subscription required" (not a sign-in failure): Google authentication succeeded, but this Google account has no active CaveAI Pro entitlement in our backend. This is expected for personal accounts without a Play subscription.
- The Desktop Companion is a companion to the Android CaveAI Pro app; subscriptions are purchased on Google Play (Android). Windows verifies the same Google account against our Firestore entitlement record.

TEST ACCOUNT (USE THIS ACCOUNT — DO NOT USE A PERSONAL GOOGLE ACCOUNT)
Email:    [FILL IN: e.g. caveaipro.msstore.review@gmail.com]
Password: [FILL IN: password for the test account above]

This account has a pre-provisioned active CaveAI Pro entitlement (PLAY_SUBSCRIPTION) in Firebase Firestore (project: caveaipro-5950e, collection: user_entitlements).

TEST STEPS
1. Install and launch Cave AI Pro.
2. On the sign-in screen, tap "Sign in with Google" (or use the Google button in the WebView).
3. Sign in with the test account email and password above (not your personal account).
4. After "Verifying CaveAI Pro access…", the main application window should open.
5. Optional: File → open a sample project, or browse the Public Library link from the sign-in screen.

TROUBLESHOOTING
- "Subscription required" after sign-in: wrong Google account was used, or test entitlement expired. Use the test account above only.
- WebView2 blank page: ensure internet access; WebView2 Evergreen is required (standard on Windows 11).
- If the test account fails, contact the developer via Partner Center messaging with the signed-in email; we can re-provision entitlement within 24 hours.

CONTACT
Developer: Georgios Kourentzis
Support: https://www.caveaipro.com/

--- END NOTES FOR CERTIFICATION ---
```

---

## Why certification failed (reference)

| Policy | Finding | Resolution |
|--------|---------|------------|
| **10.1.2.10** | "Sign in with Google" → Access Denied | Not a broken sign-in — reviewer used an account without Play subscription. Provide test account + Firestore entitlement; clarify message in app ("Subscription required"). |
| **10.3.1** | No test credentials in submission notes | Paste the block above with real test email/password. |

---

## Related

- [MICROSOFT-STORE.md](MICROSOFT-STORE.md) — MSIX build and upload
- [firebase/README.md](../firebase/README.md) — `user_entitlements` schema
