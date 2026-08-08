<#
.SYNOPSIS
    Builds all Big Walk plugins and (optionally) deploys them into the game.
.PARAMETER Deploy
    Copy the built DLLs into BepInEx\plugins afterwards. Requires the game to be closed.
#>
[CmdletBinding()]
param(
    [switch]$Deploy,
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

# The stock C:\Program Files\dotnet ships runtimes but no SDK, so tools resolve there
# and fail with a misleading "framework missing" error. Pin to the scoop SDK.
$sdk = "$env:USERPROFILE\scoop\apps\dotnet-sdk\current"
if (Test-Path $sdk) {
    $env:DOTNET_ROOT = $sdk
    $env:PATH = "$sdk;$env:PATH"
}

$repo = Split-Path $PSScriptRoot -Parent
$game = Get-BigWalkPath

$projects = Get-ChildItem (Join-Path $repo 'plugins') -Recurse -Filter '*.csproj'
if (-not $projects) { throw "No plugin projects found under $repo\plugins." }

foreach ($proj in $projects) {
    Write-Host "Building $($proj.BaseName)..."
    & dotnet build $proj.FullName -c $Configuration -v m /p:GamePath="$game"
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $($proj.Name)." }
}

if ($Deploy) {
    & "$PSScriptRoot\install.ps1" -PluginsOnly
}
