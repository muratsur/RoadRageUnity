# Downloads the Baden-Wuerttemberg open geodata (LGL) along the B500 for Road Rage's
# real-terrain pipeline: DGM1 (1 m ground heights) and, optionally, DOP20 (20 cm
# aerial photos). Licence: Datenlizenz Deutschland - Namensnennung 2.0
# (commercial use allowed). Credit in the game: "Datenquelle: LGL, www.lgl-bw.de".
#
# The portal (https://opengeodata.lgl-bw.de) hands out one zip per 2 km x 2 km tile.
# Its link format is not documented, so give this script ONE real link of each kind,
# copied from the portal (right-click the tile's download button > Copy link). The
# script reads the tile coordinates out of that link ("..._32_<east km>_<north km>...")
# and downloads every tile the B500 corridor (route +/- 400 m) touches.
#
# The data stays OUTSIDE the Unity project (it is gigabytes); nothing here is committed.
#
# Usage (PowerShell, from the repository root):
#   powershell -ExecutionPolicy Bypass -File .\Tools\Terrain\download_lgl_b500.ps1 -DgmExample "<copied DGM1 link>"
#   ... -DgmExample "<link>" -DopExample "<copied DOP20 link>"   (aerial photos too; several GB)
param(
    [Parameter(Mandatory = $true)][string]$DgmExample,
    [string]$DopExample = "",
    [string]$OutDir = (Join-Path (Split-Path -Parent (Get-Location)) "RoadRageData\lgl")
)

# The 1 km cells (lower-left corner, km, UTM zone 32) within 400 m of the route in
# Tools/Terrain/b500.kml. The portal's 2 km tiles may start on even or odd kilometres
# in either direction; the example link tells which, and the cells are grouped into
# tiles on that grid.
$Cells = @(
    "439_5382", "439_5383", "440_5382", "440_5383", "440_5384", "440_5385", "440_5386", "440_5387", "441_5374", "441_5375",
    "441_5376", "441_5377", "441_5378", "441_5379", "441_5380", "441_5381", "441_5382", "441_5383", "441_5385", "441_5386",
    "441_5387", "441_5388", "442_5371", "442_5372", "442_5373", "442_5374", "442_5375", "442_5376", "442_5377", "442_5378",
    "442_5379", "442_5380", "442_5381", "442_5382", "442_5383", "442_5387", "442_5388", "442_5389", "442_5392", "442_5393",
    "442_5395", "442_5396", "442_5401", "442_5402", "443_5371", "443_5372", "443_5373", "443_5374", "443_5375", "443_5376",
    "443_5378", "443_5379", "443_5380", "443_5381", "443_5387", "443_5388", "443_5389", "443_5390", "443_5391", "443_5392",
    "443_5393", "443_5394", "443_5395", "443_5396", "443_5397", "443_5399", "443_5400", "443_5401", "443_5402", "444_5371",
    "444_5372", "444_5389", "444_5390", "444_5391", "444_5392", "444_5393", "444_5394", "444_5395", "444_5396", "444_5397",
    "444_5398", "444_5399", "444_5400", "444_5401", "445_5369", "445_5370", "445_5371", "445_5372", "445_5396", "445_5397",
    "445_5398", "445_5399", "445_5400", "446_5369", "446_5370", "446_5371", "447_5368", "447_5369", "447_5370", "448_5368",
    "448_5369", "449_5367", "449_5368", "449_5369", "450_5367", "450_5368", "450_5369", "451_5367", "451_5368", "452_5367",
    "452_5368", "453_5366", "453_5367", "453_5368", "454_5366", "454_5367", "455_5366", "455_5367", "455_5368", "456_5366",
    "456_5367", "456_5368", "457_5365", "457_5366", "457_5367", "457_5368", "458_5361", "458_5362", "458_5363", "458_5364",
    "458_5365", "458_5366", "458_5367", "458_5368", "459_5360", "459_5361", "459_5362", "459_5363", "459_5364", "459_5365",
    "459_5366", "459_5367", "460_5358", "460_5359", "460_5360", "460_5361", "460_5362", "461_5355", "461_5356", "461_5357",
    "461_5358", "461_5359", "461_5360", "461_5361", "462_5354", "462_5355", "462_5356", "462_5357", "462_5358", "463_5354",
    "463_5355", "463_5356"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is many times faster without it

function Get-Tiles([string]$example) {
    $m = [regex]::Match($example, "_32_(\d{3})_(\d{4})")
    if (-not $m.Success) { throw "No '_32_<east>_<north>' tile coordinates in the link: $example" }
    $pe = [int]$m.Groups[1].Value % 2
    $pn = [int]$m.Groups[2].Value % 2
    $tiles = [System.Collections.Generic.SortedSet[string]]::new()
    foreach ($c in $Cells) {
        $e, $n = $c.Split("_") | ForEach-Object { [int]$_ }
        $te = $e - ((($e - $pe) % 2) + 2) % 2
        $tn = $n - ((($n - $pn) % 2) + 2) % 2
        [void]$tiles.Add("$($te)_$($tn)")
    }
    Write-Host "$($tiles.Count) tiles of 2 km along the B500"
    return $tiles
}

function Get-Dataset([string]$name, [string]$example) {
    $dir = Join-Path $OutDir $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $tiles = Get-Tiles $example
    $pattern = "_32_\d{3}_\d{4}"
    $done = 0; $skipped = 0; $failed = @()
    foreach ($t in $tiles) {
        $e, $n = $t.Split("_")
        $url = [regex]::Replace($example, $pattern, "_32_$($e)_$($n)")
        # Named by tile, not from the link: a link that carries the tile in its query
        # string would give every file the same name.
        $ext = [System.IO.Path]::GetExtension(([uri]$url).AbsolutePath)
        if ($ext -eq "") { $ext = ".zip" }
        $file = Join-Path $dir "$($name)_32_$($t)$ext"
        if (Test-Path $file) { $skipped++; continue }
        try {
            Write-Host "[$name] $t ..." -NoNewline
            Invoke-WebRequest -Uri $url -OutFile "$file.part" -UseBasicParsing
            Move-Item "$file.part" $file -Force
            Write-Host " ok"
            $done++
        } catch {
            Write-Host " failed ($($_.Exception.Message))"
            Remove-Item "$file.part" -ErrorAction SilentlyContinue
            $failed += $t
        }
    }
    Write-Host "[$name] $done downloaded, $skipped already there, $($failed.Count) failed -> $dir"
    if ($failed.Count -gt 0) {
        Write-Host "[$name] failed tiles (outside the state, or the grid differs): $($failed -join ', ')"
    }
}

Write-Host "Saving to $OutDir"
Get-Dataset "dgm1" $DgmExample
if ($DopExample -ne "") { Get-Dataset "dop20" $DopExample }
else { Write-Host "No -DopExample given: aerial photos skipped (DGM1 is enough to start)." }
