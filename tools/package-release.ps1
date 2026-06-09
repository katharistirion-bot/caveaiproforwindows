# Packages Release publish output: portable ZIP, WiX MSI, and Velopack (Setup + delta nupkgs).
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-Location $RepoRoot

$injectScript = Join-Path $RepoRoot 'tools/inject-firebase-config.ps1'
$verifyScript = Join-Path $RepoRoot 'tools/verify-firebase-config.ps1'
Write-Host 'Injecting Firebase client config (build-time API key)…'
& $injectScript -RepoRoot $RepoRoot
if ($LASTEXITCODE -ne 0) { throw 'inject-firebase-config.ps1 failed.' }

$version = $Tag -replace '^v', ''
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Tag must not be empty.' }

$pubDir = Join-Path $RepoRoot 'src/CaveAiProForWindows/bin/Release/net8.0-windows/publish/win-x64'
$exe = Join-Path $pubDir 'CaveAiProForWindows.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing published exe: $exe" }

$configSrc = Join-Path $RepoRoot 'src/CaveAiProForWindows/Assets/DesktopAuth/firebase-config.json'
$configDest = Join-Path $pubDir 'Assets/DesktopAuth/firebase-config.json'
$configDestDir = Split-Path -Parent $configDest
if (-not (Test-Path -LiteralPath $configDestDir)) {
    New-Item -ItemType Directory -Force -Path $configDestDir | Out-Null
}
Copy-Item -LiteralPath $configSrc -Destination $configDest -Force
Write-Host "OK: synced firebase-config.json to publish output"
& $verifyScript -ConfigPath $configDest
if ($LASTEXITCODE -ne 0) { throw 'verify-firebase-config.ps1 failed on publish output.' }

$outDir = Join-Path $RepoRoot '_release_out'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

# --- Portable ZIP (single-file exe + export_assets tree) ---
if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    $zipStaging = Join-Path ([IO.Path]::GetTempPath()) 'release-zip-staging'
} else {
    $zipStaging = Join-Path $env:RUNNER_TEMP 'release-zip-staging'
}
if (Test-Path $zipStaging) { Remove-Item $zipStaging -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $zipStaging 'export_assets/windows') | Out-Null
Set-Content -Path (Join-Path $zipStaging 'export_assets/windows/.gitkeep') -Value '' -Encoding utf8
Copy-Item $exe $zipStaging

$zipName = "CaveAiProForWindows-$Tag-win-x64.zip"
$zipPath = Join-Path $outDir $zipName
Compress-Archive -Path (Join-Path $zipStaging '*') -DestinationPath $zipPath -Force
Write-Host "OK: $zipPath"

# --- WiX MSI (folder publish for registered install / InstallationGuard) ---
Write-Host 'Building MSI installer…'
$installerVersion = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }
dotnet build (Join-Path $RepoRoot 'installer/CaveAiProForWindows.Installer.wixproj') -c Release -p:CaveInstallerProductVersion=$installerVersion
if ($LASTEXITCODE -ne 0) { throw 'WiX MSI build failed.' }

$msiSrc = Join-Path $RepoRoot '_build_out/CaveAiProForWindows-Setup.msi'
if (-not (Test-Path -LiteralPath $msiSrc)) {
    $msiSrc = Join-Path $RepoRoot 'installer/bin/Release/CaveAiProForWindows-Setup.msi'
}
if (-not (Test-Path -LiteralPath $msiSrc)) { throw "MSI not found after WiX build." }

$msiName = "CaveAiProForWindows-$Tag-Setup.msi"
$msiPath = Join-Path $outDir $msiName
Copy-Item -LiteralPath $msiSrc -Destination $msiPath -Force
Write-Host "OK: $msiPath"

# --- Velopack Setup.exe + delta packages (auto-update channel) ---
Write-Host 'Building Velopack release…'
$vpkWork = Join-Path $outDir 'velopack-work'
New-Item -ItemType Directory -Path $vpkWork -Force | Out-Null

dotnet tool install --global vpk 2>$null
if ($LASTEXITCODE -ne 0) {
    dotnet tool update --global vpk
}

$vpkCmd = Get-Command vpk -ErrorAction SilentlyContinue
$vpkExe = if ($vpkCmd) { $vpkCmd.Source } else { $null }
if (-not $vpkExe) { throw 'Velopack CLI (vpk) not available after dotnet tool install.' }

& $vpkExe pack `
    -u CaveAiProForWindows `
    -v $version `
    -p $pubDir `
    -e CaveAiProForWindows.exe `
    -o $vpkWork `
    --packTitle 'CAVE AI PRO' `
    --packAuthors 'CAVE AI PRO'

if ($LASTEXITCODE -ne 0) { throw 'Velopack pack failed.' }

$setupExe = Get-ChildItem -LiteralPath $vpkWork -Filter '*Setup*.exe' | Select-Object -First 1
if (-not $setupExe) { throw 'Velopack Setup.exe not found in output.' }

$setupName = "CaveAiProForWindows-$Tag-Setup.exe"
$setupPath = Join-Path $outDir $setupName
Copy-Item -LiteralPath $setupExe.FullName -Destination $setupPath -Force
Write-Host "OK: $setupPath"

# Copy Velopack delta assets (required for GithubSource auto-update)
$vpkReleaseDir = Join-Path $outDir 'velopack'
New-Item -ItemType Directory -Path $vpkReleaseDir -Force | Out-Null
Get-ChildItem -LiteralPath $vpkWork -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $vpkReleaseDir $_.Name) -Force
    Write-Host "OK: velopack/$($_.Name)"
}

if ($env:GITHUB_OUTPUT) {
    "zip_path=$zipPath" >> $env:GITHUB_OUTPUT
    "msi_path=$msiPath" >> $env:GITHUB_OUTPUT
    "setup_path=$setupPath" >> $env:GITHUB_OUTPUT
}

Write-Host 'Release packaging completed.'
