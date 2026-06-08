# Prepare website upload bundle for caveaipro.com
param(
    [string]$Tag = 'v1.3.0',
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-Location $RepoRoot

$version = $Tag -replace '^v', ''
Write-Host "=== CAVE AI PRO Windows - website release prep ($Tag) ===" -ForegroundColor Cyan

Write-Host 'Running unit tests...'
dotnet test (Join-Path $RepoRoot 'tests/CaveAiProForWindows.Tests/CaveAiProForWindows.Tests.csproj') -c Debug --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }

Write-Host 'Publishing Release single-file win-x64...'
dotnet publish (Join-Path $RepoRoot 'src/CaveAiProForWindows/CaveAiProForWindows.csproj') `
    -c Release `
    -r win-x64 `
    -p:PublishProfile=ReleaseSingleFile-Win64 `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$pubExe = Join-Path $RepoRoot 'src/CaveAiProForWindows/bin/Release/net8.0-windows/publish/win-x64/CaveAiProForWindows.exe'
if (-not (Test-Path -LiteralPath $pubExe)) { throw "Missing published exe: $pubExe" }

Write-Host 'Packaging ZIP, MSI, Velopack...'
& (Join-Path $RepoRoot 'tools/package-release.ps1') -Tag $Tag
if ($LASTEXITCODE -ne 0) { throw 'package-release.ps1 failed.' }

$releaseDir = Join-Path $RepoRoot '_release_out'
$uploadDir = Join-Path $RepoRoot '_website_upload'
if (Test-Path $uploadDir) { Remove-Item $uploadDir -Recurse -Force }
New-Item -ItemType Directory -Path $uploadDir | Out-Null

$setupExe = Get-ChildItem -LiteralPath $releaseDir -Filter "CaveAiProForWindows-$Tag-Setup.exe" | Select-Object -First 1
$setupMsi = Get-ChildItem -LiteralPath $releaseDir -Filter "CaveAiProForWindows-$Tag-Setup.msi" | Select-Object -First 1
$portableZip = Get-ChildItem -LiteralPath $releaseDir -Filter "CaveAiProForWindows-$Tag-win-x64.zip" | Select-Object -First 1

if (-not $setupExe) { throw 'Velopack Setup.exe not found in _release_out.' }

Copy-Item -LiteralPath $setupExe.FullName -Destination (Join-Path $uploadDir 'CaveAiProForWindows-Setup.exe') -Force
if ($setupMsi) {
    Copy-Item -LiteralPath $setupMsi.FullName -Destination (Join-Path $uploadDir 'CaveAiProForWindows-Setup.msi') -Force
}
if ($portableZip) {
    Copy-Item -LiteralPath $portableZip.FullName -Destination (Join-Path $uploadDir 'CaveAiProForWindows-portable.zip') -Force
}

$velopackDir = Join-Path $releaseDir 'velopack'
if (Test-Path -LiteralPath $velopackDir) {
    Copy-Item -LiteralPath $velopackDir -Destination (Join-Path $uploadDir 'updates') -Recurse -Force
}

$readmePath = Join-Path $uploadDir 'UPLOAD-README.txt'
@(
    "CAVE AI PRO for Windows - release $version"
    '=========================================='
    ''
    'Upload to www.caveaipro.com (Windows download page):'
    ''
    'PRIMARY (recommended, automatic updates when installed via this file):'
    '  CaveAiProForWindows-Setup.exe'
    ''
    'OPTIONAL:'
    '  CaveAiProForWindows-Setup.msi'
    '  CaveAiProForWindows-portable.zip'
    ''
    'AUTO-UPDATE: app checks GitHub Releases by default.'
    'Also push git tag v1.3.0 for the update feed, or mirror updates/ on your CDN.'
    ''
    'REQUIREMENTS: Windows 10/11 x64, CaveAI Pro Google account + subscription.'
    ''
    'SHA256:'
) | Set-Content -Path $readmePath -Encoding utf8

$sumsPath = Join-Path $uploadDir 'SHA256SUMS.txt'
Get-ChildItem -LiteralPath $uploadDir -File | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $($_.Name)"
    Add-Content -Path $readmePath -Value "  $line"
    Add-Content -Path $sumsPath -Value $line
}

$htmlLines = @(
    '<!-- caveaipro.com - Windows download block -->'
    '<section id="download-windows">'
    '  <h2>CAVE AI PRO for Windows</h2>'
    '  <p>Desktop survey workstation for Windows 10/11 (64-bit). Same Google account and subscription as CaveAI Pro (Android).</p>'
    '  <p><a href="/downloads/CaveAiProForWindows-Setup.exe" download>Download for Windows (recommended)</a></p>'
    '  <p><small>MSI: <a href="/downloads/CaveAiProForWindows-Setup.msi">CaveAiProForWindows-Setup.msi</a></small></p>'
    "  <p><small>Version $version</small></p>"
    '</section>'
)
$htmlLines | Set-Content -Path (Join-Path $uploadDir 'website-snippet.html') -Encoding utf8

$buildOut = Join-Path $RepoRoot '_build_out'
New-Item -ItemType Directory -Path $buildOut -Force | Out-Null
Copy-Item -LiteralPath $pubExe -Destination (Join-Path $buildOut 'CaveAiProForWindows.exe') -Force

Write-Host ''
Write-Host 'OK - website bundle ready:' -ForegroundColor Green
Write-Host "  $uploadDir"
Get-ChildItem -LiteralPath $uploadDir -Recurse -File | ForEach-Object {
    Write-Host ('  ' + $_.FullName.Replace($RepoRoot + '\', ''))
}
