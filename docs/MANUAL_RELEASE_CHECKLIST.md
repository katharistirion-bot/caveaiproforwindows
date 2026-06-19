# Manual release checklist (human-only steps)

Items that **cannot** be automated from CI or local scripts. Complete after automated builds and smoke tests pass.

Related: `docs/STORE_UPLOAD_CHECKLIST.md` (Microsoft Store), `D:\CaveAIPro\docs\PLAY_RELEASE_*.md` (Play), `D:\CaveAIpro website\docs\PRODUCTION_CHECKLIST.md` (web ops).

---

## 1. Google Play — Internal testing (Android)

**When:** After `app-release.aab` is built locally (see `D:\CaveAIPro\docs\PLAY_RELEASE_1.7.7.md`).

1. Open [Google Play Console](https://play.google.com/console) → **Cave AI Pro** → **Testing** → **Internal testing**.
2. **Create new release** → upload AAB:
   ```
   D:\CaveAIPro\app\build\outputs\bundle\release\app-release.aab
   ```
3. Confirm **versionCode 88** / **versionName 1.7.7** in the release summary.
4. Paste release notes from `D:\CaveAIPro\docs\PLAY_RELEASE_1.7.7.md` (English section).
5. **Review and roll out** to internal testers.
6. On a physical device (internal track): Public Library map (world pins), search, Mission Control, deep link `https://www.caveaipro.com/map`.
7. Play Console → **Deep links** → re-run domain verification after rollout (can take up to 24h).

Do **not** promote to production until device QA passes.

---

## 2. DNS — Apex domain at Namecheap (or registrar)

**Why:** Play App Links and Firebase Hosting need apex `caveaipro.com` on Firebase, not a parking page.

1. Firebase Console → **Hosting** → **Add custom domain** → `caveaipro.com` (apex).
2. Copy the **A** and **TXT** records Firebase shows.
3. Namecheap → **Domain List** → **caveaipro.com** → **Advanced DNS**:
   - Remove parking / URL redirect records for `@`.
   - Add Firebase **A** records for `@`.
   - Add Firebase **TXT** verification record.
   - Keep **CNAME** `www` → `caveaipro-5950e.web.app` (if not already set).
4. Wait for Firebase to show domain **Connected** (often 15–60 minutes).
5. Verify:
   ```powershell
   curl.exe -I https://caveaipro.com
   curl.exe -I https://www.caveaipro.com
   ```
   Apex should 301 to www.

See `D:\CaveAIpro website\docs\CUSTOM_DOMAIN.md` for full detail.

---

## 3. Firebase App Check — Enforce (after metrics)

**Do not enforce until valid tokens appear in Metrics** (typically 24–48h of production traffic after deploy).

### Web app

1. Firebase Console → **App Check** → **CaveAI Pro Web** → confirm reCAPTCHA v3 site key is registered.
2. [Google reCAPTCHA Admin](https://www.google.com/recaptcha/admin) → your v3 key → **Domains** → add:
   - `www.caveaipro.com`
   - `caveaipro.com`
   - `localhost` (dev only)
3. App Check → **Metrics** → wait for **valid** web requests.
4. App Check → **APIs** → set **Cloud Firestore** and **Cloud Storage** to **Enforce**.
5. Re-test signed-in map, workspace JSON download, and Storage thumbnails on https://www.caveaipro.com.

### Android app

1. App Check → **Metrics** → confirm **Play Integrity** (and debug tokens in dev) show valid requests.
2. App Check → **APIs** → **Enforce** for Android: **Cloud Firestore**, **Cloud Storage**, **Cloud Functions** (when ready).

CLI cannot toggle enforce mode — Console only.

---

## 4. reCAPTCHA Admin — domain verification

Required before or when enforcing App Check on web.

1. https://www.google.com/recaptcha/admin → select the **App Check** reCAPTCHA v3 site key.
2. **Domains** → add `www.caveaipro.com`, `caveaipro.com`.
3. Save. No redeploy needed if site key unchanged; redeploy only if you rotate the key in `.env.local` / GitHub secrets.

---

## 5. Microsoft Store (Windows)

**When:** After CI release build is green and local review MSIX is tested.

1. Build review MSIX (subscription gate disabled for cert testers):
   ```powershell
   cd D:\caveaiproforwindows
   $cfg = Get-Content tools\local\firebase-web-config.json -Raw | ConvertFrom-Json
   $env:CAVEAIPRO_FIREBASE_API_KEY = $cfg.apiKey
   .\tools\package-store-msix-review.ps1
   ```
2. [Microsoft Partner Center](https://partner.microsoft.com/dashboard) → Product **9PPF3HPZRL21** → **Packages**.
3. Upload `D:\caveaiproforwindows\_store_out\CaveAiProForWindows-*-Store-Review-unsigned.msix` (unsigned).
4. Paste certification notes from `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`.
5. After certification passes, build **production** MSIX with `.\tools\package-store-msix.ps1` (no `STORE_REVIEW_UNLOCKED`) and upload as the next Store update.

See `docs/STORE_UPLOAD_CHECKLIST.md`.

---

## 6. Device QA (all platforms)

- [ ] Android internal track: map, search, sign-in, publish flow, deep links
- [ ] Web production: sign-in, workspace, field trip share, Public Library
- [ ] Windows: sign-in (WebView2), subscription gate, Public Library sync

---

## Quick reference — artifact paths

| Platform | Artifact | Path |
|----------|----------|------|
| Android | Release AAB | `D:\CaveAIPro\app\build\outputs\bundle\release\app-release.aab` |
| Windows | CI publish exe | GitHub Actions artifact `CaveAiProForWindows-win-x64` |
| Windows | Store review MSIX | `D:\caveaiproforwindows\_store_out\*-Store-Review-unsigned.msix` |
| Web | Hosting | `npm run deploy:prod` from `D:\CaveAIpro website` (only when data/code changed) |
