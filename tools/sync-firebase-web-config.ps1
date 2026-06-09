# Sync Firebase **Browser/Web** API key into website firebase.js and Windows inject.
# Source: tools/local/firebase-web-config.json (NOT google-services.json).
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$WebConfigPath = '',
    [string]$WebsiteFirebaseJs = 'D:\CaveAIpro website\src\firebase.js',
    [switch]$InjectWindows,
    [switch]$BuildWebsite,
    [switch]$DeployWebsite,
    [switch]$BuildStoreMsix
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($WebConfigPath)) {
    $WebConfigPath = Join-Path $RepoRoot 'tools\local\firebase-web-config.json'
}

if (-not (Test-Path -LiteralPath $WebConfigPath)) {
    throw @"
Missing $WebConfigPath
Copy tools/local/firebase-web-config.json.example and paste the Browser apiKey from
Firebase Console -> Project settings -> Your apps -> Web app (SDK config).
"@
}

$obj = Get-Content -LiteralPath $WebConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$newKey = $obj.apiKey
if ([string]::IsNullOrWhiteSpace($newKey) -or $newKey -match 'PASTE_|REPLACE') {
    throw 'firebase-web-config.json has no usable Browser apiKey.'
}

$suffix = $newKey.Substring($newKey.Length - 6)
Write-Host "Browser key source: $WebConfigPath (suffix ...$suffix)"

if (Test-Path -LiteralPath $WebsiteFirebaseJs) {
    $js = Get-Content -LiteralPath $WebsiteFirebaseJs -Raw -Encoding UTF8
    if ($js -notmatch "apiKey:\s*'") {
        throw "Could not find apiKey line in $WebsiteFirebaseJs"
    }
    $updated = $js -replace "(apiKey:\s*')[^']+(')", "`${1}$newKey`${2}"
    if ($updated -eq $js) {
        Write-Host "Website firebase.js already uses suffix ...$suffix"
    } else {
        Set-Content -LiteralPath $WebsiteFirebaseJs -Value $updated -Encoding UTF8 -NoNewline
        Write-Host "Updated website: $WebsiteFirebaseJs"
    }
} else {
    Write-Warning "Website firebase.js not found: $WebsiteFirebaseJs"
}

if ($InjectWindows) {
    $inject = Join-Path $RepoRoot 'tools\inject-firebase-config.ps1'
    & $inject -RepoRoot $RepoRoot -WebConfigPath $WebConfigPath
    if ($LASTEXITCODE -ne 0) { throw 'inject-firebase-config.ps1 failed.' }
}

if ($BuildWebsite) {
    Push-Location (Split-Path -Parent $WebsiteFirebaseJs)
    try {
        npm run build:prod
        if ($LASTEXITCODE -ne 0) { throw 'npm run build:prod failed.' }
    } finally {
        Pop-Location
    }
}

if ($DeployWebsite) {
    Push-Location (Split-Path -Parent $WebsiteFirebaseJs)
    try {
        npx --yes firebase-tools@latest deploy --only hosting
        if ($LASTEXITCODE -ne 0) { throw 'firebase deploy --only hosting failed.' }
    } finally {
        Pop-Location
    }
}

if ($BuildStoreMsix) {
    $pack = Join-Path $RepoRoot 'tools\package-store-msix.ps1'
    & $pack -RepoRoot $RepoRoot
    if ($LASTEXITCODE -ne 0) { throw 'package-store-msix.ps1 failed.' }
}

Write-Host 'sync-firebase-web-config: done.'
