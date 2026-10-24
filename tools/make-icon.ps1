# Draws the project's icon (a black squircle with a grey edge, "FM" in white and "4" in red, Barlow Condensed
# Bold) into an .ico; the launcher's adds a red play badge, so the two windows differ on the taskbar.
# Windows PowerShell 5.1 (System.Drawing).
[CmdletBinding()]
param(
    # The launcher's icon instead of the game's.
    [switch]$Launcher,
    # Default: src\fm4_static_recomp.ico (the game) or the launcher's Assets\launcher.ico.
    [string]$Output,
    # Also write each size as a PNG into this folder, to look at.
    [string]$PreviewFolder
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Output) {
    $Output = if ($Launcher) { Join-Path $root 'launcher\Fm4StaticRecomp.Launcher\Assets\launcher.ico' }
              else { Join-Path $root 'src\fm4_static_recomp.ico' }
}
$fontFile = Join-Path $root 'launcher\Fm4StaticRecomp.Launcher\Assets\Fonts\BarlowCondensed-Bold.ttf'
$fonts = New-Object System.Drawing.Text.PrivateFontCollection
$fonts.AddFontFile($fontFile)
$family = $fonts.Families[0]
$white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
$red = [System.Drawing.Color]::FromArgb(255, 0xD4, 0x2E, 0x19)
$grey = [System.Drawing.Color]::FromArgb(255, 0x6F, 0x6F, 0x6F)

# A superellipse |x|^5 + |y|^5 = 1 filling the square, a pixel's margin all round.
function New-Squircle([float]$size) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $half = $size / 2
    $radius = $half - [Math]::Max(0.5, $size / 64)
    $points = for ($i = 0; $i -lt 256; $i++) {
        $t = 2 * [Math]::PI * $i / 256
        $c = [Math]::Cos($t); $s = [Math]::Sin($t)
        New-Object System.Drawing.PointF(
            [float]($half + $radius * [Math]::Sign($c) * [Math]::Pow([Math]::Abs($c), 0.4)),
            [float]($half + $radius * [Math]::Sign($s) * [Math]::Pow([Math]::Abs($s), 0.4)))
    }
    $path.AddPolygon([System.Drawing.PointF[]]$points)
    $path
}

function New-IconBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)
    $squircle = New-Squircle $size
    $g.FillPath([System.Drawing.Brushes]::Black, $squircle)
    # A grey edge, so the black square stands out on a dark taskbar.
    $edge = New-Object System.Drawing.Pen ($grey, [float][Math]::Max(1.0, $size / 48))
    $g.DrawPath($edge, $squircle)
    # The text as outlines, scaled to a fixed share of the square and centred on its ink.
    $text = New-Object System.Drawing.Drawing2D.GraphicsPath
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $text.AddString('FM', $family, [int][System.Drawing.FontStyle]::Bold, 100, (New-Object System.Drawing.PointF(0, 0)), $format)
    $fm = $text.GetBounds()
    $four = New-Object System.Drawing.Drawing2D.GraphicsPath
    $four.AddString('4', $family, [int][System.Drawing.FontStyle]::Bold, 100, (New-Object System.Drawing.PointF(($fm.Right + 4), 0)), $format)
    $all = $four.GetBounds()
    $left = $fm.Left; $top = [Math]::Min($fm.Top, $all.Top)
    $width = $all.Right - $left; $height = [Math]::Max($fm.Bottom, $all.Bottom) - $top
    # Width and height shares, then the centre; the launcher's text moves up and left for the badge.
    $box = if ($Launcher) { 0.58, 0.34, 0.38, 0.35 } else { 0.70, 0.46, 0.5, 0.5 }
    $scale = [Math]::Min($size * $box[0] / $width, $size * $box[1] / $height)
    $matrix = New-Object System.Drawing.Drawing2D.Matrix
    $matrix.Translate([float]($size * $box[2] - $width * $scale / 2), [float]($size * $box[3] - $height * $scale / 2))
    $matrix.Scale([float]$scale, [float]$scale)
    $matrix.Translate([float](-$left), [float](-$top))
    $text.Transform($matrix); $four.Transform($matrix)
    $g.FillPath((New-Object System.Drawing.SolidBrush $white), $text)
    $g.FillPath((New-Object System.Drawing.SolidBrush $red), $four)
    if ($Launcher) {
        $d = $size * 0.42; $x = $size - $d - $size * 0.05
        $g.FillEllipse((New-Object System.Drawing.SolidBrush $red), [float]$x, [float]$x, [float]$d, [float]$d)
        $triangle = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(($x + $d * 0.38), ($x + $d * 0.27))),
            (New-Object System.Drawing.PointF(($x + $d * 0.38), ($x + $d * 0.73))),
            (New-Object System.Drawing.PointF(($x + $d * 0.76), ($x + $d * 0.50))))
        $g.FillPolygon((New-Object System.Drawing.SolidBrush $white), $triangle)
    }
    $g.Dispose()
    $bitmap
}

# An .ico whose entries are PNGs (Windows Vista and later read them at every size).
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($PreviewFolder) {
        [void](New-Item -ItemType Directory -Force -Path $PreviewFolder)
        $bitmap.Save((Join-Path $PreviewFolder "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bitmap.Dispose()
    , $stream.ToArray()
}
$file = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $edge = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Output), $file.ToArray())
"Wrote $([IO.Path]::GetFullPath($Output)) ($($sizes -join ', ') px)"
