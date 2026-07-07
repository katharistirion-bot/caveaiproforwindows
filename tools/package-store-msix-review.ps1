# Builds an unsigned MSIX for Microsoft Store certification (subscription gate disabled).
# After certification passes, submit the normal build from package-store-msix.ps1 instead.

param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$IdentityName = 'GeorgiosKourentzis.CaveAIPro',
    [string]$Publisher = 'CN=54966508-95FA-45A0-B2A2-D1AF31D44DC4',
    [string]$OutDir = ''
)

& (Join-Path $PSScriptRoot 'package-store-msix.ps1') `
    -RepoRoot $RepoRoot `
    -IdentityName $IdentityName `
    -Publisher $Publisher `
    -OutDir $OutDir `
    -PublishProfile 'MicrosoftStore-Review-Win64' `
    -MsixNameSuffix 'Store-Review-unsigned' `
    -PublishRelativeDir 'microsoft-store-review/win-x64' `
    -AllowReviewBuild
