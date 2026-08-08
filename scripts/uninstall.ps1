<#
.SYNOPSIS
    Removes BepInEx and all Big Walk mods, restoring a vanilla install.
.PARAMETER KeepConfig
    Preserve BepInEx\config so your settings survive a reinstall.
.PARAMETER KeepCache
    Preserve BepInEx\interop and the cache, so the next launch is fast instead of
    regenerating interop assemblies (which takes several minutes).
#>
[CmdletBinding()]
param(
    [switch]$KeepConfig,
    [switch]$KeepCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$game = Get-BigWalkPath
Assert-GameClosed

$stash = @{}
foreach ($keep in @(
    @{ On = $KeepConfig; Rel = 'BepInEx\config' },
    @{ On = $KeepCache;  Rel = 'BepInEx\interop' },
    @{ On = $KeepCache;  Rel = 'BepInEx\cache' }
)) {
    $src = Join-Path $game $keep.Rel
    if ($keep.On -and (Test-Path $src)) {
        $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("bigwalk_" + [System.IO.Path]::GetRandomFileName())
        Move-Item $src $tmp
        $stash[$keep.Rel] = $tmp
    }
}

$removed = 0
foreach ($artifact in $script:BepInExArtifacts) {
    $target = Join-Path $game $artifact
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
        Write-Host "  removed $artifact"
        $removed++
    }
}

foreach ($rel in $stash.Keys) {
    $dest = Join-Path $game $rel
    New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
    Move-Item $stash[$rel] $dest
    Write-Host "  kept    $rel"
}

if ($removed -eq 0) {
    Write-Host "Nothing to remove - install is already vanilla."
} else {
    Write-Host ""
    Write-Host "Big Walk restored to vanilla." -ForegroundColor Green
}
