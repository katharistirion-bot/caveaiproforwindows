# Ensures Windows stub firestore.rules was NOT accidentally copied to website/Android canonical rules.
param(
    [string]$WindowsRules = (Join-Path (Split-Path -Parent $PSScriptRoot) 'firebase\firestore.rules'),
    [string]$WebsiteRules = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'CaveAIpro website\firebase\firestore.rules'),
    [string]$AndroidRules = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'caveaipro\firebase\firestore.rules')
)

$ErrorActionPreference = 'Stop'

function Get-LineCount { param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return -1 }
    return (Get-Content -LiteralPath $Path).Count
}

$winLines = Get-LineCount $WindowsRules
$webLines = Get-LineCount $WebsiteRules
$andLines = Get-LineCount $AndroidRules

Write-Host "Windows stub:  $winLines lines  ($WindowsRules)"
Write-Host "Website rules:   $webLines lines  ($WebsiteRules)"
Write-Host "Android rules:   $andLines lines  ($AndroidRules)"

if ($winLines -gt 0 -and $webLines -gt 0 -and $winLines -lt 50 -and $webLines -gt 200) {
    Write-Host 'OK: Website has full Public Library rules; Windows stub is short (expected).'
} elseif ($webLines -lt 50) {
    throw 'Website firestore.rules looks like Windows stub — deploy would break Public Library!'
}

if ($webLines -gt 0 -and $andLines -gt 0) {
    $webText = (Get-Content -LiteralPath $WebsiteRules -Raw).Replace("`r`n", "`n")
    $andText = (Get-Content -LiteralPath $AndroidRules -Raw).Replace("`r`n", "`n")
    if ($webText -eq $andText) {
        Write-Host 'OK: Website and Android firestore.rules are identical (normalized).'
    } else {
        Write-Warning 'Website and Android firestore.rules differ — sync before deploy.'
        exit 1
    }
}

exit 0
