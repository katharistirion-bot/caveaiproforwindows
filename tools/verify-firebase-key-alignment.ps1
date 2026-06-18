# Reports Firebase API key suffix alignment. Android and Browser keys are allowed to differ.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$AndroidGoogleServices = 'D:\caveaipro\app\google-services.json',
    [string]$WebConfigPath = '',
    [string]$WebsiteFirebaseJs = 'D:\CaveAIpro website\src\firebase.js',
    [string]$StoreMsix = ''
)

$ErrorActionPreference = 'Stop'

function Get-KeySuffix {
    param([string]$Key)
    if ([string]::IsNullOrWhiteSpace($Key)) { return '<missing>' }
    if ($Key -match 'REPLACE|PASTE_') { return 'PLACEHOLDER' }
    return '...' + $Key.Substring($Key.Length - 6)
}

function Read-GoogleServicesSuffix { param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '<file-missing>' }
    $doc = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    return Get-KeySuffix $doc.client[0].api_key[0].current_key
}

function Read-WebConfigSuffix { param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '<file-missing>' }
    $obj = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    return Get-KeySuffix $obj.apiKey
}

function Read-FirebaseJsSuffix { param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '<file-missing>' }
    $text = Get-Content -LiteralPath $Path -Raw
    if ($text -match "apiKey:\s*'([^']+)'") { return Get-KeySuffix $Matches[1] }
    return '<parse-failed>'
}

function Read-InitJsonSuffix { param([string]$HostName)
    try {
        $init = Invoke-RestMethod -Uri "https://$HostName/__/firebase/init.json" -TimeoutSec 15
        return Get-KeySuffix $init.apiKey
    } catch { return '<fetch-failed>' }
}

function Read-MsixSuffix { param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '<file-missing>' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -like '*Assets/DesktopAuth/firebase-config.json' } | Select-Object -First 1
        if (-not $entry) { return '<no-config-in-msix>' }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { return Get-KeySuffix (($reader.ReadToEnd() | ConvertFrom-Json).apiKey) }
        finally { $reader.Close() }
    } finally { $zip.Dispose() }
}

if ([string]::IsNullOrWhiteSpace($WebConfigPath)) {
    $WebConfigPath = Join-Path $RepoRoot 'tools\local\firebase-web-config.json'
}
if ([string]::IsNullOrWhiteSpace($StoreMsix)) {
    $ver = '1.4.2'
    $props = Join-Path $RepoRoot 'Directory.Build.props'
    if (Test-Path $props) {
        [xml]$xml = Get-Content $props
        if ($xml.Project.PropertyGroup.Version) { $ver = $xml.Project.PropertyGroup.Version }
    }
    $StoreMsix = Join-Path $RepoRoot "_store_out\CaveAiProForWindows-$ver-Store-unsigned.msix"
}

$browserSuffix = Read-WebConfigSuffix $WebConfigPath
if ($browserSuffix -match '^<|PLACEHOLDER') {
    $browserSuffix = Read-FirebaseJsSuffix $WebsiteFirebaseJs
}

$rows = @(
    [pscustomobject]@{ Source = 'Android google-services.json'; Suffix = (Read-GoogleServicesSuffix $AndroidGoogleServices); Group = 'android' }
    [pscustomobject]@{ Source = 'Browser firebase-web-config.json'; Suffix = $browserSuffix; Group = 'browser' }
    [pscustomobject]@{ Source = 'Website src/firebase.js'; Suffix = (Read-FirebaseJsSuffix $WebsiteFirebaseJs); Group = 'browser' }
    [pscustomobject]@{ Source = 'Live init.json (www.caveaipro.com)'; Suffix = (Read-InitJsonSuffix 'www.caveaipro.com'); Group = 'browser' }
    [pscustomobject]@{ Source = 'Store MSIX firebase-config'; Suffix = (Read-MsixSuffix $StoreMsix); Group = 'browser' }
)
$rows | Format-Table -AutoSize

$browserRows = $rows | Where-Object { $_.Group -eq 'browser' -and $_.Suffix -notmatch '^<' }
$initRow = $browserRows | Where-Object { $_.Source -like '*init.json*' } | Select-Object -First 1
$alignRows = $browserRows | Where-Object { $_.Source -notlike '*init.json*' }
$expected = ($alignRows | Where-Object { $_.Source -like '*firebase-web-config*' } | Select-Object -First 1).Suffix
if ($expected -match '^<|PLACEHOLDER') {
    $expected = ($alignRows | Where-Object { $_.Source -like '*firebase.js*' } | Select-Object -First 1).Suffix
}

$bad = @()
if ($expected -match '^<|PLACEHOLDER') {
    Write-Host 'MISSING: tools/local/firebase-web-config.json with Browser apiKey'
    $bad += 'browser-config-missing'
} else {
    foreach ($r in $alignRows) {
        if ($r.Suffix -ne $expected) { $bad += $r.Source }
    }
}

if ($initRow -and $expected -notmatch '^<|PLACEHOLDER' -and $initRow.Suffix -ne $expected) {
    Write-Warning "Live init.json suffix $($initRow.Suffix) differs from browser stack ($expected). Hosting metadata only - website/MSIX use explicit config. Safe to ignore unless Windows dev builds rely on FirebaseHostingConfigFetcher."
}

if ($bad.Count -eq 0) {
    Write-Host "OK: Browser stack aligned (suffix $expected). Android may differ - that is normal."
    exit 0
}

Write-Host "Browser stack mismatch (expected suffix $expected):"
foreach ($item in $bad) { Write-Host ('  - ' + $item) }
Write-Host ''
Write-Host 'init.json is Firebase Hosting metadata; stale keys do not affect website (firebase.js) or Store MSIX.'
exit 1
