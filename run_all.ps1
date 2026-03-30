$ErrorActionPreference = "Stop"

# End-to-end launcher:
# 1) run the OpenBF benchmark, 2) stage solver outputs for Unity, 3) launch the player.

# -----------------------------
# Auto-detect Julia executable
# -----------------------------
$JuliaExe = $null
$possibleJuliaRoots = @(
    "$env:LOCALAPPDATA\Programs",
    "$env:PROGRAMFILES",
    "$env:PROGRAMFILES(x86)"
)

foreach ($root in $possibleJuliaRoots) {
    if (-not (Test-Path $root)) { continue }
    $found = Get-ChildItem -Path $root -Recurse -Filter "julia.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\bin\\julia.exe$" } |
        Select-Object -First 1
    if ($found) { $JuliaExe = $found.FullName; break }
}

if (-not $JuliaExe) {
    throw "Julia executable not found automatically. Please locate julia.exe and hardcode its path in this script."
}
Write-Host "Found Julia: $JuliaExe"

# -----------------------------
# Unity project destinations (fixed requirements)
# -----------------------------
$UnityProject    = "C:\Users\User\ECS_1DBloodFlowVisualization"
$StreamingAssets = Join-Path $UnityProject "Assets\StreamingAssets"
$LastDest        = Join-Path $StreamingAssets "LastFiles"

# -----------------------------
# openBF config
# -----------------------------
$YamlConfig = "C:\Users\User\.julia\packages\openBF\tP0gP\models\boileau2015\adan56\adan56.yaml"
$ModelRoot  = Split-Path $YamlConfig -Parent

# -----------------------------
# Tools
# -----------------------------
$JuliaScript    = Join-Path $UnityProject "tools\run_openbf.jl"

# IMPORTANT:
# Use the WRAPPED vessels converter (outputs {"vessels":[...]}).
# Make sure you have this script at:
# C:\Users\User\ECS_1DBloodFlowVisualization\tools\yaml_to_vessels_json.py
$YamlToVessels  = Join-Path $UnityProject "tools\adan56_yaml_to_json.py"

# -----------------------------
# JSON output for Unity (Unity expects this filename)
# -----------------------------
$VesselsJson = Join-Path $StreamingAssets "vessels.json"

# -----------------------------
# Unity Player EXE (set correct path OR auto-detect)
# -----------------------------
$PlayerExe  = "C:\Users\User\ECS_1DBloodFlowVisualization.exe"
if (-not (Test-Path $PlayerExe)) {
    $buildRoot = Join-Path $UnityProject "Builds"
    if (Test-Path $buildRoot) {
        $autoExe = Get-ChildItem -Path $buildRoot -Recurse -Filter "*.exe" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notmatch "UnityCrashHandler|UnityBugReporter" } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($autoExe) { $PlayerExe = $autoExe.FullName }
    }
}
if (-not (Test-Path $PlayerExe)) { throw "Missing Unity EXE: $PlayerExe" }
Write-Host "Found Unity EXE: $PlayerExe"

# -----------------------------
# Sanity checks
# -----------------------------
if (-not (Test-Path $JuliaScript))   { throw "Missing Julia script: $JuliaScript" }
if (-not (Test-Path $YamlToVessels)) { throw "Missing vessels converter: $YamlToVessels" }
if (-not (Test-Path $YamlConfig))    { throw "Missing YAML config: $YamlConfig" }

# Ensure folders exist
New-Item -ItemType Directory -Force -Path $StreamingAssets | Out-Null
New-Item -ItemType Directory -Force -Path $LastDest | Out-Null

# -----------------------------
# CLEANUP: remove old JSON + old .last
# -----------------------------
Write-Host "0) Clearing Unity StreamingAssets JSON files..."
Get-ChildItem -Path $StreamingAssets -Filter "*.json" -File -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "1) Clearing Unity StreamingAssets\LastFiles..."
Get-ChildItem -Path $LastDest -Filter "*.last" -File -ErrorAction SilentlyContinue | Remove-Item -Force

# -----------------------------
# Run Julia from a known working directory
# -----------------------------
$JuliaWorkingDir = $UnityProject

Write-Host "2) Running openBF simulation..."
Push-Location $JuliaWorkingDir
& $JuliaExe --startup-file=no --history-file=no $JuliaScript
Pop-Location

# -----------------------------
# Find results folder: newest *_results near likely roots
# -----------------------------
Write-Host "3) Locating newest results folder..."

$searchRoots = @(
    $JuliaWorkingDir,
    $ModelRoot
) | Select-Object -Unique

$resultsDirs = @()
foreach ($root in $searchRoots) {
    if (-not (Test-Path $root)) { continue }
    $dirs = Get-ChildItem -Path $root -Directory -Recurse -Depth 3 -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "*_results" }
    if ($dirs) { $resultsDirs += $dirs }
}

$resultsDirs = $resultsDirs | Sort-Object LastWriteTime -Descending | Select-Object -Unique

if ($resultsDirs.Count -eq 0) {
    throw "Could not find any '*_results' folders under: $($searchRoots -join ', ')"
}

$newestResults = $resultsDirs[0].FullName
Write-Host "   Using results folder: $newestResults"

# -----------------------------
# Copy .last files into Unity
# -----------------------------
Write-Host "4) Collecting .last files from results folder..."
$runLastFiles = Get-ChildItem -Path $newestResults -Filter "*.last" -Recurse -File -ErrorAction SilentlyContinue

if ($runLastFiles.Count -eq 0) {
    throw "No .last files found inside: $newestResults"
}

Write-Host "   Found $($runLastFiles.Count) .last files"

Write-Host "5) Copying .last files into Unity..."
$runLastFiles | Copy-Item -Destination $LastDest -Force

# -----------------------------
# Convert YAML -> vessels.json (WRAPPED format)
# -----------------------------
Write-Host "6) Converting YAML -> vessels.json into StreamingAssets..."
python $YamlToVessels $YamlConfig $VesselsJson

if (-not (Test-Path $VesselsJson)) {
    throw "vessels.json was not created at: $VesselsJson"
}

Write-Host "   Created: $VesselsJson"

# -----------------------------
# Launch Unity Player
# -----------------------------
Write-Host "7) Launching Unity Player..."
Start-Process -FilePath $PlayerExe -WorkingDirectory (Split-Path $PlayerExe)

Write-Host "Done."
