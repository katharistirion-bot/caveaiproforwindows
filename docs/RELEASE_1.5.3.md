# Release 1.5.3 - Windows (2026-06-07)

## Version

| Field | Value |
|-------|-------|
| Version | **1.5.3** |
| Assembly | **1.5.3.0** |

## Artifacts

| Package | Path |
|---------|------|
| Microsoft Store MSIX | `_store_out\CaveAiProForWindows-1.5.3-Store-unsigned.msix` |
| Sideload EXE | `src\CaveAiProForWindows\bin\Release\net8.0-windows\publish\win-x64\CaveAiProForWindows.exe` |

## Build

```powershell
dotnet test tests\CaveAiProForWindows.Tests\CaveAiProForWindows.Tests.csproj -c Debug
.\tools\package-store-msix.ps1
.\tools\smoke-publish.ps1
```

Optional sideload ZIP + MSI:

```powershell
.\tools\package-release.ps1 -Tag v1.5.3
```

## Store release notes

Copy into Partner Center -> **Properties** -> **Description** (or per-language release notes):

- Sketch Editor: dedicated cave plan design after survey import (symbols, wall styles, LRUD assist, export)
- Survey Intelligence dashboard: offline QC, loop closure, backup compare, Android sync status
- Plan and Section tabs are view-only; all drawing lives in Sketch Editor
- Design-from-survey workflow after ZIP/JSON import; improved public library search
- Removed cloud AI features (Generative Map, Replicate) for a fully offline-first workstation
- Stability, performance, and export improvements (Therion, field trip PDF, cloud publish photos)

See also `docs/STORE_UPLOAD_CHECKLIST.md`.

## Partner Center upload

1. [Microsoft Partner Center](https://partner.microsoft.com/dashboard) -> **Apps and games** -> **CAVE AI PRO**
2. Create or open submission -> **Packages**
3. Upload `_store_out\CaveAiProForWindows-1.5.3-Store-unsigned.msix`
4. Confirm version **1.5.3.0** and identity **GeorgiosKourentzis.CaveAIPro**
5. Paste release notes above; submit for certification
