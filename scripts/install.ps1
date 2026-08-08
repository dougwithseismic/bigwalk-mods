<#
.SYNOPSIS
    Installs BepInEx (IL2CPP) and the Big Walk plugins into the game folder.
.PARAMETER PluginsOnly
    Skip the BepInEx loader and only refresh built plugin DLLs. Use this for the
    normal edit-build-test loop once the loader is already in place.
#>
[CmdletBinding()]
param(
    [switch]$PluginsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$repo = Split-Path $PSScriptRoot -Parent
$game = Get-BigWalkPath
Assert-GameClosed

Write-Host "Game folder: $game"

if (-not $PluginsOnly) {
    # Prefer the Thunderstore pack: it is what every Big Walk mod depends on and
    # what users get from their mod manager, so we develop against the same build.
    # The raw bleeding-edge zip is the fallback.
    $tsPack = Get-ChildItem (Join-Path $repo 'vendor') -Filter 'BepInExPack_IL2CPP-*.zip' -ErrorAction SilentlyContinue |
              Sort-Object Name -Descending | Select-Object -First 1
    $bePack = Join-Path $repo 'vendor\BepInEx-IL2CPP-be785.zip'

    Write-Host "Installing BepInEx loader..."
    # Expand-Archive over an existing tree leaves stale files behind, so the
    # uninstall runs first to guarantee we land on a known-clean state.
    & "$PSScriptRoot\uninstall.ps1" -KeepConfig

    if ($tsPack) {
        # Thunderstore packages nest the payload under BepInExPack\ alongside
        # manifest.json and icon.png, none of which belong in the game folder.
        $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("bepx_" + [System.IO.Path]::GetRandomFileName())
        try {
            Expand-Archive -Path $tsPack.FullName -DestinationPath $staging -Force
            $payload = Join-Path $staging 'BepInExPack'
            if (-not (Test-Path $payload)) { throw "Unexpected pack layout in $($tsPack.Name) - no BepInExPack folder." }
            Copy-Item (Join-Path $payload '*') $game -Recurse -Force
            Write-Host "  Installed $($tsPack.BaseName)."
        } finally {
            Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
        }
    } elseif (Test-Path $bePack) {
        Expand-Archive -Path $bePack -DestinationPath $game -Force
        Write-Host "  Installed bleeding-edge BepInEx (no Thunderstore pack in vendor\)."
    } else {
        throw "No BepInEx pack found in $repo\vendor."
    }
}

$pluginDir = Join-Path $game 'BepInEx\plugins'
New-Item -ItemType Directory -Force $pluginDir | Out-Null

$built = Get-ChildItem (Join-Path $repo 'plugins') -Recurse -Filter '*.dll' -ErrorAction SilentlyContinue |
         Where-Object { $_.FullName -match '\\bin\\' -and $_.FullName -notmatch '\\obj\\' }

# Drop our previously-deployed DLLs first. Renaming or merging a plugin otherwise
# leaves the old assembly behind, and BepInEx happily loads both - which shows up
# as duplicate menus rather than as an obvious error.
$ours = $built.Name
Get-ChildItem $pluginDir -Filter 'BigWalk.*.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notin $ours } |
    ForEach-Object {
        Remove-Item $_.FullName -Force
        Write-Host "  stale  -> removed $($_.Name)"
    }

if ($built) {
    foreach ($dll in $built) {
        Copy-Item $dll.FullName $pluginDir -Force
        Write-Host "  plugin -> $($dll.Name)"
    }
} else {
    Write-Host "  (no built plugins yet - run scripts\build.ps1 after the first game launch)"
}

Write-Host ""
Write-Host "Done. Launch Big Walk." -ForegroundColor Green
Write-Host "First launch takes several minutes: BepInEx generates IL2CPP interop assemblies."
Write-Host "Log: $game\BepInEx\LogOutput.log"
