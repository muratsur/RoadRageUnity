<#
Adds a model downloaded from Hyper3D Rodin to the Greenwood test spot.

Rodin's download is a zip holding another zip, with two FBX files (lod.fbx, bare,
and lod_basic_pbr.fbx, textured) and loose texture PNGs. This unpacks it, keeps
the textured FBX under the name you give, and puts it with its textures in its
own folder under Assets/Resources/TestAssets (git-ignored, never committed).

The name carries the settings the game reads:
  _h<metres>  height it stands at (default 9)     e.g. _h1.5 for a hatchback
  _r<degrees> turn if it faces the wrong way      e.g. _r180

Usage (from the project folder):
  .\Tools\Rodin\add_rodin_model.ps1 "$env:USERPROFILE\Downloads\<download>.zip" car_hatch_cyan_h1.5
  .\Tools\Rodin\add_rodin_model.ps1 -Latest car_hatch_cyan_h1.5     # newest zip in Downloads

Then in Unity: Road Rage > Set Up Rodin Models, and play Greenwood.
#>
param(
    [Parameter(Position = 0)] [string] $Zip,
    [Parameter(Position = 1)] [string] $Name,
    [switch] $Latest
)
$ErrorActionPreference = "Stop"

if ($Latest) {
    if (-not $Name) { $Name = $Zip }
    $Zip = (Get-ChildItem "$env:USERPROFILE\Downloads\*.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
    Write-Host "Newest download: $Zip"
}
if (-not $Zip -or -not (Test-Path $Zip)) { throw "Zip not found: '$Zip'" }
if (-not $Name) { throw "Give the model a name, e.g. car_hatch_cyan_h1.5" }

$project = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$target = Join-Path $project "Assets\Resources\TestAssets\$Name"
$work = Join-Path $env:TEMP ("rodin_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

try {
    Expand-Archive $Zip -DestinationPath $work -Force
    # Zips inside the zip, however deep.
    for ($round = 0; $round -lt 3; $round++) {
        $inner = Get-ChildItem $work -Recurse -Filter *.zip
        if (-not $inner) { break }
        foreach ($z in $inner) {
            Expand-Archive $z.FullName -DestinationPath $z.DirectoryName -Force
            Remove-Item $z.FullName
        }
    }

    $fbx = Get-ChildItem $work -Recurse -Filter *.fbx | Sort-Object { if ($_.Name -match "pbr") { 0 } else { 1 } }, Length | Select-Object -First 1
    $glb = Get-ChildItem $work -Recurse -Filter *.glb | Select-Object -First 1
    $model = if ($fbx) { $fbx } else { $glb }
    if (-not $model) { throw "No .fbx or .glb inside $Zip" }

    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item $model.FullName (Join-Path $target ($Name + $model.Extension))
    Get-ChildItem $model.DirectoryName -File | Where-Object { $_.Extension -match "^\.(png|jpg|jpeg|tga)$" } |
        Copy-Item -Destination $target

    Write-Host ""
    Write-Host "Added $Name from $($model.Name):"
    Get-ChildItem $target | ForEach-Object { Write-Host "  $($_.Name)" }
    Write-Host ""
    Write-Host "Next: in Unity, Road Rage > Set Up Rodin Models, then play Greenwood."
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
