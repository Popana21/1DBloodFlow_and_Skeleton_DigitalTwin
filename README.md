# ECS 1D Blood Flow Visualization

Unity-based visualization framework for 1D blood flow simulation data mapped onto an anatomical skeleton within a digital twin environment.

## Demo Video

[▶ Watch the blood flow visualization demo on YouTube](https://youtu.be/j13sAnd9ink)

## Overview

This project combines:

- `openBF` for running the 1D blood flow simulation
- `Julia` for executing the `openBF` workflow
- `Python` for converting the benchmark YAML model into the `vessels.json` topology contract used by Unity
- `Unity 6` for loading the topology and time-series data, mapping the vascular network to a humanoid skeleton, and rendering the interactive visualization

The repository currently targets the `Boileau 2015 / adan56` benchmark model and a Windows-based workflow.

## Repository Structure

- `Assets/`: Unity assets, scripts, scenes, prefabs, materials, and runtime data contracts
- `Assets/StreamingAssets/vessels.json`: topology contract consumed by Unity
- `Assets/StreamingAssets/LastFiles/`: staged `.last` files consumed by Unity at runtime
- `tools/run_openbf.jl`: Julia entry point that launches the benchmark simulation
- `tools/adan56_yaml_to_json.py`: converts the benchmark YAML into Unity's wrapped `vessels.json` format
- `run_all.ps1`: end-to-end launcher for simulation, staging, conversion, and player startup
- `run_all.bat`: double-click wrapper for `run_all.ps1`

## Requirements

The project currently assumes the following software is available:

- Windows
- [Unity 6.1.5f1](https://unity.com/releases/editor/archive) (`6000.1.5f1`)
- [Julia](https://julialang.org/downloads/platform)
- [Python 3](https://www.python.org/downloads/)
- `PyYAML` for Python
- [openBF](https://github.com/INSIGNEO/openBF)

Optional but recommended:

- Git
- GitHub Desktop or another Git client
- Visual Studio / VS Code for inspecting the Unity-side C# code

## Installation

### 1. Clone or download this repository

Clone the repository to a local folder such as:

```powershell
git clone https://github.com/Popana21/1DBloodFlow_and_Skeleton_DigitalTwin.git
cd 1DBloodFlow_and_Skeleton_DigitalTwin
```

If you downloaded the repository as a ZIP file, extract it to a local folder and open that folder in PowerShell instead.

### 2. Install Unity

Install Unity through Unity Hub and make sure the installed editor version matches the project version:

- `6000.1.5f1`

The Unity version used by the project is recorded in:

- `ProjectSettings/ProjectVersion.txt`

### 3. Install Julia

Install Julia using the official installer or `juliaup`:

- Julia download instructions: https://julialang.org/downloads/platform

After installation, verify that Julia is available:

```powershell
julia --version
```

The PowerShell launcher also tries to auto-detect `julia.exe` under the standard Windows install locations.

### 4. Install Python and PyYAML

Install Python 3 and ensure it is available from PowerShell:

```powershell
python --version
```

Then install `PyYAML`:

```powershell
pip install pyyaml
```

This dependency is required by:

- `tools/adan56_yaml_to_json.py`

### 5. Install openBF

The simulation backend used by this project is `openBF`:

- GitHub repository: https://github.com/INSIGNEO/openBF

Install it according to the instructions in the official repository. The current project setup expects the benchmark model:

- `models/boileau2015/adan56/adan56.yaml`

to be available inside the local `openBF` installation.

## Important Project-Specific Paths

The current launcher scripts use machine-specific Windows paths. Before running the full workflow, review these values in:

- `run_all.ps1`
- `tools/run_openbf.jl`

In particular, check:

- the Unity project root
- the `openBF` benchmark YAML path
- the expected Unity player executable path

The benchmark YAML file is expected to be located inside the local Julia package directory for `openBF`, for example:

```text
C:\Users\<USERNAME>\.julia\packages\openBF\<OPENBF_VERSION>\models\boileau2015\adan56\adan56.yaml
```

The exact path may vary depending on the local Julia package installation and the installed `openBF` version.

Likewise, the Unity player executable is expected either at a fixed path such as:

```text
C:\Users\<USERNAME>\ECS_1DBloodFlowVisualization.exe
```

or the newest `.exe` found under:

```text
Builds\
```

If your local setup differs, update the paths in the scripts before running.

## Opening the Project in Unity

1. Open Unity Hub
2. Choose `Add project`
3. Select the repository folder
4. Open the project with Unity `6000.1.5f1`

When the project opens, Unity will regenerate local folders such as `Library`, `Temp`, and other editor metadata automatically.

## Running the Full Workflow

The repository includes an end-to-end launcher:

- `run_all.ps1`
- `run_all.bat`

This workflow performs the following steps:

1. finds a Julia installation
2. runs the `openBF` benchmark simulation
3. finds the newest results folder
4. copies the generated `.last` files into `Assets/StreamingAssets/LastFiles`
5. converts the benchmark YAML into `Assets/StreamingAssets/vessels.json`
6. launches the Unity player executable

### Option A: Run using PowerShell

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\run_all.ps1
```

### Option B: Run using the batch file

Double-click:

- `run_all.bat`

or run:

```powershell
.\run_all.bat
```

## Manual Run Procedure

If you prefer to run the steps manually, the workflow is:

### 1. Run the benchmark simulation

```powershell
julia .\tools\run_openbf.jl
```

This script calls `openBF` and generates a benchmark results folder containing the `.last` files.

### 2. Convert the benchmark YAML to Unity JSON

```powershell
python .\tools\adan56_yaml_to_json.py "PATH_TO_ADAN56_YAML" ".\Assets\StreamingAssets\vessels.json"
```

This produces the wrapped JSON structure Unity expects:

```json
{
  "vessels": [
    ...
  ]
}
```

### 3. Copy the `.last` files into Unity

Copy the generated `.last` files into:

```text
Assets\StreamingAssets\LastFiles\
```

### 4. Start Unity or the built player

You can then:

- open the project in the Unity editor and run the scene, or
- launch the built executable if one is available

## Notes on the Current Workflow

- The project currently targets the `adan56` benchmark specifically.
- The launcher is Windows-oriented.
- The launcher assumes the benchmark model and Unity build already exist at known local paths.
- The generated simulation results folder `adan56_results/` is runtime data and is usually not necessary to track in Git.
- Unity-generated folders such as `Library/`, `Temp/`, and `Logs/` should not be committed.

## Troubleshooting

### `Julia executable not found automatically`

The launcher could not find `julia.exe`. Either:

- install Julia in a standard Windows location, or
- hardcode the path to `julia.exe` in `run_all.ps1`

### `Missing YAML config`

The configured `openBF` benchmark path does not match your local installation. Update:

- `$YamlConfig` in `run_all.ps1`
- the path in `tools/run_openbf.jl`

### `Missing Unity EXE`

The launcher could not find a built Unity player. Either:

- build the project from Unity, or
- update `$PlayerExe` in `run_all.ps1`

### `Missing dependency: PyYAML`

Install the Python dependency:

```powershell
pip install pyyaml
```

## Citation / Thesis Context

This repository contains the software artifact developed for a master's thesis on integrating 1D blood flow simulation and skeletal anatomy within a digital twin visualization environment.
