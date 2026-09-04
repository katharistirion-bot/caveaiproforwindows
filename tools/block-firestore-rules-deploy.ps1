# Hard stop: never deploy Firestore rules from the Windows repo.
# Canonical rules live in CaveAIpro website (or Android). This stub would lock the product.
$ErrorActionPreference = 'Stop'
Write-Host 'Do not deploy Firestore rules from caveaiproforwindows.'
Write-Host 'This repo firebase/firestore.rules is a deny-all STUB.'
Write-Host 'Canonical rules: CaveAIpro website/firebase/firestore.rules or CaveAIPro/firebase/firestore.rules'
Write-Host 'From the website repo run: npm run deploy:rules'
exit 1
