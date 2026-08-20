<#
.SYNOPSIS
    Runs build and unit tests for ElBruno.S1Mini.

.DESCRIPTION
    Orchestrates a full or partial test run against the ElBruno.S1Mini solution.
    Steps: (1) dotnet build, (2) unit tests.
    Each step can be skipped independently via switches.

    Exit codes:
        0  - All requested steps passed
        1  - Build failed
        2  - Unit tests failed
        99 - Unexpected / unhandled error

.PARAMETER SkipBuild
    Skip the dotnet build step.

.PARAMETER NoBuild
    Alias for -SkipBuild (mirrors the common dotnet CLI convention).

.PARAMETER SkipUnitTests
    Skip the unit test project run.

.PARAMETER Framework
    Target framework passed to dotnet build and dotnet test. Defaults to 'net8.0'.

.PARAMETER Filter
    xUnit --filter expression applied to unit tests (e.g. "FullyQualifiedName~TranscriptNormalizerTests").

.EXAMPLE
    .\run-tests.ps1
    Full run: build and unit tests.

.EXAMPLE
    .\run-tests.ps1 -SkipBuild
    Skip build, run tests only (assumes already built).

.EXAMPLE
    .\run-tests.ps1 -Filter "FullyQualifiedName~TranscriptNormalizerTests"
    Build then run only matching tests.
#>

[CmdletBinding(SupportsShouldProcess = $false)]
param(
    [switch]$SkipBuild,
    [switch]$NoBuild,
    [switch]$SkipUnitTests,
    [string]$Framework = 'net8.0',
    [string]$Filter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Banner {
    param([string]$Message, [string]$Color = 'Cyan')
    Write-Host ''
    Write-Host ('=' * 70) -ForegroundColor $Color
    Write-Host "  $Message" -ForegroundColor $Color
    Write-Host ('=' * 70) -ForegroundColor $Color
    Write-Host ''
}

function Write-Step {
    param([string]$Message)
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')]  $Message" -ForegroundColor White
}

function Write-Success {
    param([string]$Message)
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')]  $Message" -ForegroundColor Green
}

function Write-Failure {
    param([string]$Message)
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')]  ERROR: $Message" -ForegroundColor Red
}

function Get-ElapsedSeconds {
    param([datetime]$Start)
    return [Math]::Round(((Get-Date) - $Start).TotalSeconds, 1)
}

$repoRoot = $null
$searchDir = $PSScriptRoot

while ($searchDir -and $searchDir -ne [System.IO.Path]::GetPathRoot($searchDir)) {
    if (Test-Path -LiteralPath (Join-Path $searchDir 'ElBruno.S1Mini.slnx')) {
        $repoRoot = $searchDir
        break
    }
    $searchDir = Split-Path $searchDir -Parent
}

if (-not $repoRoot) {
    Write-Host 'ERROR: Could not find ElBruno.S1Mini.slnx in any parent directory.' -ForegroundColor Red
    exit 99
}

$solutionFile = Join-Path $repoRoot 'ElBruno.S1Mini.slnx'
$unitTestProj = Join-Path $repoRoot 'src\tests\ElBruno.S1Mini.Tests\ElBruno.S1Mini.Tests.csproj'

$scriptStart = Get-Date

Write-Banner -Message "run-tests.ps1  |  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  |  $repoRoot"

$shouldSkipBuild = $SkipBuild -or $NoBuild

if ($shouldSkipBuild) {
    Write-Step 'Build step skipped (-SkipBuild / -NoBuild).'
}
else {
    $buildStart = Get-Date
    Write-Step "Building $solutionFile..."

    & dotnet build $solutionFile
    if ($LASTEXITCODE -ne 0) {
        Write-Failure "Build failed (exit $LASTEXITCODE)."
        exit 1
    }

    Write-Success "Build succeeded in $(Get-ElapsedSeconds $buildStart)s."
}

if ($SkipUnitTests) {
    Write-Step 'Unit tests skipped (-SkipUnitTests).'
}
else {
    $unitStart = Get-Date
    Write-Step "Running unit tests ($Framework)..."

    $testArgs = @(
        'test',
        $unitTestProj,
        '--framework', $Framework,
        '--no-build',
        '--logger', 'console;verbosity=minimal'
    )

    if ($Filter) {
        $testArgs += '--filter'
        $testArgs += $Filter
    }

    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Failure "Unit tests failed (exit $LASTEXITCODE)."
        exit 2
    }

    Write-Success "Unit tests passed in $(Get-ElapsedSeconds $unitStart)s."
}

Write-Host ''
Write-Host ('=' * 70) -ForegroundColor Green
Write-Host ("  All checks passed in $(Get-ElapsedSeconds $scriptStart)s") -ForegroundColor Green
Write-Host ('=' * 70) -ForegroundColor Green

exit 0
