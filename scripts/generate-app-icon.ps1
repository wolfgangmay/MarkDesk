# Generates MarkDesk application icon (Assets/App.ico) with embedded multi-size PNGs.
# Design: blue gradient rounded square, white bold "M", three text lines below
# (Markdown editor + rendering metaphor). Pure GDI+, no external assets.
param(
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $root 'src\MarkDesk\Assets'
$icoPath = Join-Path $assetsDir 'App.ico'
$previewPath = Join-Path $assetsDir 'App-preview.png'

function Draw-IconCanvas([int]$size, [double]$scale) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [System.Drawing.RectangleF]::new(0, 0, $size, $size)

    # Background: rounded rect with diagonal gradient
    $radius = 56 * $scale
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $r,
        [System.Drawing.Color]::FromArgb(255, 36, 132, 219),
        [System.Drawing.Color]::FromArgb(255, 10, 74, 164),
        45.0)
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # White bold "M" (cap-height box)
    $font = [System.Drawing.Font]::new('Segoe UI', 150 * $scale, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $text = 'M'
    $sf = [System.Drawing.StringFormat]::new()
    $sf.Alignment = 'Center'
    $sf.LineAlignment = 'Center'
    $mRect = [System.Drawing.RectangleF]::new(0, -18 * $scale, $size, 150 * $scale)
    $g.DrawString($text, $font, [System.Drawing.Brushes]::White, $mRect, $sf)
    $font.Dispose()

    # Three text lines below (shorter each)
    $lineBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $lineBrush2 = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(200, 255, 255, 255))
    $lineBrush3 = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(140, 255, 255, 255))
    $cx = $size / 2
    $sy = 152 * $scale
    $lh = 14 * $scale
    $gap = 8 * $scale
    $widths = @((96 * $scale), (72 * $scale), (48 * $scale))
    $brushes = @($lineBrush, $lineBrush2, $lineBrush3)
    for ($i = 0; $i -lt 3; $i++) {
        $w = $widths[$i]
        $h = $lh
        $y = $sy + $i * ($lh + $gap)
        $rr = $h / 2
        $lpath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $lpath.AddArc($cx - $w / 2, $y, $h, $h, 90, 180)
        $lpath.AddArc($cx + $w / 2 - $h, $y, $h, $h, 270, 180)
        $lpath.CloseFigure()
        $g.FillPath($brushes[$i], $lpath)
        $lpath.Dispose()
    }

    $g.Dispose()
    $bmp
}

# Preview at full res for visual check
$bmp = Draw-IconCanvas 256 1.0
$bmp.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "preview -> $previewPath"

if (-not $Preview) {
    # Full ICO: embed PNG of each size (256 stored as 0)
    $sizes = @(256, 128, 64, 48, 32, 24, 16)
    $pngs = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($s in $sizes) {
        $canvas = Draw-IconCanvas $s ($s / 256.0)
        $ms = [System.IO.MemoryStream]::new()
        $canvas.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs.Add($ms.ToArray())
        $ms.Dispose()
        $canvas.Dispose()
    }

    $count = $sizes.Count
    $ico = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ico)
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$count)

    $offset = 6 + 16 * $count
    for ($i = 0; $i -lt $count; $i++) {
        $s = $sizes[$i]
        $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
        $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
        $bw.Write([Byte]0)
        $bw.Write([Byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$pngs[$i].Length)
        $bw.Write([UInt32]$offset)
        $offset += $pngs[$i].Length
    }
    for ($i = 0; $i -lt $count; $i++) {
        $bw.Write($pngs[$i])
    }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())
    $bw.Dispose()
    $ico.Dispose()
    Write-Host "icon -> $icoPath ($($ico.Length) bytes, $count sizes)"
}
