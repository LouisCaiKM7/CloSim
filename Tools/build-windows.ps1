<#
    build-windows.ps1 — headless Windows build wrapper for CloSim (online-multiplayer effort).

    WHAT IT DOES
      Invokes the Unity Editor in batch mode and runs CloSimBuild.BuildWindows
      (Assets/Editor/Build/CloSimBuild.cs), producing a Standalone Windows x64
      player under Build/Windows/CloSim.exe.

    ── WHERE TO SET THE UNITY PATH ──────────────────────────────────────────────
      This project uses Unity 2023.2.22f1 (see ProjectSettings/ProjectVersion.txt).
      Point the script at your Unity.exe using ONE of, in priority order:
        1. -UnityPath "<full path to Unity.exe>"       (parameter, wins)
        2. $env:UNITY_PATH = "<full path to Unity.exe>" (environment variable)
        3. Auto-detect the default Unity Hub install for the version below.
      Typical Hub location:
        C:\Program Files\Unity\Hub\Editor\2023.2.22f1\Editor\Unity.exe

    EXACT COMMAND THIS SCRIPT RUNS
      & "<Unity.exe>" -batchmode -quit -nographics `
          -projectPath "<repo root>" `
          -executeMethod CloSimBuild.BuildWindows `
          -logFile "-" `
          -buildOutput "Build/Windows/CloSim.exe"

    USAGE
      pwsh ./Tools/build-windows.ps1
      pwsh ./Tools/build-windows.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\2023.2.22f1\Editor\Unity.exe"
      pwsh ./Tools/build-windows.ps1 -Output "Build/Windows/CloSim.exe" -Development

    EXIT CODES
      Passes through Unity's exit code (0 = success, non-zero = failure).
#>
[CmdletBinding()]
param(
    [string]$UnityPath = $env:UNITY_PATH,
    [string]$Output = "Build/Windows/CloSim.exe",
    [switch]$Development
)

$ErrorActionPreference = "Stop"
$UnityVersion = "2023.2.22f1"

# Repo root = parent of this Tools/ folder.
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-UnityPath {
    param([string]$Explicit)

    if ($Explicit -and (Test-Path $Explicit)) { return (Resolve-Path $Explicit).Path }
    if ($Explicit) { throw "UnityPath '$Explicit' does not exist. Fix -UnityPath or `$env:UNITY_PATH." }

    # Auto-detect the default Unity Hub install for the project's version.
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe",
        "C:\Program Files\Unity\Editor\Unity.exe",
        "${env:ProgramFiles}\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return (Resolve-Path $c).Path } }

    throw @"
Could not locate Unity $UnityVersion.
Set the Unity path one of these ways and re-run:
  1. pwsh ./Tools/build-windows.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
  2. `$env:UNITY_PATH = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
Then run again.
"@
}

$unity = Resolve-UnityPath -Explicit $UnityPath
Write-Host "[build-windows] Unity   : $unity"
Write-Host "[build-windows] Project : $RepoRoot"
Write-Host "[build-windows] Output  : $Output"

$unityArgs = @(
    "-batchmode", "-quit", "-nographics",
    "-projectPath", $RepoRoot,
    "-executeMethod", "CloSimBuild.BuildWindows",
    "-logFile", "-",
    "-buildOutput", $Output
)
if ($Development) { $unityArgs += "-development" }

& $unity @unityArgs
$code = $LASTEXITCODE
if ($code -ne 0) {
    Write-Error "[build-windows] Unity exited with code $code"
    exit $code
}
Write-Host "[build-windows] Done. Player at: $(Join-Path $RepoRoot $Output)"
exit 0
