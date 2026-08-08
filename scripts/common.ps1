# Shared helpers for Big Walk mod install/uninstall.
Set-StrictMode -Version Latest

function Get-BigWalkPath {
    <#
      Resolves the Big Walk install directory by reading Steam's libraryfolders.vdf
      and looking for appid 1478500 in each library. Falls back to $env:BIGWALK_PATH.
    #>
    if ($env:BIGWALK_PATH -and (Test-Path $env:BIGWALK_PATH)) { return $env:BIGWALK_PATH }

    $steamRoots = @(
        "${env:ProgramFiles(x86)}\Steam",
        "$env:ProgramFiles\Steam"
    ) | Where-Object { Test-Path $_ }

    foreach ($root in $steamRoots) {
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path $vdf)) { continue }

        # Each library block has a "path" line; appids appear in its "apps" block.
        $libs = Select-String -Path $vdf -Pattern '"path"\s+"(.+?)"' -AllMatches |
                ForEach-Object { $_.Matches.Groups[1].Value -replace '\\\\', '\' }

        foreach ($lib in $libs) {
            $candidate = Join-Path $lib 'steamapps\common\Big Walk'
            if (Test-Path (Join-Path $candidate 'Big Walk.exe')) { return $candidate }
        }
    }
    throw "Could not locate Big Walk. Set BIGWALK_PATH to the folder containing 'Big Walk.exe'."
}

function Assert-GameClosed {
    if (Get-Process -Name 'Big Walk' -ErrorAction SilentlyContinue) {
        throw "Big Walk is running. Close it first - the loader cannot be swapped while the game holds its files."
    }
}

# Everything BepInEx drops into the game folder. Uninstall removes exactly this set,
# which is what keeps a revert clean rather than best-effort.
$script:BepInExArtifacts = @(
    'winhttp.dll',
    'doorstop_config.ini',
    '.doorstop_version',
    'changelog.txt',
    'BepInEx',
    'dotnet'
)
