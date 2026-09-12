<#
.SYNOPSIS
    Builds the Release .esriAddinX for both supported ArcGIS Pro versions and publishes them as
    assets on a new GitHub Release.

.DESCRIPTION
    Requires ArcGIS Pro + the ArcGIS Pro SDK for .NET installed locally (the project's build
    targets and assembly references live under "C:\Program Files\ArcGIS\Pro\bin", so this cannot
    run on a hosted CI runner -- see docs-src/installation.md). Also requires the GitHub CLI
    ("gh"), authenticated ("gh auth login"), with push/release permission on the repo.

    Builds "for-v3.6.x" and "master" each in their own temporary git worktree, so your current
    checkout and any uncommitted work are left untouched. Neither branch's working tree is
    modified or committed to.

.PARAMETER Version
    Release version, without a leading "v" (e.g. "1.2.0"). The GitHub tag and release title are
    "v<Version>".

.PARAMETER Notes
    Release notes. If omitted, gh auto-generates notes from commits since the previous tag.

.PARAMETER DryRun
    Build and stage both .esriAddinX files but skip creating the GitHub release. The staged files
    are left in release-staging\ for inspection.

.EXAMPLE
    tools\release.ps1 -Version 1.2.0

.EXAMPLE
    tools\release.ps1 -Version 1.2.0 -Notes "Adds the Help button and per-section docs links."
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$Notes,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$tag = "v$Version"

function Find-MSBuild {
    $vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found at '$vswhere' -- install Visual Studio, or edit this script to point at MSBuild.exe directly."
    }
    $msbuildPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    if (-not $msbuildPath) {
        throw "vswhere couldn't find an MSBuild.exe. Is Visual Studio (with the ArcGIS Pro SDK workload) installed?"
    }
    return $msbuildPath
}

function Assert-GhReady {
    $null = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $?) {
        throw "GitHub CLI ('gh') not found on PATH. Install it (winget install --id GitHub.cli) and run 'gh auth login'."
    }
    # Redirect stdout only -- redirecting stderr here would turn gh's non-zero exit into a
    # terminating error (via $ErrorActionPreference) before the friendlier throw below runs.
    gh auth status 1> $null
    if ($LASTEXITCODE -ne 0) {
        throw "gh is not authenticated. Run 'gh auth login' first."
    }
}

if (-not $DryRun) { Assert-GhReady }
$msbuild = Find-MSBuild
Write-Host "Using MSBuild: $msbuild"

$repoRoot = (git rev-parse --show-toplevel).Trim()
if (-not $repoRoot) { throw "Not inside a git repository." }
Push-Location $repoRoot
try {
    $status = git status --porcelain
    # Worktrees build in isolated checkouts, so a dirty current tree is not itself unsafe -- but
    # warn anyway, since it's easy to forget you meant to commit something first.
    if ($status) {
        Write-Warning "Your current checkout has uncommitted changes. They are NOT included in the release build (each branch is built from its own committed tip in a separate worktree)."
    }

    $targets = @(
        @{ Branch = "for-v3.6.x"; Tfm = "net8.0-windows";  AssetName = "KyFromAboveSTACAddin-3.6.x.esriAddinX" },
        @{ Branch = "master";     Tfm = "net10.0-windows"; AssetName = "KyFromAboveSTACAddin-3.7.x.esriAddinX" }
    )

    $stagingDir = Join-Path $repoRoot "release-staging"
    if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
    New-Item -ItemType Directory -Path $stagingDir | Out-Null

    foreach ($t in $targets) {
        $branch = $t.Branch
        $worktreeName = "kyfromabove-release-$($branch -replace '[^\w.]', '-')"
        $worktreePath = Join-Path (Split-Path $repoRoot -Parent) $worktreeName

        if (Test-Path $worktreePath) {
            Write-Host "Removing stale worktree at $worktreePath"
            git worktree remove $worktreePath --force 2>$null
            if (Test-Path $worktreePath) { Remove-Item $worktreePath -Recurse -Force }
        }

        Write-Host "`n=== Building $branch ($($t.Tfm)) ==="
        # --detach: $branch may already be checked out in this repo's main worktree (or another
        # one), which "git worktree add <path> <branch>" refuses. Detached HEAD at the branch's
        # tip commit avoids that conflict entirely.
        git worktree add --detach $worktreePath $branch
        if ($LASTEXITCODE -ne 0) { throw "git worktree add failed for $branch" }

        try {
            Push-Location $worktreePath
            & $msbuild "KyFromAboveSTACAddin.csproj" /t:Restore /v:minimal
            if ($LASTEXITCODE -ne 0) { throw "Restore failed for $branch" }

            & $msbuild "KyFromAboveSTACAddin.csproj" /p:Configuration=Release /v:minimal
            if ($LASTEXITCODE -ne 0) { throw "Build failed for $branch" }
        }
        finally {
            Pop-Location
        }

        $builtFile = Join-Path $worktreePath "bin\Release\$($t.Tfm)\KyFromAboveSTACAddin.esriAddinX"
        if (-not (Test-Path $builtFile)) {
            throw "Expected build output not found: $builtFile"
        }

        $stagedFile = Join-Path $stagingDir $t.AssetName
        Copy-Item $builtFile $stagedFile -Force
        $sizeMb = [math]::Round((Get-Item $stagedFile).Length / 1MB, 1)
        Write-Host "Staged $($t.AssetName) ($sizeMb MB)"

        git worktree remove $worktreePath --force
    }

    if ($DryRun) {
        Write-Host "`n-DryRun set: skipping GitHub release. Staged files are in $stagingDir"
        return
    }

    Write-Host "`n=== Creating GitHub release $tag ==="
    $assetPaths = $targets | ForEach-Object { Join-Path $stagingDir $_.AssetName }
    $ghArgs = @($tag) + $assetPaths + @("--title", $tag, "--target", "master")
    if ($Notes) { $ghArgs += @("--notes", $Notes) } else { $ghArgs += "--generate-notes" }

    gh release create @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }

    Remove-Item $stagingDir -Recurse -Force
    Write-Host "`nReleased $tag with assets: $($targets.AssetName -join ', ')"
    Write-Host "Stable download links (update once in docs-src/installation.md, never again):"
    foreach ($t in $targets) {
        Write-Host "  https://github.com/ianhorn/kyfromabove-stac-addin/releases/latest/download/$($t.AssetName)"
    }
}
finally {
    Pop-Location
}
