# Microsoft Store upload — CAVE AI PRO **1.5.29**

Product ID **9PPF3HPZRL21** · Partner Center: https://partner.microsoft.com/dashboard

Generated: 2026-11-15 · Survey Cloud save, instrument legs, LiDAR outline, return-to-entrance GPS (see `CHANGELOG.md`).

## MSIX artifacts (unsigned)

| Package | Path | When to use |
|---------|------|-------------|
| **Production** | `_store_out\CaveAiProForWindows-1.5.29-Store-unsigned.msix` | Store update after certification passes |
| **Review (certification)** | `_store_out\CaveAiProForWindows-1.5.29-Store-Review-unsigned.msix` | Microsoft certification only (subscription bypass) |

Microsoft re-signs on upload — **do not sign locally**.

Build locally:

```powershell
.\tools\package-store-msix.ps1
.\tools\package-store-msix-review.ps1
```

Or download the CI artifact **CaveAiProForWindows-store-msix** from the green `store-msix-pack` job on `chore/initial-repository-import`.

## GitHub sideload (parallel channel)

Publish tag **v1.5.29** when ready (Velopack Setup.exe). Until that release exists, use Partner Center MSIX for Store users only.

Checklist:

- [ ] Production MSIX built or downloaded from CI
- [ ] Partner Center → Packages → upload `CaveAiProForWindows-1.5.29-Store-unsigned.msix`
- [ ] Release notes pasted (below)
- [ ] After Store is live, create GitHub Release `v1.5.29` for sideload (optional parallel channel)
- [ ] Do **not** deploy Firestore rules from this repo (stub only — deploy from website)

### Release notes (English)

```
• Signed-in save uploads the private survey to Survey Cloud without replacing a newer cloud copy.
• Tools → Survey instrument records a leg from a paired DistoX, CaveBLE, or BRIC.
• Tools → LiDAR scanner puts a wall outline on the plan from OBJ, XYZ, LAS, or a live scanner.
• Tools → Return to entrance shows live Windows GPS bearing and optional local alarm.
• Explore with no network opens the cached surface pack.
```

See also: `docs/STORE_UPLOAD_CHECKLIST.md`, `docs/MICROSOFT-STORE-CERTIFICATION-NOTES.md`, website `docs/ECOSYSTEM_STORE_RELEASE.md`.
