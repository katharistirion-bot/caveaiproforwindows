# Microsoft Store upload — CAVE AI PRO **1.5.13**

Product ID **9PPF3HPZRL21** · Partner Center: https://partner.microsoft.com/dashboard

Generated: 2026-07-11 · Artifacts rebuilt 16:11 (includes `5db67f6` expedition share + profile readiness).

## MSIX artifacts (unsigned)

| Package | Path | When to use |
|---------|------|-------------|
| **Production** | `_store_out\CaveAiProForWindows-1.5.13-Store-unsigned.msix` | Store update after certification passes |
| **Review (certification)** | `_store_out\CaveAiProForWindows-1.5.13-Store-Review-unsigned.msix` | Microsoft certification only (subscription bypass) |

~97 MB each · Microsoft re-signs on upload — **do not sign locally**.

## GitHub sideload (parallel channel)

Velopack installer already on GitHub: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.5.13

Use Store MSIX for Microsoft Store listing; GitHub Setup.exe for sideload + auto-update.

---

## Partner Center — production update

1. Sign in to [Partner Center](https://partner.microsoft.com/dashboard) → **Apps and games** → **CAVE AI PRO**.
2. **Packages** → **Upload new package** → select **`CaveAiProForWindows-1.5.13-Store-unsigned.msix`**.
3. Confirm identity matches Product identity (`GeorgiosKourentzis.CaveAIPro`, publisher CN from manifest).
4. Paste release notes (below) → **Submit for certification** (or publish if auto-approval).

### Release notes (English)

```
• Expedition share on surface map — view active subscriber shares (contract parity with Android/web).
• Publisher profile readiness in cloud publish checklist (name, country required).
• Firebase App Check on Windows — WebView2 bridge + REST headers for protected Firestore calls.
• Security: hardened Public Library WebView auth redirects, redacted crash logs, authenticated field-trip reads.
• Near Me reference catalog — improved GPS accuracy for radius filter.
• Cross-platform publish draft sync and UTF-8 contract parity with Android/web.
```

---

## Partner Center — certification (review MSIX)

Use only if Microsoft requires a review build (new listing, capability review, or rejection resubmit):

1. Build: `.\tools\package-store-msix-review.ps1 -RepoRoot D:\caveaiproforwindows`
2. Upload **`CaveAiProForWindows-1.5.13-Store-Review-unsigned.msix`** to **Packages**.
3. **App capabilities** → **Run full trust** — paste justification from `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`.
4. **Notes for certification** → paste full block from same doc.
5. After **pass**, upload **production** MSIX (above) — **never** ship review build to production.

---

## Rebuild

```powershell
cd D:\caveaiproforwindows
$cfg = Get-Content tools\local\firebase-web-config.json -Raw | ConvertFrom-Json
$env:CAVEAIPRO_FIREBASE_API_KEY = $cfg.apiKey
.\tools\package-store-msix.ps1 -RepoRoot D:\caveaiproforwindows
```

Review build: `.\tools\package-store-msix-review.ps1 -RepoRoot D:\caveaiproforwindows`

---

## Pre-upload checklist

- [x] Production MSIX built (`package-store-msix.ps1`)
- [x] Review MSIX built (`package-store-msix-review.ps1`)
- [x] GitHub Release v1.5.13 published (sideload channel)
- [ ] Partner Center upload (manual)
- [ ] Firestore rules deployed from website/Android (`npm run deploy:rules`)

See also: `docs/STORE_UPLOAD_CHECKLIST.md`, `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`, website `docs/ECOSYSTEM_STORE_RELEASE.md`.
