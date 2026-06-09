# Verify Android google-services.json uses expected Android API key suffix.
param(
    [string]$ExpectedSuffix = 'RRRmA0',
    [string]$AndroidJson = 'D:\caveaipro\app\google-services.json',
    [string]$LocalCopy = 'D:\caveaiproforwindows\tools\local\google-services.json'
)

$ErrorActionPreference = 'Stop'

function Read-AndroidMeta {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Path = $Path; Missing = $true }
    }
    $j = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $key = $j.client[0].api_key[0].current_key
    $pkg = ($j.client | Where-Object { $_.client_info.android_client_info } | Select-Object -First 1).client_info.android_client_info.package_name
    $suffix = '...' + $key.Substring($key.Length - 6)
    return [pscustomobject]@{
        Path    = $Path
        Missing = $false
        Package = $pkg
        Suffix  = $suffix
        Project = $j.project_info.project_id
        Match   = ($suffix -eq ('...' + $ExpectedSuffix))
    }
}

$rows = @(
    Read-AndroidMeta $AndroidJson
    Read-AndroidMeta $LocalCopy
)
$rows | Format-Table -AutoSize

$bad = $rows | Where-Object { $_.Missing -or -not $_.Match }
if ($bad.Count -gt 0) {
    Write-Host "Android google-services.json mismatch (expected suffix ...$ExpectedSuffix)."
    exit 1
}

Write-Host "OK: Android Play app google-services.json uses suffix ...$ExpectedSuffix."
exit 0
