Write-Host "This script renames KyFromAbove certificate files to KyFromAbove-STAC and updates scripts that reference the filenames."
$root = (Get-Location).ProviderPath
$oldCer = Join-Path $root 'KyFromAbove.cer'
$oldPfx = Join-Path $root 'KyFromAbove.pfx'
$newCer = Join-Path $root 'KyFromAbove-STAC.cer'
$newPfx = Join-Path $root 'KyFromAbove-STAC.pfx'
if (Test-Path $oldCer) { Rename-Item -LiteralPath $oldCer -NewName (Split-Path $newCer -Leaf) -Force }
if (Test-Path $oldPfx) { Rename-Item -LiteralPath $oldPfx -NewName (Split-Path $newPfx -Leaf) -Force }
Write-Host "Renamed certificate files (if present). Review tools/*.ps1 for remaining references to KyFromAbove and update as needed." 
