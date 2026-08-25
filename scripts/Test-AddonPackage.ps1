[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$addonDirectory = Join-Path $repositoryRoot 'addons/noesisgui'
$addonDirectoryPrefix = $addonDirectory + [System.IO.Path]::DirectorySeparatorChar
$resolvedArchivePath = if ([System.IO.Path]::IsPathRooted($ArchivePath)) {
    [System.IO.Path]::GetFullPath($ArchivePath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $ArchivePath))
}

if (![System.IO.File]::Exists($resolvedArchivePath)) {
    throw "Addon package '$resolvedArchivePath' does not exist."
}

$expectedEntries = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($rootFile in @('README.md', 'CHANGELOG.md', 'LICENSE')) {
    if (![System.IO.File]::Exists((Join-Path $repositoryRoot $rootFile))) {
        throw "Required package file '$rootFile' does not exist."
    }
    $expectedEntries.Add($rootFile) | Out-Null
}

$addonFiles = Get-ChildItem -LiteralPath $addonDirectory -File -Recurse
foreach ($file in $addonFiles) {
    $relativePath = $file.FullName.Substring($addonDirectoryPrefix.Length).Replace('\', '/')
    $expectedEntries.Add("addons/noesisgui/$relativePath") | Out-Null
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$actualEntries = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchivePath)
try {
    foreach ($entry in $archive.Entries) {
        if ([string]::IsNullOrEmpty($entry.Name)) {
            continue
        }

        $entryName = $entry.FullName.Replace('\', '/').TrimStart('/')
        if (!$actualEntries.Add($entryName)) {
            throw "Addon package contains duplicate entry '$entryName'."
        }
    }
} finally {
    $archive.Dispose()
}

$missingEntries = @($expectedEntries | Where-Object { !$actualEntries.Contains($_) } | Sort-Object)
$unexpectedEntries = @($actualEntries | Where-Object { !$expectedEntries.Contains($_) } | Sort-Object)
if ($missingEntries.Count -gt 0 -or $unexpectedEntries.Count -gt 0) {
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($missingEntries.Count -gt 0) {
        $problems.Add("Missing entries:`n - " + [string]::Join("`n - ", $missingEntries))
    }
    if ($unexpectedEntries.Count -gt 0) {
        $problems.Add("Unexpected entries:`n - " + [string]::Join("`n - ", $unexpectedEntries))
    }
    throw "Addon package manifest does not match the repository:`n$([string]::Join("`n", $problems))"
}

Write-Host "Verified addon package '$resolvedArchivePath' ($($actualEntries.Count) files)."
