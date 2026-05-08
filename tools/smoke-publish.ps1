# Publishes the Windows app (ReleaseSingleFile-Win64) and asserts the exe + publish-manifest exist.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

dotnet publish (Join-Path $repoRoot 'src/CaveAiProForWindows/CaveAiProForWindows.csproj') `
    -c Release `
    -r win-x64 `
    -p:PublishProfile=ReleaseSingleFile-Win64

$pubDir = Join-Path $repoRoot 'src/CaveAiProForWindows/bin/Release/net8.0-windows/publish/win-x64'
$exe = Join-Path $pubDir 'CaveAiProForWindows.exe'
$manifest = Join-Path $pubDir 'publish-manifest.json'

if (-not (Test-Path $exe)) { throw "Missing published exe: $exe" }
if (-not (Test-Path $manifest)) { throw "Missing publish-manifest.json: $manifest" }

Write-Host "OK: $exe"
Write-Host "OK: $manifest"
