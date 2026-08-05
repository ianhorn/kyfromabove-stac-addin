# Cleanup build folders (disk only) and produce git commands to remove tracked artifacts.
$ErrorActionPreference = 'Stop'
Write-Host "Running cleanup-build-disk-only.ps1 in: $(Get-Location)"

$root = (Get-Location).ProviderPath
$dirs = Get-ChildItem -Path . -Directory -Recurse -Force -ErrorAction SilentlyContinue |
	Where-Object { ($_.Name -in @('bin','obj')) -and ($_.FullName -notmatch '\\.git\\') }

if (-not $dirs) {
	Write-Host "No bin/ or obj/ directories found under the repository. Nothing to do."
	exit 0
}

Write-Host "Found $($dirs.Count) build directories. Generating suggested git commands and removing directories from disk..."

$gitCmds = @()
foreach ($d in $dirs) {
	$rel = $d.FullName.Substring($root.Length + 1)
	$gitCmds += "git rm -r --cached --ignore-unmatch -- '$rel'"
}

$gitCmds | Out-File -FilePath tools\git-remove-build-artifacts.txt -Encoding utf8
Write-Host "Wrote suggested git commands to tools/git-remove-build-artifacts.txt"

foreach ($d in $dirs) {
	try {
		Write-Host " - Removing $($d.FullName) from disk"
		Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction Stop
	} catch {
		Write-Warning "Failed to remove $($d.FullName): $_"
	}
}

Write-Host "Disk cleanup complete. Review and run the commands in tools/git-remove-build-artifacts.txt to remove tracked build artifacts from git index and commit the changes."
