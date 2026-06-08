# Fails when firebase-config.json is missing or still has REPLACE_AT_BUILD (release guard).
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ConfigPath = '',
    [switch]$AllowPlaceholder
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $RepoRoot 'src\CaveAiProForWindows\Assets\DesktopAuth\firebase-config.json'
}

if (-not (Test-Path -LiteralPath $ConfigPath)) {
    if ($AllowPlaceholder) {
        Write-Host "verify-firebase-config: missing $ConfigPath (allowed)."
        exit 0
    }
    throw "verify-firebase-config: missing $ConfigPath"
}

$json = Get-Content -LiteralPath $ConfigPath -Raw
if ($json -match 'REPLACE_AT_BUILD') {
    if ($AllowPlaceholder) {
        Write-Host "verify-firebase-config: placeholder present (allowed)."
        exit 0
    }
    throw @"
verify-firebase-config: placeholder apiKey in $ConfigPath
Run: `$env:CAVEAIPRO_FIREBASE_API_KEY = '<Firebase Web API key>'; .\tools\inject-firebase-config.ps1
"@
}

try {
    $obj = $json | ConvertFrom-Json
} catch {
    throw "verify-firebase-config: invalid JSON at $ConfigPath — $($_.Exception.Message)"
}

if ([string]::IsNullOrWhiteSpace($obj.apiKey)) {
    throw "verify-firebase-config: apiKey is empty in $ConfigPath"
}

Write-Host "verify-firebase-config: OK ($ConfigPath, project $($obj.projectId))"
exit 0
