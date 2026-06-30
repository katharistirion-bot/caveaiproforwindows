# Writes Assets/DesktopAuth/firebase-config.json for Windows WebView auth.
# Uses the Firebase **Browser/Web** API key — NOT the Android google-services key.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ApiKey = $env:CAVEAIPRO_FIREBASE_API_KEY,
    [string]$WebConfigPath = '',
    [string]$ProjectId = 'caveaipro-5950e',
    [string]$AuthDomain = 'caveaipro-5950e.firebaseapp.com',
    [string]$StorageBucket = 'caveaipro-5950e.firebasestorage.app',
    [switch]$AllowPlaceholder
)

$ErrorActionPreference = 'Stop'
$path = Join-Path $RepoRoot 'src\CaveAiProForWindows\Assets\DesktopAuth\firebase-config.json'

function Read-WebConfigJson {
    param([string]$JsonPath)
    if (-not (Test-Path -LiteralPath $JsonPath)) {
        throw "firebase-web-config.json not found: $JsonPath"
    }
    $obj = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $key = $obj.apiKey
    if ([string]::IsNullOrWhiteSpace($key) -or $key -match 'PASTE_|REPLACE') {
        throw "firebase-web-config.json has no usable apiKey at $JsonPath"
    }
    return @{
        ApiKey        = $key.Trim()
        ProjectId     = if ($obj.projectId) { $obj.projectId.Trim() } else { $ProjectId }
        StorageBucket = if ($obj.storageBucket) { $obj.storageBucket.Trim() } else { $StorageBucket }
        AuthDomain    = if ($obj.authDomain) { $obj.authDomain.Trim() } else { $AuthDomain }
    }
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    if ([string]::IsNullOrWhiteSpace($WebConfigPath)) {
        $WebConfigPath = Join-Path $RepoRoot 'tools\local\firebase-web-config.json'
    }
    if (Test-Path -LiteralPath $WebConfigPath) {
        $fromWeb = Read-WebConfigJson -JsonPath $WebConfigPath
        $ApiKey = $fromWeb.ApiKey
        $ProjectId = $fromWeb.ProjectId
        $StorageBucket = $fromWeb.StorageBucket
        $AuthDomain = $fromWeb.AuthDomain
        Write-Host "inject-firebase-config: read Browser API key from $WebConfigPath"
    }
}

# CI/local: reuse config injected earlier in the same job (smoke-publish calls inject again).
if ([string]::IsNullOrWhiteSpace($ApiKey) -and (Test-Path -LiteralPath $path)) {
    try {
        $existing = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        $existingKey = [string]$existing.apiKey
        if (-not [string]::IsNullOrWhiteSpace($existingKey) -and $existingKey -notmatch 'REPLACE|PASTE_') {
            $ApiKey = $existingKey.Trim()
            if ($existing.projectId) { $ProjectId = [string]$existing.projectId.Trim() }
            if ($existing.storageBucket) { $StorageBucket = [string]$existing.storageBucket.Trim() }
            if ($existing.authDomain) { $AuthDomain = [string]$existing.authDomain.Trim() }
            Write-Host "inject-firebase-config: reusing existing config at $path"
        }
    } catch {
        Write-Verbose "inject-firebase-config: could not read existing config at $path"
    }
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    if ($AllowPlaceholder) {
        Write-Host 'inject-firebase-config: no API key — leaving REPLACE_AT_BUILD placeholder.'
        exit 0
    }
    throw @'
No Firebase Browser API key found for Windows/WebView auth.
  • Create a Browser API key in Google Cloud (HTTP referrers for caveaipro.com + localhost).
  • Firebase Console → Project settings → Web app → copy apiKey into:
      tools/local/firebase-web-config.json
  • Or set CAVEAIPRO_FIREBASE_API_KEY / pass -ApiKey
Do NOT use Android google-services.json current_key for Windows or website — it is Android-restricted.
'@
}

if ($ApiKey -match 'REPLACE|PASTE_') {
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

$verifyScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\verify-firebase-config.ps1'
if (Test-Path -LiteralPath $verifyScript) {
    & $verifyScript -ConfigPath $path
    if ($LASTEXITCODE -ne 0) { throw 'verify-firebase-config.ps1 failed after inject.' }
}

exit 0
