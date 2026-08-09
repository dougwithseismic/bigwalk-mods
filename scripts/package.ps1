<#
.SYNOPSIS
    Builds Thunderstore-ready zips for each plugin, under dist\.
.DESCRIPTION
    A Thunderstore package is a flat zip containing manifest.json, icon.png (exactly
    256x256), README.md, and the mod payload laid out as it should land in the game
    folder - so plugin DLLs go under BepInEx\plugins\.
.PARAMETER Author
    Thunderstore team/author name that will own the package.
.PARAMETER Only
    Package just the plugins whose project name matches this wildcard, so work in
    progress on one plugin does not block releasing another.
#>
[CmdletBinding()]
param(
    [string]$Author = 'n0__name',
    [string]$Configuration = 'Release',
    [string]$WebsiteUrl = 'https://github.com/dougwithseismic/bigwalk-mods',
    [string]$Only
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$repo = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

& "$PSScriptRoot\build.ps1" -Configuration $Configuration -Only $Only

# The big-walk community's loader package. Mod managers read this and install the
# loader for the user, so it must be the exact "Team-Package-1.2.3" form.
#
# NOTE: this is 6.0.755, which is what users actually get from the mod manager.
# Our local dev install is be.785. Test against 755 before publishing rather than
# assuming 30 bleeding-edge builds apart are interchangeable.
$dependencies = @('BepInEx-BepInExPack_IL2CPP-6.0.755')

$projects = Get-ChildItem (Join-Path $repo 'plugins') -Recurse -Filter '*.csproj'
if ($Only) { $projects = $projects | Where-Object { $_.BaseName -like "*$Only*" } }

foreach ($proj in $projects) {
    [xml]$xml = Get-Content $proj.FullName
    $props = $xml.Project.PropertyGroup

    $assembly = ($props.AssemblyName    | Where-Object { $_ }) -as [string]
    $version  = ($props.Version         | Where-Object { $_ }) -as [string]
    $desc     = ($props.Description     | Where-Object { $_ }) -as [string]
    # Looked up by XPath rather than property access: most plugins do not define it,
    # and Set-StrictMode makes a missing property a hard error.
    $pkgNode  = $xml.SelectSingleNode('//PackageName')
    $pkg      = if ($pkgNode) { $pkgNode.InnerText.Trim() } else { $null }

    if (-not $assembly) { Write-Warning "Skipping $($proj.Name): no AssemblyName."; continue }
    if (-not $version)  { $version = '0.1.0' }
    if (-not $desc)     { $desc = "$assembly for Big Walk." }

    # A plugin may publish under a different name than its assembly - the assembly
    # name is baked into the DLL and the config file path, so it is not free to
    # change, while the storefront name is chosen for readability.
    # Thunderstore package names allow only alphanumerics and underscores.
    if (-not $pkg) { $pkg = $assembly }
    $pkgName = $pkg -replace '[^a-zA-Z0-9_]', '_'

    $stage = Join-Path $dist "stage\$pkgName"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force "$stage\BepInEx\plugins" | Out-Null

    $dll = Join-Path $proj.Directory "bin\$Configuration\$assembly.dll"
    if (-not (Test-Path $dll)) { throw "Built DLL missing: $dll" }
    Copy-Item $dll "$stage\BepInEx\plugins\" -Force

    # Thunderstore truncates descriptions over 250 chars.
    if ($desc.Length -gt 250) { $desc = $desc.Substring(0, 250) }

    [ordered]@{
        name             = $pkgName
        version_number   = $version
        website_url      = $WebsiteUrl
        description      = $desc
        dependencies     = $dependencies
    } | ConvertTo-Json -Depth 4 | Set-Content "$stage\manifest.json" -Encoding UTF8

    $readme = Join-Path $proj.Directory 'README.md'
    if (Test-Path $readme) {
        Copy-Item $readme "$stage\README.md" -Force
    } else {
        "# $assembly`n`n$desc`n" | Set-Content "$stage\README.md" -Encoding UTF8
    }

    # Thunderstore renders CHANGELOG.md as its own tab when the package includes one.
    $changelog = Join-Path $proj.Directory 'CHANGELOG.md'
    if (Test-Path $changelog) { Copy-Item $changelog "$stage\CHANGELOG.md" -Force }

    # Keep an optional UI screenshot alongside the README for repository mirrors
    # and release tooling that can serve package assets directly.
    $screenshot = Join-Path $proj.Directory 'screenshot.png'
    if (Test-Path $screenshot) { Copy-Item $screenshot "$stage\screenshot.png" -Force }

    # Ship real art when a plugin provides icon.png; fall back to a generated placeholder.
    $ownIcon = Join-Path $proj.Directory 'icon.png'
    if (Test-Path $ownIcon) {
        Copy-Item $ownIcon "$stage\icon.png" -Force
    } else {
        $motif = if ($assembly -match 'Skip') { 'skip' } else { 'walk' }
        & python "$PSScriptRoot\make-icon.py" ($assembly -replace '^BigWalk\.', '') "$stage\icon.png" $motif
        if ($LASTEXITCODE -ne 0) { throw "Icon generation failed for $assembly." }
    }

    $zip = Join-Path $dist "$pkgName-$version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$stage\*" -DestinationPath $zip
    Write-Host "packaged -> $zip" -ForegroundColor Green
}

Remove-Item (Join-Path $dist 'stage') -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host "Upload the zips in dist\ at https://thunderstore.io/ (author: $Author)."
