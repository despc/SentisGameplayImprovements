# Copies the client mod GridPrediction (ClientMods\GridPrediction\) into the game's local mods folder,
# %APPDATA%\SpaceEngineers\Mods\GridPrediction, from where the game publishes it to the workshop (Mods > the mod > Publish).
#   powershell -ExecutionPolicy Bypass -File tools\install-grid-prediction.ps1
# The folder's old scripts are replaced; modinfo.sbmi the game writes on publish (the workshop id) is kept.
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $env:APPDATA 'SpaceEngineers\Mods\GridPrediction'
New-Item -ItemType Directory -Force $target | Out-Null
$scripts = Join-Path $target 'Data\Scripts'
if (Test-Path $scripts) { Remove-Item $scripts -Recurse -Force }
Copy-Item (Join-Path $repo 'ClientMods\GridPrediction\*') $target -Recurse -Force
Write-Host "The mod is in $target"
