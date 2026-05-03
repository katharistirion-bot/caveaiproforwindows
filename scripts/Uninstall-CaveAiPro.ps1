#Requires -Version 5.1
param(
    [string]$InstallRoot = ''
)
# Removes per-user install (expects to live in the install folder, or pass -InstallRoot).
$ErrorActionPreference = 'Stop'

if (-not $InstallRoot) {
    $InstallRoot = $PSScriptRoot
}

$programsFolder = [Environment]::GetFolderPath('Programs')
$shortcutPath = Join-Path $programsFolder 'CAVE AI PRO.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

$regKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CaveAiProForWindows'
if (Test-Path -LiteralPath $regKey) {
    Remove-Item -LiteralPath $regKey -Recurse -Force
}

$appMarker = 'HKCU:\Software\CaveAiPro\CaveAiProForWindows'
if (Test-Path -LiteralPath $appMarker) {
    Remove-Item -LiteralPath $appMarker -Recurse -Force
}

$marker = Join-Path $InstallRoot 'Uninstall-CaveAiPro.ps1'
if (-not (Test-Path -LiteralPath $marker)) {
    Write-Warning "Folder does not look like a CAVE AI PRO install: $InstallRoot"
}

# Delete install folder after this process exits (cannot delete running script in-place).
$batchPath = Join-Path $env:TEMP ("caveai-uninstall-" + [Guid]::NewGuid().ToString('N') + '.cmd')
$installEscaped = $InstallRoot -replace '"', '""'
@"
@echo off
ping 127.0.0.1 -n 3 >nul
rd /s /q "$installEscaped"
del "%~f0"
"@ | Set-Content -LiteralPath $batchPath -Encoding OEM

Start-Process -FilePath 'cmd.exe' -ArgumentList @('/q', '/c', "`"$batchPath`"") -WindowStyle Hidden
Write-Host 'CAVE AI PRO has been removed (files will finish deleting in a few seconds).' -ForegroundColor Green
