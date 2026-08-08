<#
.SYNOPSIS
    Scaffolds a new Big Walk plugin and adds it to the solution.
.EXAMPLE
    .\scripts\new-mod.ps1 -Name BigLegs -Description "Makes your legs longer."
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Name,
    [string]$Description = '',
    [string]$Version = '0.1.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Name -notmatch '^[A-Za-z][A-Za-z0-9_]*$') {
    throw "Name must be alphanumeric and start with a letter (got '$Name')."
}

$repo = Split-Path $PSScriptRoot -Parent
$assembly = "BigWalk.$Name"
$dir = Join-Path $repo "plugins\$assembly"
if (Test-Path $dir) { throw "$dir already exists." }
if (-not $Description) { $Description = "$Name for Big Walk." }

New-Item -ItemType Directory -Force $dir | Out-Null

@"
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <AssemblyName>$assembly</AssemblyName>
    <Version>$Version</Version>
    <Description>$Description</Description>
  </PropertyGroup>

</Project>
"@ | Set-Content (Join-Path $dir "$assembly.csproj") -Encoding UTF8

# References, target framework and analyzer settings all come from
# plugins\Directory.Build.props, so the generated csproj stays this small.
@"
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace BigWalk.$Name;

[BepInPlugin(Guid, "Big Walk — $Name", "$Version")]
public class Plugin : BasePlugin
{
    public const string Guid = "com.bigwalk.$($Name.ToLowerInvariant())";

    internal static BepInEx.Logging.ManualLogSource Trace;

    public override void Load()
    {
        Trace = Log;

        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo("$assembly loaded.");
    }
}
"@ | Set-Content (Join-Path $dir 'Plugin.cs') -Encoding UTF8

@"
# $assembly

$Description

## Install

Install with a Thunderstore mod manager (Gale or Thunderstore Mod Manager), or drop
``$assembly.dll`` into ``BepInEx\plugins\``.
"@ | Set-Content (Join-Path $dir 'README.md') -Encoding UTF8

$sdk = "$env:USERPROFILE\scoop\apps\dotnet-sdk\current"
if (Test-Path $sdk) { $env:DOTNET_ROOT = $sdk; $env:PATH = "$sdk;$env:PATH" }

$sln = Get-ChildItem $repo -Filter '*.slnx' | Select-Object -First 1
if ($sln) { & dotnet sln $sln.FullName add (Join-Path $dir "$assembly.csproj") }

Write-Host ""
Write-Host "Created plugins\$assembly" -ForegroundColor Green
Write-Host "  .\scripts\build.ps1 -Deploy   to build and install it"
