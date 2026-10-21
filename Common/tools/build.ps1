# Builds every plugin project in the repo into common\artifacts\.
# Requires dotnet SDK (netstandard2.1 target).

$ErrorActionPreference = "Stop"

$repo       = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # repo root
$common     = Join-Path $repo "common"
$artifacts  = Join-Path $common "artifacts"

# Game paths (env-overridable).
$env:PEAK_GAME          = if ($env:PEAK_GAME)          { $env:PEAK_GAME }          else { "C:\Users\ntu\game\Steam\steamapps\common\PEAK" }
$env:PEAK_MANAGED       = if ($env:PEAK_MANAGED)       { $env:PEAK_MANAGED }       else { Join-Path $env:PEAK_GAME "PEAK_Data\Managed" }
$env:PEAK_BEPINEX_CORE  = if ($env:PEAK_BEPINEX_CORE)  { $env:PEAK_BEPINEX_CORE }  else { Join-Path $env:PEAK_GAME "BepInEx\core" }

if (-not (Test-Path $env:PEAK_MANAGED)) { throw "PEAK_MANAGED not found: $env:PEAK_MANAGED" }
if (-not (Test-Path $env:PEAK_BEPINEX_CORE)) { throw "PEAK_BEPINEX_CORE not found: $env:PEAK_BEPINEX_CORE" }

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

# Discover all *.csproj under the repo except the common library (it is pulled in as a reference).
$projects = Get-ChildItem -Path $repo -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notlike "*\common\PeakModding.Common\*" }

if (-not $projects) { Write-Host "No plugin projects found."; exit 0 }

foreach ($proj in $projects) {
    Write-Host "==> Building $($proj.FullName)"
    dotnet build $proj.FullName -c Release -v minimal
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed: $($proj.FullName)" }

    $name = $proj.BaseName
    $outDir = Join-Path $proj.DirectoryName "bin\Release"
    $outDll = Join-Path $outDir "$name.dll"

    # Copy plugin + its referenced deps (any dlls the plugin emitted) into artifacts.
    $dest = Join-Path $artifacts "$name"
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Get-ChildItem -Path $outDir -Filter *.dll | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $dest $_.Name) -Force
    }
    Write-Host "    ^ artifacts -> $dest"
}

Write-Host ""
Write-Host "Build complete. Artifacts in: $artifacts"
Write-Host "Deploy with: powershell -ExecutionPolicy Bypass -File common\tools\deploy.ps1"