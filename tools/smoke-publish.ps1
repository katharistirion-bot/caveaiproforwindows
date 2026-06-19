# Publishes CaveAiProForWindows (ReleaseSingleFile-Win64), verifies output, optional process smoke test.
param(
    [switch]$SkipTests,
    [switch]$LaunchSmokeTest,
    [switch]$NoRestore,
    [int]$SmokeSeconds = 8
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not $SkipTests) {
    Write-Host "Running unit tests (Debug)…"
    dotnet test (Join-Path $repoRoot 'tests/CaveAiProForWindows.Tests/CaveAiProForWindows.Tests.csproj') -c Debug --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
}

Write-Host "Publishing Release single-file win-x64…"
$publishArgs = @(
    (Join-Path $repoRoot 'src/CaveAiProForWindows/CaveAiProForWindows.csproj'),
    '-c', 'Release',
    '-r', 'win-x64',
    '-p:PublishProfile=ReleaseSingleFile-Win64',
    '--verbosity', 'minimal'
)
if ($NoRestore) { $publishArgs += '--no-restore' }
dotnet publish @publishArgs

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$pubDir = Join-Path $repoRoot 'src/CaveAiProForWindows/bin/Release/net8.0-windows/publish/win-x64'
$exe = Join-Path $pubDir 'CaveAiProForWindows.exe'
$manifest = Join-Path $pubDir 'publish-manifest.json'

if (-not (Test-Path $exe)) { throw "Missing published exe: $exe" }
if (-not (Test-Path $manifest)) { throw "Missing publish-manifest.json: $manifest" }

$exeInfo = Get-Item $exe
$sizeMb = [math]::Round($exeInfo.Length / 1MB, 1)
Write-Host "OK: $exe ($sizeMb MB)"
Write-Host "OK: $manifest"

$manifestJson = Get-Content $manifest -Raw | ConvertFrom-Json
if ($manifestJson.runtimeIdentifier -ne 'win-x64') {
    throw "publish-manifest.json runtimeIdentifier expected win-x64, got $($manifestJson.runtimeIdentifier)"
}

if ($LaunchSmokeTest) {
    Write-Host "Smoke launch: registering dev install dir and starting process for $SmokeSeconds second(s)…"
    $regPath = 'HKCU:\SOFTWARE\CaveAiPro\CaveAiProForWindows'
    $hadReg = Test-Path -LiteralPath $regPath
    $backupInstalled = $null
    $backupInstallDir = $null
    if ($hadReg) {
        $backupInstalled = (Get-ItemProperty -LiteralPath $regPath -Name 'Installed' -ErrorAction SilentlyContinue).Installed
        $backupInstallDir = (Get-ItemProperty -LiteralPath $regPath -Name 'InstallDir' -ErrorAction SilentlyContinue).InstallDir
    }
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty -Path $regPath -Name 'Installed' -Value 1 -Type DWord
    Set-ItemProperty -Path $regPath -Name 'InstallDir' -Value $pubDir

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    if ($null -eq $p) {
        if ($hadReg) {
            if ($null -ne $backupInstalled) { Set-ItemProperty -Path $regPath -Name 'Installed' -Value $backupInstalled -Type DWord }
            if ($null -ne $backupInstallDir) { Set-ItemProperty -Path $regPath -Name 'InstallDir' -Value $backupInstallDir }
        } else {
            Remove-Item -LiteralPath $regPath -Recurse -Force -ErrorAction SilentlyContinue
        }
        throw 'Failed to start published executable.'
    }
    try {
        Start-Sleep -Seconds $SmokeSeconds
        if ($p.HasExited -and $p.ExitCode -ne 0) {
            throw "Application exited within ${SmokeSeconds}s with exit code $($p.ExitCode)."
        }
        if ($p.HasExited) {
            throw "Application exited within ${SmokeSeconds}s (exit code 0) before smoke window elapsed."
        }
        Write-Host "Smoke launch OK: process still running after ${SmokeSeconds}s."
    }
    finally {
        if (-not $p.HasExited) {
            $null = $p.Kill()
            $p.WaitForExit(5000)
        }
        if ($hadReg) {
            if ($null -ne $backupInstalled) { Set-ItemProperty -Path $regPath -Name 'Installed' -Value $backupInstalled -Type DWord } else { Remove-ItemProperty -Path $regPath -Name 'Installed' -ErrorAction SilentlyContinue }
            if ($null -ne $backupInstallDir) { Set-ItemProperty -Path $regPath -Name 'InstallDir' -Value $backupInstallDir } else { Remove-ItemProperty -Path $regPath -Name 'InstallDir' -ErrorAction SilentlyContinue }
        } else {
            Remove-Item -LiteralPath $regPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "Smoke publish completed successfully."
