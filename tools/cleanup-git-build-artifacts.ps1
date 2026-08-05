# Cleanup script: remove tracked bin/obj build artifacts from git and delete them from disk.
$ErrorActionPreference = 'Stop'
Write-Host "Running cleanup-git-build-artifacts.ps1 in: $(Get-Location)"

# Find bin/obj directories (exclude .git folder)
$root = (Get-Location).ProviderPath
$dirs = Get-ChildItem -Path . -Directory -Recurse -Force -ErrorAction SilentlyContinue |
	Where-Object { ($_.Name -in @('bin','obj')) -and ($_.FullName -notmatch '\\.git\\') }

if (-not $dirs) {
	Write-Host "No bin/ or obj/ directories found under the repository. Nothing to do."
	exit 0
}

Write-Host "Found $($dirs.Count) build directories. Removing from git index..."

$removedAny = $false
foreach ($d in $dirs) {
	# Compute path relative to repo root
	$rel = $d.FullName.Substring($root.Length + 1)
	Write-Host " - git rm --cached --ignore-unmatch -r '$rel'"
	git rm -r --cached --ignore-unmatch -- "$rel" 2>$null
	if ($LASTEXITCODE -eq 0) { $removedAny = $true }
}

if ($removedAny) {
	Write-Host "Staging .gitignore and committing removal of tracked build artifacts..."
	git add .gitignore
	$status = git status --porcelain
	if ($status) {
		git commit -m "Remove tracked build artifacts (bin/obj) and respect .gitignore" -q
		Write-Host "Committed changes to git."
	}
} else {
	Write-Host "No tracked build files were removed from the git index."
}

Write-Host "Deleting bin/obj directories from disk..."
foreach ($d in $dirs) {
	try {
		Write-Host " - Removing $($d.FullName)"
		Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction Stop
	} catch {
		Write-Warning "Failed to remove $($d.FullName): $_"
	}
}

Write-Host "Cleanup complete."
