# Creates and pushes a git tag from Directory.Build.props Version (e.g. v1.2.0).
param(
    [switch]$DryRun,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-Location $RepoRoot

$props = Join-Path $RepoRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $props)) { throw "Missing $props" }

$m = Select-String -LiteralPath $props -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if (-not $m) { throw 'Could not read <Version> from Directory.Build.props' }

$version = $m.Matches[0].Groups[1].Value.Trim()
$tag = "v$version"

Write-Host "Release tag: $tag" -ForegroundColor Cyan

$changelog = Join-Path $RepoRoot 'CHANGELOG.md'
if (Test-Path -LiteralPath $changelog) {
    $header = "## [$version]"
    $hasSection = Select-String -LiteralPath $changelog -Pattern ([regex]::Escape($header)) -Quiet
    if (-not $hasSection) {
        Write-Warning "CHANGELOG.md has no section '$header' — add it before tagging, or release notes step will fail."
    }
} else {
    Write-Warning 'CHANGELOG.md not found.'
}

if ($DryRun) {
    Write-Host 'Dry run — no git commands executed.' -ForegroundColor Yellow
    exit 0
}

git rev-parse --git-dir 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Not a git repository.' }

$existing = git tag -l $tag
if ($existing) { throw "Tag $tag already exists locally." }

git status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
$dirty = git status --porcelain
if ($dirty) {
    Write-Warning 'Working tree has uncommitted changes — tag will point at current HEAD anyway.'
}

git tag -a $tag -m "Release $version"
Write-Host "Created tag $tag" -ForegroundColor Green
Write-Host "Push with: git push origin $tag" -ForegroundColor Cyan
