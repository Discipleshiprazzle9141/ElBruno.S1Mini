<#
.SYNOPSIS
    Bumps the repository's package version in every file that must stay in sync.

.DESCRIPTION
    Updates the following in one pass:

    1. src/ElBruno.S1Mini/ElBruno.S1Mini.csproj -> <Version>
    2. README.md                                -> "## What's New" section (prepends
                                                     a new bullet, keeps exactly the
                                                     last 5 entries)

    Run Validate-ReleaseVersion.ps1 afterwards (and before publishing) to confirm
    every file agrees on the new version.

    ElBruno.S1Mini has a single packable project and no CHANGELOG.md, so the
    version source of truth is the csproj <Version> element and the README
    What's New section that the publish workflow validates.

.PARAMETER Version
    The new package version, e.g. "0.2.0". Must be a valid SemVer core
    (major.minor.patch, optional prerelease suffix).

.PARAMETER Highlight
    The README "What's New" bullet text for this release, without the leading
    "- " marker. Example:
    '🚀 **`v0.2.0`** — Adds streaming NormalizeAsync overload.'

    If it does not already mention "v$Version", the script fails fast so the
    bullet can't silently point at the wrong release.

.EXAMPLE
    ./scripts/Set-ReleaseVersion.ps1 -Version 0.2.0 `
        -Highlight '🚀 **`v0.2.0`** — Adds streaming NormalizeAsync overload.'
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Highlight
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    $searchDir = $PSScriptRoot

    while ($searchDir -and $searchDir -ne [System.IO.Path]::GetPathRoot($searchDir)) {
        if (Test-Path -LiteralPath (Join-Path $searchDir 'ElBruno.S1Mini.slnx')) {
            return $searchDir
        }

        $searchDir = Split-Path $searchDir -Parent
    }

    throw 'Could not find ElBruno.S1Mini.slnx in any parent directory.'
}

$repoRoot = Get-RepoRoot
$csprojPath = Join-Path $repoRoot 'src\ElBruno.S1Mini\ElBruno.S1Mini.csproj'
$readmePath = Join-Path $repoRoot 'README.md'

if (-not ($Highlight -match [regex]::Escape("v$Version"))) {
    throw "Highlight text must mention 'v$Version' so the What's New bullet matches the release. Got: '$Highlight'"
}

if (-not $Highlight.TrimStart().StartsWith('-')) {
    $bulletLine = "- $Highlight"
} else {
    $bulletLine = $Highlight
}

# --- 1. csproj <Version> ------------------------------------------------------

Write-Host "Updating $csprojPath..." -ForegroundColor Cyan
$csprojContent = Get-Content -LiteralPath $csprojPath -Raw
$versionPattern = '<Version>[^<]*</Version>'

if ($csprojContent -notmatch $versionPattern) {
    throw "Could not find <Version> element in $csprojPath."
}

$oldVersionMatch = [regex]::Match($csprojContent, '<Version>([^<]*)</Version>')
$oldVersion = $oldVersionMatch.Groups[1].Value

$csprojContent = [regex]::Replace(
    $csprojContent,
    $versionPattern,
    "<Version>$Version</Version>"
)

if ($PSCmdlet.ShouldProcess($csprojPath, "Set <Version> $oldVersion -> $Version")) {
    Set-Content -LiteralPath $csprojPath -Value $csprojContent -NoNewline
}

Write-Host "  $oldVersion -> $Version" -ForegroundColor Green

# --- 2. README.md What's New --------------------------------------------------

Write-Host "Updating $readmePath..." -ForegroundColor Cyan
$readmeLines = Get-Content -LiteralPath $readmePath

$startIdx = -1
$endIdx = $readmeLines.Count

for ($i = 0; $i -lt $readmeLines.Count; $i++) {
    if ($readmeLines[$i] -match "^## What's New") {
        $startIdx = $i
        continue
    }

    if ($startIdx -ge 0 -and $i -gt $startIdx -and $readmeLines[$i] -match '^## ') {
        $endIdx = $i
        break
    }
}

if ($startIdx -lt 0) {
    throw "Could not find a '## What's New' section in $readmePath."
}

$sectionLines = $readmeLines[($startIdx + 1)..($endIdx - 1)]
$bulletIdxs = @()
for ($i = 0; $i -lt $sectionLines.Count; $i++) {
    if ($sectionLines[$i] -match '^- ') {
        $bulletIdxs += $i
    }
}

if ($bulletIdxs.Count -eq 0) {
    throw "Could not find any '- ' bullet entries under '## What's New' in $readmePath."
}

$preambleLines = if ($bulletIdxs[0] -gt 0) { $sectionLines[0..($bulletIdxs[0] - 1)] } else { @() }
$lastBulletIdx = $bulletIdxs[$bulletIdxs.Count - 1]
$trailingLines = if ($lastBulletIdx -lt $sectionLines.Count - 1) { $sectionLines[($lastBulletIdx + 1)..($sectionLines.Count - 1)] } else { @() }
$existingBullets = @($bulletIdxs | ForEach-Object { $sectionLines[$_] })
$newBullets = @($bulletLine) + $existingBullets
if ($newBullets.Count -gt 5) {
    $droppedBullets = $newBullets[5..($newBullets.Count - 1)]
    $newBullets = $newBullets[0..4]
    foreach ($dropped in $droppedBullets) {
        Write-Host "  Dropping oldest What's New entry to keep exactly 5: $dropped" -ForegroundColor Yellow
    }
}

$newSectionLines = @($preambleLines) + @($newBullets) + @($trailingLines)
$newReadmeLines = @($readmeLines[0..$startIdx]) + $newSectionLines + @($readmeLines[$endIdx..($readmeLines.Count - 1)])

if ($PSCmdlet.ShouldProcess($readmePath, "Prepend What's New bullet for v$Version")) {
    Set-Content -LiteralPath $readmePath -Value $newReadmeLines
}

Write-Host "  Prepended bullet for v$Version; What's New now has $($newBullets.Count) entries." -ForegroundColor Green

Write-Host ''
Write-Host "Version bump to $Version complete. Run scripts/Validate-ReleaseVersion.ps1 -Version $Version next." -ForegroundColor Cyan
