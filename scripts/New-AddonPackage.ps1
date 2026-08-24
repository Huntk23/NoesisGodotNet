[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$addonDirectory = Join-Path $repositoryRoot 'addons/noesisgui'
$addonDirectoryPrefix = $addonDirectory + [System.IO.Path]::DirectorySeparatorChar
$rootFiles = @('README.md', 'CHANGELOG.md', 'LICENSE')
$resolvedOutputPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}

if (![System.IO.Directory]::Exists($addonDirectory)) {
    throw "Addon directory '$addonDirectory' does not exist."
}
if ([System.IO.File]::Exists($resolvedOutputPath)) {
    throw "Package output '$resolvedOutputPath' already exists."
}

$outputDirectory = Split-Path -Parent $resolvedOutputPath
if (![string]::IsNullOrEmpty($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = $null
$fileCount = 0
try {
    $archive = [System.IO.Compression.ZipFile]::Open(
        $resolvedOutputPath,
        [System.IO.Compression.ZipArchiveMode]::Create)

    foreach ($rootFile in $rootFiles) {
        $sourcePath = Join-Path $repositoryRoot $rootFile
        if (![System.IO.File]::Exists($sourcePath)) {
            throw "Required package file '$sourcePath' does not exist."
        }

        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $sourcePath,
            $rootFile,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $fileCount++
    }

    $addonFiles = Get-ChildItem -LiteralPath $addonDirectory -File -Recurse | Sort-Object FullName
    foreach ($file in $addonFiles) {
        $relativePath = $file.FullName.Substring($addonDirectoryPrefix.Length).Replace('\', '/')
        $entryName = "addons/noesisgui/$relativePath"
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $file.FullName,
            $entryName,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $fileCount++
    }
} catch {
    if ($null -ne $archive) {
        $archive.Dispose()
        $archive = $null
    }
    if ([System.IO.File]::Exists($resolvedOutputPath)) {
        [System.IO.File]::Delete($resolvedOutputPath)
    }
    throw
} finally {
    if ($null -ne $archive) {
        $archive.Dispose()
    }
}

Write-Host "Created addon package '$resolvedOutputPath' with $fileCount files."
