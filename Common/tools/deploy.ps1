# Copies built plugin artifacts from common\artifacts\ into the game's BepInEx\plugins, each in its
# own subfolder (BepInEx 5 scans plugin subfolders for *Plugin.dll).
# Requires that build.ps1 has been run first.

$ErrorActionPreference = "Stop"

$repo       = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # repo root
$common     = Join-Path $repo "common"
$artifacts  = Join-Path $common "artifacts"

$env:PEAK_GAME   = if ($env:PEAK_GAME)   { $env:PEAK_GAME }   else { "C:\Users\ntu\game\Steam\steamapps\common\PEAK" }
$pluginsDir = Join-Path $env:PEAK_GAME "BepInEx\plugins"

if (-not (Test-Path $artifacts)) { throw "No artifacts found ($artifacts). Run common\tools\build.ps1 first." }
if (-not (Test-Path $pluginsDir)) { throw "Game plugins dir not found: $pluginsDir" }

$deployed = @()

foreach ($modDir in Get-ChildItem -Path $artifacts -Directory) {
    $modName = $modDir.Name
    $target = Join-Path $pluginsDir $modName
    # Clean first so removed dlls don't linger (e.g. when a mod drops a shared dll).
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem -Path $modDir.FullName -Filter *.dll | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $target $_.Name) -Force
    }
    $deployed += "$modName -> $target"
}

Write-Host "Deployed plugins:"
$deployed | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "Done. Restart the game to load the mod(s). Logs: set Logging.Console.Enabled=true in BepInEx\config\BepInEx.cfg"