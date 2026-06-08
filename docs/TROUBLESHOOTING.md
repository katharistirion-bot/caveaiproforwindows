# Troubleshooting (Windows)

## Diagnostic logs

Open **Help → Open diagnostic folder…** or browse:

`%LOCALAPPDATA%\CaveAiProForWindows\`

| File | When it is written |
|------|---------------------|
| `startup.log` | Each launch — auth, updates, WebView bridge |
| `last-error.txt` | Unhandled crash or fatal startup error |
| `legal_terms_acceptance.json` | When you accept the EULA |
| `auth-token.dat` | After Google sign-in (DPAPI-encrypted) |

When reporting a bug, attach `startup.log` and `last-error.txt` if present. Do **not** share `auth-token.dat`.

## Common issues

### “Not running from a registered installation”

Install via **Setup.exe** or **MSI** from GitHub Releases, or use a Debug build for development.

### App closes at login

You need an active **CaveAI Pro** subscription or trial on the same Google account. Purchase or trial on Android (Google Play), then sign in again on Windows.

### Updates not offered

Install with **Velopack Setup.exe**, not MSI or ZIP. MSI users must download new releases manually.

### SmartScreen warning

The publisher certificate may be new. Prefer downloads from the official GitHub Releases page. Code signing requires `WINDOWS_CERT_*` secrets in the release pipeline — see [CODE-SIGNING.md](CODE-SIGNING.md).

## Support contact

caveaipro@gmail.com
