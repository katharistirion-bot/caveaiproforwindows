# Restores git-tracked firebase-config.json to REPLACE_AT_BUILD (never commit real Browser keys).
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$path = Join-Path $RepoRoot 'src\CaveAiProForWindows\Assets\DesktopAuth\firebase-config.json'

$placeholder = @'
{
  "apiKey": "REPLACE_AT_BUILD",
  "authDomain": "caveaipro-5950e.firebaseapp.com",
  "projectId": "caveaipro-5950e",
  "storageBucket": "caveaipro-5950e.firebasestorage.app"
}
'@.TrimEnd()

if (-not (Test-Path -LiteralPath $path)) {
    if (-not $Quiet) { Write-Warning "restore-firebase-config-placeholder: missing $path" }
    exit 0
}

$current = Get-Content -LiteralPath $path -Raw -Encoding UTF8
if ($current -match 'REPLACE_AT_BUILD') {
    if (-not $Quiet) { Write-Host 'restore-firebase-config-placeholder: already placeholder (OK for git).' }
    exit 0
}

Set-Content -LiteralPath $path -Value $placeholder -Encoding UTF8
if (-not $Quiet) {
    Write-Host 'restore-firebase-config-placeholder: reset source to REPLACE_AT_BUILD (real key stays in MSIX/publish output only).'
}
exit 0
