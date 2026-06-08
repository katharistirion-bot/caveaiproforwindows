# Extracts the CHANGELOG.md section for a semver (e.g. 1.2.1 from tag v1.2.1).
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$ChangelogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ChangelogPath)) {
    throw "Missing changelog: $ChangelogPath"
}

$lines = Get-Content -LiteralPath $ChangelogPath -Encoding utf8
$header = "## [$Version]"
$start = [array]::IndexOf($lines, $header)
if ($start -lt 0) {
    throw "No CHANGELOG section '$header' in $ChangelogPath"
}

$body = New-Object System.Collections.Generic.List[string]
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match '^## \[') { break }
    $body.Add($line)
}

while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[0])) { $body.RemoveAt(0) }
while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[$body.Count - 1])) { $body.RemoveAt($body.Count - 1) }

if ($body.Count -eq 0) {
    throw "CHANGELOG section '$header' is empty."
}

return ($body -join "`n")
