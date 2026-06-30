# Code signing (Authenticode)

Release builds can be signed in GitHub Actions when these repository secrets are configured:

| Secret | Description |
|--------|-------------|
| `WINDOWS_CERT_BASE64` | PFX file, base64-encoded |
| `WINDOWS_CERT_PASSWORD` | PFX password |

## CI pipeline

`release.yml` signs:

1. Published `CaveAiProForWindows.exe` (before packaging)
2. WiX MSI and Velopack `*-Setup.exe` (after `package-release.ps1`)

Script: `tools/sign-release.ps1` — uses Windows SDK `signtool` with SHA256 + DigiCert timestamp, then **`signtool verify /pa`**.

If secrets are missing, signing is skipped (exit 0) and a warning is printed.

**Production recommendation:** configure both secrets before tagging a public release. Unsigned `Setup.exe` / MSI builds trigger Windows SmartScreen warnings and erode user trust. The release workflow writes a prominent warning to the job log when `WINDOWS_CERT_BASE64` is absent on tag builds.

## Local signing

```powershell
$env:WINDOWS_CERT_BASE64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\path\to\cert.pfx'))
$env:WINDOWS_CERT_PASSWORD = 'your-password'
./tools/sign-release.ps1 -Files @(
  'path\to\CaveAiProForWindows.exe',
  'path\to\CaveAiProForWindows-Setup.msi'
)
```

For MSI-only local workflow, see also `installer/SIGNING.md`.

## Certificate recommendations

- **Standard code signing** — reduces SmartScreen friction over time
- **EV code signing** — immediate SmartScreen reputation (higher cost)
- Providers: SSL.com, DigiCert, Sectigo, etc.

Keep the private key offline; store only the base64 PFX in GitHub encrypted secrets.

## Verify a signed binary

```powershell
signtool verify /pa /v CaveAiProForWindows.exe
```

Or: file Properties → Digital Signatures tab.

## Installer shortcuts and protocol (verification)

After `tools/package-release.ps1`:

| Channel | Desktop shortcut | Start Menu | `caveaipro://` | `.json` / `.zip` FTA |
|---------|------------------|------------|----------------|----------------------|
| Velopack Setup | `--shortcutLocations StartMenu,Desktop` | Yes | Yes (app registration) | Yes (app registration) |
| WiX MSI | `Package.wxs` `Shortcut` elements | Yes | `Protocol` element | `ProgId` / extension tables |
| Store MSIX | `store/Package.appxmanifest` | Store-managed | `uap:Protocol` | `uap:FileTypeAssociation` |

Smoke-test on a clean VM: install → open sample `.zip` → follow `caveaipro://explore?...` link from browser.
