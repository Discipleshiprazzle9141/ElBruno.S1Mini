<#
.SYNOPSIS
    Validates that required package metadata files are present in each NuGet package.

.DESCRIPTION
    Opens every .nupkg in a directory, reads the nuspec icon and readme entries,
    and verifies that each declared file is actually present inside the package.
#>

[CmdletBinding(SupportsShouldProcess = $false)]
param(
    [string]$PackageDirectory = 'artifacts'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

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

function Get-NuspecMetadataValue {
    param(
        [Parameter(Mandatory = $true)][xml]$Nuspec,
        [Parameter(Mandatory = $true)][string]$ElementName
    )

    $node = $Nuspec.SelectSingleNode("//*[local-name()='package']/*[local-name()='metadata']/*[local-name()='$ElementName']")
    if (-not $node) {
        return ''
    }

    return ([string]$node.InnerText).Trim()
}

function Test-ZipEntryExists {
    param(
        [Parameter(Mandatory = $true)]$Archive,
        [Parameter(Mandatory = $true)][string]$EntryPath
    )

    $normalizedPath = $EntryPath.Replace('\\', '/').TrimStart('/')
    return [bool]($Archive.Entries | Where-Object { $_.FullName.Equals($normalizedPath, [System.StringComparison]::OrdinalIgnoreCase) })
}

function Get-PackConditionHint {
    param([Parameter(Mandatory = $true)][string]$ElementName)

    if ($ElementName -eq 'icon') {
        return 'Likely cause: the conditional <PackageIcon Condition="Exists(''$(MSBuildThisFileDirectory)../../images/nuget_logo.png'')"> in src/ElBruno.S1Mini/ElBruno.S1Mini.csproj evaluated false during pack, or the matching Pack=true item was not included. Ensure the icon file is committed and packed.'
    }

    return 'Likely cause: PackageReadmeFile or its matching Pack=true item in src/ElBruno.S1Mini/ElBruno.S1Mini.csproj was skipped by a conditional Exists(...) check. Ensure the README file is committed and packed.'
}

$repoRoot = Get-RepoRoot
if (-not [System.IO.Path]::IsPathRooted($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot $PackageDirectory
}

$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg' | Where-Object { $_.Name -notlike '*.symbols.nupkg' } | Sort-Object Name)

if ($packages.Count -eq 0) {
    throw "No .nupkg files found under '$PackageDirectory'."
}

$requiredContent = @(
    @{ ElementName = 'icon'; Label = 'NuGet icon' },
    @{ ElementName = 'readme'; Label = 'package README' }
)
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($package in $packages) {
    Write-Host "Validating $($package.Name)..." -ForegroundColor Cyan

    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $nuspecEntry = $archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "Package '$($package.Name)' does not contain a .nuspec."
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try {
            $nuspec = [xml]$reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        foreach ($content in $requiredContent) {
            $elementName = $content.ElementName
            $label = $content.Label
            $entryPath = Get-NuspecMetadataValue -Nuspec $nuspec -ElementName $elementName
            $hint = Get-PackConditionHint -ElementName $elementName

            if ([string]::IsNullOrWhiteSpace($entryPath)) {
                $failures.Add("$($package.Name) is missing the <$elementName> element for the $label. $hint")
                continue
            }

            if (-not (Test-ZipEntryExists -Archive $archive -EntryPath $entryPath)) {
                $failures.Add("$($package.Name) declares <$elementName>$entryPath</$elementName>, but '$entryPath' is missing from the package. $hint")
                continue
            }

            Write-Host "  OK <$elementName>$entryPath</$elementName> and package entry '$entryPath'" -ForegroundColor Green
        }
    }
    finally {
        $archive.Dispose()
    }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    foreach ($failure in $failures) {
        Write-Host "❌ $failure" -ForegroundColor Red
    }

    throw "Package content validation failed for $($failures.Count) required content item(s)."
}

Write-Host ''
Write-Host "Validated $($packages.Count) package(s); all required package content is declared in the nuspec and present in the package." -ForegroundColor Green
