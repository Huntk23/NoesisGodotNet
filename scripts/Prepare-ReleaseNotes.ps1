[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ReleaseTag,

    [Parameter(Mandatory)]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$changelogPath = Join-Path $repositoryRoot 'CHANGELOG.md'
$pluginConfigPath = Join-Path $repositoryRoot 'addons/noesisgui/plugin.cfg'
$version = $ReleaseTag -replace '^v', ''

if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Release tag '$ReleaseTag' does not contain a supported semantic version."
}

$pluginConfig = [System.IO.File]::ReadAllText($pluginConfigPath)
$pluginVersionMatch = [regex]::Match($pluginConfig, '(?m)^version\s*=\s*"([^"]+)"\s*$')
if (!$pluginVersionMatch.Success) {
    throw "No version entry was found in '$pluginConfigPath'."
}

$pluginVersion = $pluginVersionMatch.Groups[1].Value
if ($pluginVersion -ne $version) {
    throw "Tag '$ReleaseTag' does not match plugin.cfg version '$pluginVersion'."
}

$lines = [System.IO.File]::ReadAllLines($changelogPath)
$versionHeading = '^##\s+' + [regex]::Escape($version) + '(?:\s|$)'
$sectionStart = -1
for ($index = 0; $index -lt $lines.Length; $index++) {
    if ($lines[$index] -match $versionHeading) {
        $sectionStart = $index + 1
        break
    }
}

if ($sectionStart -lt 0) {
    throw "No changelog section was found for version '$version'."
}

$sectionEnd = $lines.Length
for ($index = $sectionStart; $index -lt $lines.Length; $index++) {
    if ($lines[$index] -match '^##\s+') {
        $sectionEnd = $index
        break
    }
}

$section = [System.Collections.Generic.List[string]]::new()
for ($index = $sectionStart; $index -lt $sectionEnd; $index++) {
    $section.Add($lines[$index])
}

while ($section.Count -gt 0 -and [string]::IsNullOrWhiteSpace($section[0])) {
    $section.RemoveAt(0)
}
while ($section.Count -gt 0 -and [string]::IsNullOrWhiteSpace($section[$section.Count - 1])) {
    $section.RemoveAt($section.Count - 1)
}

if ($section.Count -eq 0) {
    throw "The changelog section for version '$version' is empty."
}

$releaseNotes = [System.Collections.Generic.List[string]]::new()
$releaseNotes.Add('## Changes')
$releaseNotes.Add('')
$releaseNotes.AddRange($section)
$releaseNotes.Add('')
$releaseNotes.Add('## Installation')
$releaseNotes.Add('')
$releaseNotes.Add('Unzip the addon into your Godot .NET project so the folder lands at')
$releaseNotes.Add('`addons/noesisgui/`. Add the `Noesis.GUI` and `Noesis.App.Theme`')
$releaseNotes.Add('NuGet packages and `<UseRidGraph>true</UseRidGraph>` to your project,')
$releaseNotes.Add('build it, and enable the plugin. Full instructions are in the README.')

$resolvedOutputPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $repositoryRoot $OutputPath
}
$outputDirectory = Split-Path -Parent $resolvedOutputPath
if (![string]::IsNullOrEmpty($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$content = [string]::Join("`n", $releaseNotes) + "`n"
[System.IO.File]::WriteAllText($resolvedOutputPath, $content, $utf8WithoutBom)

Write-Host "Prepared release notes for $ReleaseTag from CHANGELOG.md."
