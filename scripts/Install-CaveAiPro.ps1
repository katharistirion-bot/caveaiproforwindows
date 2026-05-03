#Requires -Version 5.1
param(
    # Larger folder, but runs without installing .NET Desktop Runtime on the PC.
    [switch]$SelfContained
)
# Per-user install: Start menu shortcut + "Apps & features" entry. No admin required.
$ErrorActionPreference = 'Stop'

function Get-ProjectVersion {
    param([string]$ProjectPath)
    $m = Select-String -LiteralPath $ProjectPath -Pattern '<Version>(\d+\.\d+\.\d+[^<]*)</Version>' -ErrorAction SilentlyContinue
    if ($m) { return $m.Matches[0].Groups[1].Value }
    return '0.0.0'
}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ProjectPath = Join-Path $RepoRoot 'src\CaveAiProForWindows\CaveAiProForWindows.csproj'
if (-not (Test-Path -LiteralPath $ProjectPath)) {
    Write-Error "Project not found: $ProjectPath"
}

$null = Get-Command dotnet -ErrorAction Stop

$Version = Get-ProjectVersion -ProjectPath $ProjectPath
$PublishDir = Join-Path $env:TEMP ("CaveAiProPublish-" + [Guid]::NewGuid().ToString('N'))
$InstallRoot = Join-Path $env:LOCALAPPDATA 'Programs\CaveAiProForWindows'
$ExeName = 'CaveAiProForWindows.exe'

$sc = if ($SelfContained) { 'true' } else { 'false' }
Write-Host "Publishing CAVE AI PRO ($Version), self-contained=$sc..." -ForegroundColor Cyan
& dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained $sc `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Test-Path -LiteralPath $InstallRoot) {
    Write-Host "Removing previous install..." -ForegroundColor Yellow
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null

Copy-Item -Path (Join-Path $PublishDir '*') -Destination $InstallRoot -Recurse -Force
Get-ChildItem -LiteralPath $InstallRoot -Filter '*.pdb' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

# Same marker as MSI (HKCU) so Release builds only run from this folder (InstallationGuard).
$appMarker = 'HKCU:\Software\CaveAiPro\CaveAiProForWindows'
New-Item -Path $appMarker -Force | Out-Null
Set-ItemProperty -LiteralPath $appMarker -Name 'Installed' -Value 1 -Type DWord
Set-ItemProperty -LiteralPath $appMarker -Name 'InstallDir' -Value $InstallRoot

$uninstallSrc = Join-Path $PSScriptRoot 'Uninstall-CaveAiPro.ps1'
Copy-Item -LiteralPath $uninstallSrc -Destination (Join-Path $InstallRoot 'Uninstall-CaveAiPro.ps1') -Force

$exePath = Join-Path $InstallRoot $ExeName
if (-not (Test-Path -LiteralPath $exePath)) {
    Write-Error "Publish did not produce: $exePath"
}

$WshShell = New-Object -ComObject WScript.Shell
$programsFolder = [Environment]::GetFolderPath('Programs')
$shortcutPath = Join-Path $programsFolder 'CAVE AI PRO.lnk'
$shortcut = $WshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $InstallRoot
$shortcut.Description = 'PC companion for CaveAI Pro (Android on Google Play). Windows x64 only.'
$shortcut.Save()

$regKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CaveAiProForWindows'
New-Item -Path $regKey -Force | Out-Null
$uninstallCmd = "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $InstallRoot 'Uninstall-CaveAiPro.ps1')`""
Set-ItemProperty -LiteralPath $regKey -Name 'DisplayName' -Value 'CAVE AI PRO'
Set-ItemProperty -LiteralPath $regKey -Name 'DisplayVersion' -Value $Version
Set-ItemProperty -LiteralPath $regKey -Name 'Publisher' -Value 'CAVE AI PRO'
Set-ItemProperty -LiteralPath $regKey -Name 'InstallLocation' -Value $InstallRoot
Set-ItemProperty -LiteralPath $regKey -Name 'UninstallString' -Value $uninstallCmd
Set-ItemProperty -LiteralPath $regKey -Name 'QuietUninstallString' -Value $uninstallCmd
Set-ItemProperty -LiteralPath $regKey -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -LiteralPath $regKey -Name 'NoRepair' -Value 1 -Type DWord

Remove-Item -LiteralPath $PublishDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Installed to: $InstallRoot" -ForegroundColor Green
Write-Host "Start menu: CAVE AI PRO" -ForegroundColor Green
Write-Host "Companion for Android app from Google Play; this install is for PC desktop only (not from Play)." -ForegroundColor DarkGray
if (-not $SelfContained) {
    Write-Host "Requires .NET 8 Desktop Runtime if not already installed:" -ForegroundColor DarkGray
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor DarkGray
} else {
    Write-Host "Self-contained build: no separate .NET runtime install needed." -ForegroundColor DarkGray
}
Write-Host ""
