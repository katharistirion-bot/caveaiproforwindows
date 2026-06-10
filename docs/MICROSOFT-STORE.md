# Microsoft Store distribution — CAVE AI PRO for Windows

Packaging guide for submitting an **unsigned** `.msix` to Partner Center (the Store re-signs on upload).

## Build channel (codebase)

Store builds use the compile constant **`STORE_DISTRIBUTION`** (set by publish profile `MicrosoftStore-Win64`):

| Behavior | Sideload (GitHub / caveaipro.com) | Microsoft Store |
|----------|-----------------------------------|-----------------|
| Velopack bootstrap (`Program.cs`) | Yes | **Skipped** |
| In-app updates (`AppUpdateService`) | Velopack / GitHub | **Skipped** (Store updates) |
| `InstallationGuard` | Registry / Velopack / MSI | **Also allows** `WindowsApps` / `ProgramData\Packages` paths |

## Version

Application version is centralized in `Directory.Build.props` (`<Version>`). Current release: **1.4.0** (see `CHANGELOG.md`).

## Obfuscation (required before packaging)

Release builds run **[Obfuscar](https://github.com/obfuscar/obfuscar)** via the `MSBuild.Obfuscar` package and `src/CaveAiProForWindows/build/Obfuscation.targets`:

- Runs on **`dotnet build -c Release`** (before publish / MSIX pack).
- Renames implementation code, hides strings, and skips WPF/XAML/JSON surfaces (`Obfuscar.Template.xml`).
- **Subscription / Firebase entitlement logic** (`Services.Auth`, etc.) is obfuscated; only stable JSON models and MVVM binding types are skipped.

Reproduce obfuscation only:

```powershell
dotnet build src\CaveAiProForWindows\CaveAiProForWindows.csproj -c Release
```

Log: `src\CaveAiProForWindows\bin\Release\net8.0-windows\obfuscar.log` (or under `win-x64\` when RID is set).

Disable for local debugging: `-p:ObfuscatorEnabled=false`.

## Store MSIX pipeline (unsigned)

One command — build, obfuscate, publish Store profile, pack MSIX **without signing**:

```powershell
.\tools\package-store-msix.ps1
```

### What the script does

1. Inject Firebase client config (`CAVEAIPRO_FIREBASE_API_KEY` required)
2. `dotnet restore` solution
3. **Full** `dotnet publish -p:PublishProfile=MicrosoftStore-Win64` — Obfuscar, **self-contained** folder layout (`includedFrameworks` in `runtimeconfig.json`), `STORE_DISTRIBUTION`, no Velopack
4. `verify-store-publish.ps1` — fails the build if publish output is framework-dependent (would show the Windows “.NET install” dialog on clean machines)
5. `makeappx pack` — unsigned MSIX from `store/Package.appxmanifest` + publish output

**Important:** Do **not** use `dotnet publish --no-build` for Store MSIX. A prior framework-dependent build plus `--no-build` leaves `runtimeconfig.json` with `"frameworks"` instead of `"includedFrameworks"`, which fails Store certification (10.1.2.10).

### Output

Default folder: `_store_out\`

Example: `_store_out\CaveAiProForWindows-1.4.0-Store-unsigned.msix`

### Manual steps (equivalent)

```powershell
dotnet restore CaveAiProForWindows.sln
dotnet publish src\CaveAiProForWindows\CaveAiProForWindows.csproj -c Release -p:PublishProfile=MicrosoftStore-Win64
.\tools\verify-store-publish.ps1 -PublishDir src\CaveAiProForWindows\bin\Release\net8.0-windows\publish\microsoft-store\win-x64
# Then run package-store-msix.ps1 for inject + pack, or use the full script end-to-end.
```

Publish output (unpacked): `src\CaveAiProForWindows\bin\Release\net8.0-windows\publish\microsoft-store\win-x64\`

### Sideload (non-Store) — existing pipeline

```powershell
.\tools\package-release.ps1 -Tag v1.4.0
```

## MSIX tooling

`package-store-msix.ps1` locates `makeappx.exe` from:

1. **Windows SDK** — `Program Files (x86)\Windows Kits\10\bin\<version>\x64\makeappx.exe`
2. **NuGet fallback** — `store/msix-tool.csproj` restores `Microsoft.Windows.SDK.BuildTools`

If both are missing, install the [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) (Desktop C++ workload) or run `dotnet restore store/msix-tool.csproj`.

## Signing

- **Do not** sign the submission `.msix` with a local or third-party certificate.
- Upload the unsigned package to Partner Center; Microsoft re-signs for the Store.

Sideload Authenticode signing is documented in [CODE-SIGNING.md](CODE-SIGNING.md) (not used for Store submission).

## Partner Center — before upload

**Copy exact values from Partner Center → Product management → your app → Product identity.** Do not guess or derive them from the app display name or project folder. Partner Center validation fails if **Name**, **Publisher**, or **PublisherDisplayName** differ by even one character.

Update `store/Package.appxmanifest` **Identity** and **Properties** (or pass parameters to the script) so they match that page **exactly**:

- **Name** — package identity (e.g. `GeorgiosKourentzis.CaveAIPro`; not the executable or project name)
- **Publisher** — publisher ID from Product identity (e.g. `CN=54966508-95FA-45A0-B2A2-D1AF31D44DC4`; not the human-readable publisher name)
- **PublisherDisplayName** — must match Partner Center exactly (e.g. `GeorgiosKourentzis` with no spaces; a mismatch such as `Georgios Kourentzis` causes validation **ERROR** on upload)
- **Package family name (PFN)** — derived by Partner Center from **Name** + **Publisher** (e.g. `GeorgiosKourentzis.CaveAIPro_wvp8e4sf8kjk6`); you do not set it in the manifest, but it must match after upload
- **Version** — stamped automatically from `Directory.Build.props` (four-part, e.g. `1.4.0.0`)

Optional script overrides (use the same values as Product identity):

```powershell
.\tools\package-store-msix.ps1 -IdentityName 'GeorgiosKourentzis.CaveAIPro' -Publisher 'CN=54966508-95FA-45A0-B2A2-D1AF31D44DC4'
```

### Listing checklist

- [ ] Package identity / PFN aligned with manifest
- [ ] Store listing — description, screenshots (1920×1080), privacy policy URL
- [ ] Age rating (IARC)
- [ ] Capabilities — `internetClient`, `runFullTrust` (full-trust desktop)
- [ ] **runFullTrust approval** — declare the restricted capability in Partner Center before submission: **Product management → your app → App capabilities** (or **App setup → Capabilities**), add **Run full trust**, and submit for Microsoft review if prompted. The manifest includes `<rescap:Capability Name="runFullTrust" />`; Partner Center shows a **WARNING** until this is declared and approved.
- [ ] WebView2 — note runtime dependency in certification notes (Evergreen WebView2; not a separate user install for most Windows 11 PCs)
- [ ] **.NET runtime** — Store MSIX must be **self-contained** (bundled runtime). Verify with `verify-store-publish.ps1` before upload. No separate .NET Desktop Runtime install is required when `includedFrameworks` is present in publish output.
- [ ] Firebase / OAuth — Store redirect URIs if required for desktop auth
- [ ] Certification — requires Google account + active CaveAI Pro (Play) subscription

### Certification test account

Microsoft testers must pass the Google sign-in gate and subscription check. **Copy-paste ready notes**, Firestore provisioning steps, and troubleshooting: **[MICROSOFT-STORE-CERTIFICATION-NOTES.md](MICROSOFT-STORE-CERTIFICATION-NOTES.md)**.

Summary:

- Create a **dedicated Google test account** and pre-provision `user_entitlements/{uid}` in Firestore (`PLAY_SUBSCRIPTION`, `status=ACTIVE`, future `premiumCloudUntil`), **or** sign in on Android as a Play license tester first.
- In Partner Center → **Notes for certification**, paste the full block from that doc (email, password, test steps).
- "Subscription required" after sign-in means **auth succeeded** but the account has no entitlement — not a broken sign-in.

Without valid credentials, certification fails at login even when the MSIX is otherwise correct.

### Certification resubmit (Product ID `9PPF3HPZRL21`)

1. Complete the **developer checklist** in [MICROSOFT-STORE-CERTIFICATION-NOTES.md](MICROSOFT-STORE-CERTIFICATION-NOTES.md) (test account + Firestore entitlement + verify on Windows).
2. Run `.\tools\package-store-msix.ps1` (with `CAVEAIPRO_FIREBASE_API_KEY` set).
3. Confirm `verify-store-publish.ps1` passes and MSIX size is ~200+ MB (bundled runtime).
4. Upload the new unsigned `.msix` from `_store_out\` to Partner Center → **Packages**.
5. Paste **Notes for certification** from [MICROSOFT-STORE-CERTIFICATION-NOTES.md](MICROSOFT-STORE-CERTIFICATION-NOTES.md) (replace `[FILL IN]` placeholders).

## Install guard

Release builds from a loose folder (e.g. `Downloads`, `dist`) are blocked. Valid locations:

1. **Microsoft Store** — under `\WindowsApps\` or `\ProgramData\Packages\`
2. **Velopack** — `%LocalAppData%\CaveAiProForWindows\`
3. **MSI / script** — registry `HKLM` or `HKCU` `SOFTWARE\CaveAiPro\CaveAiProForWindows`

Debug builds skip the guard.

## Related docs

- [INSTALL.md](INSTALL.md) — end-user install paths
- [CODE-SIGNING.md](CODE-SIGNING.md) — sideload signing
- `legal/00-DISTRIBUTION-PLATFORMS.md` — product family wording
