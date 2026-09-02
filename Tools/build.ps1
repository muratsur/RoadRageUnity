# Batchmode build wrapper.
#
#   .\Tools\build.ps1                 # Windows player
#   .\Tools\build.ps1 -Target Android
#   .\Tools\build.ps1 -Scenes         # just report what the build would ship
#
# Edit $UnityExe if your editor lives elsewhere. The version is read from
# ProjectSettings/ProjectVersion.txt so this stays correct when the project upgrades.
param(
    [ValidateSet('Windows','Android')] [string]$Target = 'Windows',
    [switch]$Scenes
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = (Select-String -Path "$root\ProjectSettings\ProjectVersion.txt" -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
$UnityExe = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"

if (-not (Test-Path $UnityExe)) {
    Write-Error "Unity $version not found at $UnityExe - edit `$UnityExe in this script."
}

$method = if ($Scenes) { 'ReportSceneList' } elseif ($Target -eq 'Android') { 'BuildAndroid' } else { 'BuildWindows' }
$log = Join-Path $root "Build\$method.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null

Write-Host "Unity $version -> RoadRageCLI.$method"
Write-Host "log: $log"

# -nographics is safe here: these entry points build and report, they do not render.
& $UnityExe -quit -batchmode -nographics -projectPath $root `
    -executeMethod "RoadRage.Editor.RoadRageCLI.$method" -logFile $log

$code = $LASTEXITCODE
Get-Content $log | Select-String -Pattern 'RR_BUILD'
if ($code -ne 0) { Write-Error "Unity exited $code - see $log" }
Write-Host "OK"
