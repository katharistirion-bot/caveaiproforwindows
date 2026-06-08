# Installing CAVE AI PRO for Windows

> **Public release:** Windows desktop companion is available on [caveaipro.com](https://www.caveaipro.com/#windows-download) (Download for Windows). **Microsoft Store** listing is planned — see [MICROSOFT-STORE.md](MICROSOFT-STORE.md). GitHub Releases and local builds below are for developers and advanced users.

## Distribution channels

| Channel | Audience | Auto-update |
|---------|----------|-------------|
| **Microsoft Store** (planned) | Most Windows users | Yes (via Store) |
| `*-Setup.exe` (**Velopack**) | Sideload from caveaipro.com / GitHub | Yes (in-app) |
| `*-Setup.msi` | IT / per-machine install | No — manual reinstall |
| `*-win-x64.zip` | Portable / developers | No |

### Sideload (current primary channel)

Install from [caveaipro.com](https://www.caveaipro.com/#windows-download) or [GitHub Releases](https://github.com/katharistirion-bot/caveaiproforwindows/releases) using:

`CaveAiProForWindows-{version}-Setup.exe` (**Velopack**)

This channel supports **automatic in-app updates** (Help → Check for updates).

### Microsoft Store (in preparation)

Store builds are compiled with `STORE_DISTRIBUTION`: Velopack and in-app GitHub update checks are disabled; updates come from the Store. See [MICROSOFT-STORE.md](MICROSOFT-STORE.md) for the packaging checklist.

After installation (any channel), start **CAVE AI PRO** from the Start menu. Sign in with the same Google account as **CaveAI Pro (Android)**; an active subscription or trial is required.

## Developers

- **Debug from source:** `DevRun.bat` or `dotnet run` (Debug skips install guard).
- **Try Release build locally:** `TryApp.bat` (registers a dev install path) or install the Velopack Setup.exe.
- **Build MSI (WiX):** `BuildInstaller.bat` — for enterprise packaging, not the primary user channel.
- **Build Store payload:** `dotnet publish src\CaveAiProForWindows\CaveAiProForWindows.csproj -c Release -p:PublishProfile=MicrosoftStore-Win64`
- **Build sideload release assets:** publish (above profile or `ReleaseSingleFile-Win64`), then `.\tools\package-release.ps1 -Tag v1.3.0` — injects Firebase config from `CAVEAIPRO_FIREBASE_API_KEY` when set (see [SECURITY.md](SECURITY.md)).

See also: [CODE-SIGNING.md](CODE-SIGNING.md), [TROUBLESHOOTING.md](TROUBLESHOOTING.md), [MICROSOFT-STORE.md](MICROSOFT-STORE.md).
