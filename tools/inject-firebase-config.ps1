# Writes Assets/DesktopAuth/firebase-config.json from CAVEAIPRO_FIREBASE_API_KEY (or parameter).
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ApiKey = $env:CAVEAIPRO_FIREBASE_API_KEY,
    [string]$ProjectId = 'caveaipro-5950e',
    [string]$AuthDomain = 'caveaipro-5950e.firebaseapp.com',
    [string]$StorageBucket = 'caveaipro-5950e.firebasestorage.app',
    [switch]$AllowPlaceholder
)

$ErrorActionPreference = 'Stop'
$path = Join-Path $RepoRoot 'src\CaveAiProForWindows\Assets\DesktopAuth\firebase-config.json'

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    if ($AllowPlaceholder) {
        Write-Host 'inject-firebase-config: no API key — leaving REPLACE_AT_BUILD placeholder.'
        exit 0
    }
    throw @'
CAVEAIPRO_FIREBASE_API_KEY is not set.
Set the Firebase Web API key (user or machine env) or pass -ApiKey.
For CI: GitHub secret CAVEAIPRO_FIREBASE_API_KEY (see .github/workflows/release.yml).
'@
}

if ($ApiKey -match 'REPLACE') {
    throw 'Refusing to inject a placeholder API key.'
}

$json = @{
    apiKey         = $ApiKey.Trim()
    authDomain     = $AuthDomain
    projectId      = $ProjectId
    storageBucket  = $StorageBucket
} | ConvertTo-Json -Compress

$dir = Split-Path -Parent $path
if (-not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

Set-Content -LiteralPath $path -Value $json -Encoding UTF8 -NoNewline
Write-Host "inject-firebase-config: wrote $path (project $ProjectId)"
exit 0
