# Generates assets/icon.ico (multi-size) and assets/icon.png from code.
# Usage: powershell -ExecutionPolicy Bypass -File build/make-icon.ps1
param([string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'))

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $k = $s / 256.0

    # Squircle background with a soft blue gradient
    $m = 10 * $k
    $bg = New-RoundedPath $m $m ($s - 2 * $m) ($s - 2 * $m) (58 * $k)
    $p0 = New-Object System.Drawing.PointF 0, 0
    $p1 = New-Object System.Drawing.PointF 0, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $p0, $p1, ([System.Drawing.Color]::FromArgb(255, 100, 210, 255)), ([System.Drawing.Color]::FromArgb(255, 0, 105, 235))
    $g.FillPath($brush, $bg)

    # Folder: back tab + back sheet (translucent) and front sheet (white)
    $white70 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 200, 232, 255))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $tab = New-RoundedPath (54 * $k) (66 * $k) (70 * $k) (36 * $k) (11 * $k)
    $back = New-RoundedPath (54 * $k) (80 * $k) (148 * $k) (104 * $k) (14 * $k)
    $g.FillPath($white70, $tab)
    $g.FillPath($white70, $back)
    $front = New-RoundedPath (54 * $k) (102 * $k) (148 * $k) (92 * $k) (14 * $k)
    $g.FillPath($white, $front)

    # Accent line on the folder front
    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 122, 255)), ([single](9 * $k))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawLine($pen, (80 * $k), (150 * $k), (176 * $k), (150 * $k))
        $pen.Dispose()
    }
    $g.Dispose()
    return $bmp
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $b = New-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    $pngs += , ($ms.ToArray())
}

$icoPath = Join-Path $OutDir 'icon.ico'
$fs = [System.IO.File]::Create($icoPath)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

$big = New-Icon 512
$big.Save((Join-Path $OutDir 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
Write-Host "Icon written to $OutDir"

