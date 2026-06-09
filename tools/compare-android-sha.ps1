# Compare local signingReport SHA-1 suffixes with google-services.json oauth_client hashes.
param(
    [string]$GoogleServices = 'D:\caveaipro\app\google-services.json',
    [string]$AndroidRoot = 'D:\caveaipro'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $GoogleServices)) {
    Write-Error "Missing $GoogleServices"
}

$doc = Get-Content -LiteralPath $GoogleServices -Raw | ConvertFrom-Json
$registered = @()
foreach ($client in $doc.client) {
    foreach ($oauth in @($client.oauth_client)) {
        if ($null -eq $oauth) { continue }
        $h = $oauth.android_info.certificate_hash
        if ($h) { $registered += $h.ToLower() }
    }
}

Push-Location $AndroidRoot
try {
    $report = & .\gradlew.bat :app:signingReport 2>&1 | Out-String
} finally {
    Pop-Location
}

$local = @()
foreach ($line in ($report -split "`n")) {
    if ($line -match 'Variant:\s*(\S+)') { $variant = $Matches[1] }
    if ($line -match 'SHA1:\s*([0-9A-F:]+)') {
        $sha1 = ($Matches[1] -replace ':', '').ToLower()
        $local += [pscustomobject]@{
            Variant = $variant
            Suffix  = '...' + $sha1.Substring($sha1.Length - 6)
            InJson  = $registered -contains $sha1
        }
    }
}

Write-Host 'Registered in google-services.json (oauth_client certificate_hash suffixes):'
foreach ($h in ($registered | Sort-Object -Unique)) {
    Write-Host ('  ...' + $h.Substring($h.Length - 6))
}

Write-Host ''
Write-Host 'Local signingReport SHA-1:'
$local | Format-Table -AutoSize

$missing = $local | Where-Object { -not $_.InJson }
if ($missing) {
    Write-Warning 'SHA-1 above not found in google-services.json — add in Firebase Console, download fresh json, rebuild AAB if json changed.'
    exit 1
}

Write-Host 'Local debug/release SHA-1 entries are present in google-services.json.'
Write-Host 'If error persists on Play installs, add Play App Signing SHA from Play Console (App integrity) - it is NOT in signingReport.'
exit 0
