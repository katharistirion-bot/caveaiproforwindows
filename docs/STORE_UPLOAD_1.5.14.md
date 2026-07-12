# Microsoft Store upload — CAVE AI PRO **1.5.14**

Product ID **9PPF3HPZRL21** · Partner Center: https://partner.microsoft.com/dashboard

Generated: 2026-07-12 · Explore terrain parity, expedition share, followed contributors.

## MSIX artifacts (unsigned)

| Package | Path | When to use |
|---------|------|-------------|
| **Production** | `_store_out\CaveAiProForWindows-1.5.14-Store-unsigned.msix` | Store update after certification passes |
| **Review (certification)** | `_store_out\CaveAiProForWindows-1.5.14-Store-Review-unsigned.msix` | Microsoft certification only (subscription bypass) |

~97 MB each · Microsoft re-signs on upload — **do not sign locally**.

## GitHub sideload (parallel channel)

Velopack installer on GitHub: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.5.14

Use Store MSIX for Microsoft Store listing; GitHub Setup.exe for sideload + auto-update.

---

## Partner Center — production update

1. Sign in to [Partner Center](https://partner.microsoft.com/dashboard) → **Apps and games** → **CAVE AI PRO**.
2. **Packages** → **Upload new package** → select **`CaveAiProForWindows-1.5.14-Store-unsigned.msix`**.
3. Confirm identity matches Product identity (`GeorgiosKourentzis.CaveAIPro`, publisher CN from manifest).
4. Paste release notes (below) → **Submit for certification** (or publish if auto-approval).

### Release notes (English)

```
• Explore terrain deep links — reference catalog and field trip planner open web Explore map with viewport and layer params.
• Hydrology scout — karst and hydrology layer flags on Explore URLs (website/Android parity).
• Expedition share — active subscriber shares on surface map layer.
• Followed contributors — Public Library followed publishers and in-app profile completion.
• Cross-platform Explore URL golden tests and CI MSIX packaging fix.
• Surface map WebView stability and survey corridor geometry hardening.
```

---

## Partner Center — certification (review MSIX)

Use only if Microsoft requires a review build (new listing, capability review, or rejection resubmit):

1. Build: `.\tools\package-store-msix-review.ps1 -RepoRoot D:\caveaiproforwindows`
2. Upload **`CaveAiProForWindows-1.5.14-Store-Review-unsigned.msix`** to **Packages**.
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

- [ ] Production MSIX built (`package-store-msix.ps1`)
- [ ] Review MSIX built (`package-store-msix-review.ps1`)
- [ ] GitHub Release v1.5.14 published (sideload channel)
- [ ] Partner Center upload (manual)
- [ ] Firestore rules deployed from website/Android (`npm run deploy:rules`)

See also: `docs/STORE_UPLOAD_CHECKLIST.md`, `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`, website `docs/ECOSYSTEM_STORE_RELEASE.md`.
