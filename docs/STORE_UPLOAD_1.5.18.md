# Microsoft Store upload — CAVE AI PRO **1.5.18**

Product ID **9PPF3HPZRL21** · Partner Center: https://partner.microsoft.com/dashboard

Generated: 2026-08-29 · Named cartography CRUD + persist fixes (see `CHANGELOG.md`).

## MSIX artifacts (unsigned)

| Package | Path | When to use |
|---------|------|-------------|
| **Production** | `_store_out\CaveAiProForWindows-1.5.18-Store-unsigned.msix` | Store update after certification passes |
| **Review (certification)** | `_store_out\CaveAiProForWindows-1.5.18-Store-Review-unsigned.msix` | Microsoft certification only (subscription bypass) |

Microsoft re-signs on upload — **do not sign locally**.

## GitHub sideload (parallel channel)

Velopack installer on GitHub: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.5.18

Use Store MSIX for Microsoft Store listing; GitHub Setup.exe for sideload + auto-update.

---

## Partner Center — production update

1. Sign in to [Partner Center](https://partner.microsoft.com/dashboard) → **Apps and games** → **CAVE AI PRO**.
2. **Packages** → **Upload new package** → select **`CaveAiProForWindows-1.5.18-Store-unsigned.msix`**.
3. Confirm identity matches Product identity (`GeorgiosKourentzis.CaveAIPro`, publisher CN from manifest).
4. Paste release notes (below) → **Submit for certification** (or publish if auto-approval).

### Release notes (English)

```
• Named cartography — New / Save as / Rename / Delete on Plan view (Android parity).
• Named maps persist — sync live drawings into the open named map before JSON/ZIP/cloud write.
• Glyph cache — reject PNG bytes wrongly cached as MapLibre glyph protobuf.
• OWNER entitlement — Desktop Companion accepts Firestore entitlementSource OWNER (parity with Android/web).
```

---

## Partner Center — review / certification package

1. Build with `.\tools\package-store-msix-review.ps1`.
2. Upload **`CaveAiProForWindows-1.5.18-Store-Review-unsigned.msix`** to **Packages**.
3. Paste notes from `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`.
4. After cert passes, replace with the production MSIX from `package-store-msix.ps1` — **do not** ship the review build.

---

## Checklist

- [ ] Production MSIX built (`package-store-msix.ps1`)
- [ ] Review MSIX built (`package-store-msix-review.ps1`)
- [ ] GitHub Release v1.5.18 published (sideload channel)
- [ ] Partner Center upload (manual)
- [ ] Firestore rules deployed from website/Android (`npm run deploy:rules`)

See also: `docs/STORE_UPLOAD_CHECKLIST.md`, `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`, website `docs/ECOSYSTEM_STORE_RELEASE.md`.