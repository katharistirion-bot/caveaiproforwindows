# Verifies Microsoft Store publish output is self-contained (no separate .NET Desktop Runtime install).
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDir)) {
    Write-Error "Publish directory not found: $PublishDir"
    exit 1
}

$exe = Join-Path $PublishDir 'CaveAiProForWindows.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Error "Missing published executable: $exe"
    exit 1
}

$runtimeConfigPath = Join-Path $PublishDir 'CaveAiProForWindows.runtimeconfig.json'
if (-not (Test-Path -LiteralPath $runtimeConfigPath)) {
    Write-Error "Missing runtimeconfig.json: $runtimeConfigPath"
    exit 1
}

$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$runtimeOptions = $runtimeConfig.runtimeOptions

if ($null -ne $runtimeOptions.frameworks) {
    Write-Error @'
Store publish is framework-dependent (runtimeconfig.json uses "frameworks").
Run a full "dotnet publish -p:PublishProfile=MicrosoftStore-Win64" without --no-build
so runtimeconfig.json uses "includedFrameworks" and bundles the .NET runtime.
'@
    exit 1
}

if ($null -eq $runtimeOptions.includedFrameworks -or $runtimeOptions.includedFrameworks.Count -lt 1) {
    Write-Error 'runtimeconfig.json is missing "includedFrameworks" — publish is not self-contained.'
    exit 1
}

$requiredNative = @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')
foreach ($name in $requiredNative) {
    $path = Join-Path $PublishDir $name
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Error "Missing native runtime file (self-contained layout): $name"
        exit 1
    }
}

Write-Host "OK: Store publish is self-contained ($PublishDir)"
exit 0
