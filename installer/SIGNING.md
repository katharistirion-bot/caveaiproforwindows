# MSI code signing (Windows)

Sign the CaveAiProForWindows MSI after building the installer so SmartScreen and enterprise deployment trust the package.

## Prerequisites

- Windows SDK (includes `signtool.exe`), typically under:
  `C:\Program Files (x86)\Windows Kits\10\bin\<version>\x64\signtool.exe`
- A code-signing certificate (.pfx) from a trusted CA, or an internal enterprise cert

## Sign the MSI

```powershell
$SignTool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
$Msi = ".\installer\out\CaveAiProForWindows.msi"
$Pfx = ".\certs\codesign.pfx"
$Password = Read-Host "PFX password" -AsSecureString

& $SignTool sign /fd SHA256 /f $Pfx /p ([Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))) /tr http://timestamp.digicert.com /td SHA256 $Msi
```

Verify:

```powershell
& $SignTool verify /pa /v $Msi
```

## Optional build script placeholder

Run from repo root after WiX build:

```powershell
.\installer\sign-msi.ps1 -MsiPath ".\installer\out\CaveAiProForWindows.msi" -PfxPath ".\certs\codesign.pfx"
```

The script is a stub — set `$SignTool` and certificate paths for your environment. Do not commit `.pfx` files or passwords.

## Notes

- Timestamp (`/tr`) keeps signatures valid after the cert expires.
- EV certificates reduce SmartScreen warnings faster than standard OV certs.
- For CI, store the PFX in a secret vault and invoke `signtool` in the release job only.
