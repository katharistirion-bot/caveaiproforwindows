# Signs Release artifacts with Authenticode when WINDOWS_CERT_BASE64 + WINDOWS_CERT_PASSWORD are set (GitHub secrets).
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:WINDOWS_CERT_BASE64)) {
    Write-Host 'Code signing skipped — WINDOWS_CERT_BASE64 secret not configured.'
    exit 0
}

$signtool = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe",
    "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\signtool.exe"
) | ForEach-Object { Get-Item $_ -ErrorAction SilentlyContinue } | Sort-Object FullName -Descending | Select-Object -First 1

if (-not $signtool) {
    throw 'signtool.exe not found. Install Windows SDK on the runner or build agent.'
}

$pfxPath = Join-Path $env:RUNNER_TEMP 'codesign.pfx'
if (-not $env:RUNNER_TEMP) { $pfxPath = Join-Path ([IO.Path]::GetTempPath()) 'codesign.pfx' }

try {
    [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($env:WINDOWS_CERT_BASE64))
    $password = $env:WINDOWS_CERT_PASSWORD
    if ([string]::IsNullOrEmpty($password)) { throw 'WINDOWS_CERT_PASSWORD is required when WINDOWS_CERT_BASE64 is set.' }

    foreach ($file in $Files) {
        if (-not (Test-Path -LiteralPath $file)) { throw "Missing file to sign: $file" }
        Write-Host "Signing $file"
        & $signtool.FullName sign /fd SHA256 /f $pfxPath /p $password /tr http://timestamp.digicert.com /td SHA256 /a $file
        if ($LASTEXITCODE -ne 0) { throw "signtool failed for $file (exit $LASTEXITCODE)" }
    }
}
finally {
    if (Test-Path -LiteralPath $pfxPath) { Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue }
}

Write-Host 'Code signing completed.'
