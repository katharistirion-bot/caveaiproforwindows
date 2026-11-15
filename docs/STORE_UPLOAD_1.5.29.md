# Microsoft Store upload — CAVE AI PRO **1.5.29**

Product ID **9PPF3HPZRL21** · Partner Center: https://partner.microsoft.com/dashboard

Generated: 2026-11-15 · Survey Cloud save, instrument legs, LiDAR outline, return-to-entrance GPS (see `CHANGELOG.md`).

## MSIX artifacts (unsigned)

| Package | Path | When to use |
|---------|------|-------------|
| **Production** | `_store_out\CaveAiProForWindows-1.5.29-Store-unsigned.msix` | Store update after certification passes |
| **Review (certification)** | `_store_out\CaveAiProForWindows-1.5.29-Store-Review-unsigned.msix` | Microsoft certification only (subscription bypass) |

Microsoft re-signs on upload — **do not sign locally**.

## GitHub sideload (parallel channel)

Velopack installer on GitHub: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.5.29

Use Store MSIX for Microsoft Store listing; GitHub Setup.exe for sideload + auto-update.

---

## Partner Center — production update

1. Sign in to [Partner Center](https://partner.microsoft.com/dashboard) → **Apps and games** → **CAVE AI PRO**.
2. **Packages** → **Upload new package** → select **`CaveAiProForWindows-1.5.29-Store-unsigned.msix`**.
3. Confirm identity matches Product identity (`GeorgiosKourentzis.CaveAIPro`, publisher CN from manifest).
4. Paste release notes (below) → **Submit for certification** (or publish if auto-approval).

### Release notes (English)

```
• Signed-in save uploads the private survey to Survey Cloud without replacing a newer cloud copy.
• Tools → Survey instrument records a leg from a paired DistoX, CaveBLE, or BRIC.
• Tools → LiDAR scanner puts a wall outline on the plan from OBJ, XYZ, LAS, or a live scanner.
• Tools → Return to entrance shows live Windows GPS bearing and optional local alarm.
• Explore with no network opens the cached surface pack.
```

---

## Partner Center — review / certification package

1. Build with `.\tools\package-store-msix-review.ps1`.
2. Upload **`CaveAiProForWindows-1.5.29-Store-Review-unsigned.msix`** to **Packages**.
3. Paste notes from `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`.
4. After cert passes, replace with the production MSIX from `package-store-msix.ps1` — **do not** ship the review build.

---

## Checklist

- [ ] Production MSIX built (`package-store-msix.ps1`)
- [ ] Review MSIX built (`package-store-msix-review.ps1`)
- [ ] GitHub Release v1.5.29 published (sideload channel)
- [ ] Partner Center upload (manual)
- [ ] Firestore rules deployed from website/Android (`npm run deploy:rules`)

See also: `docs/STORE_UPLOAD_CHECKLIST.md`, `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`, website `docs/ECOSYSTEM_STORE_RELEASE.md`.
