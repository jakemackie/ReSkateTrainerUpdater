<#
.SYNOPSIS
    Draws the two pictures the setup wizard shows (wizard-large.bmp and wizard-small.bmp).

.DESCRIPTION
    They are already committed, so this only needs running to change the look. It uses the app's own icon and fonts, so
    the installer matches the app. Windows only (System.Drawing).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\installer\make-wizard-images.ps1
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$here   = Split-Path -Parent $MyInvocation.MyCommand.Path
$assets = Join-Path (Split-Path -Parent $here) 'src\ReSkateTrainerUpdater\Assets'

$fonts = [System.Drawing.Text.PrivateFontCollection]::new()
foreach ($file in 'PermanentMarker-Regular.ttf', 'Montserrat-ExtraBold.ttf', 'Montserrat-SemiBold.ttf') {
    $fonts.AddFontFile((Join-Path $assets "Fonts\$file"))
}
function Family($name) { $fonts.Families | Where-Object { $_.Name -eq $name } | Select-Object -First 1 }
$marker   = Family 'Permanent Marker'
$semibold = Family 'Montserrat SemiBold'

$icon = [System.Drawing.Icon]::new((Join-Path $assets 'ReSkateTrainerUpdater.ico'), 256, 256)
$logo = $icon.ToBitmap()

function New-Backdrop([System.Drawing.Graphics]$g, [int]$w, [int]$h) {
    $top = [System.Drawing.Color]::FromArgb(30, 44, 68)
    $bottom = [System.Drawing.Color]::FromArgb(10, 11, 14)
    $gradient = [System.Drawing.RectangleF]::new(-1, -1, ($w + 2), ($h + 2))
    $sky = [System.Drawing.Drawing2D.LinearGradientBrush]::new($gradient, $top, $bottom, 90.0)
    $g.FillRectangle($sky, 0, 0, $w, $h)

    # A blue glow low down, like the glow behind the app's big tile.
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddEllipse([single](-$w * 0.4), [single]($h * 0.62), [single]($w * 1.8), [single]($h * 0.7))
    $glow = [System.Drawing.Drawing2D.PathGradientBrush]::new($path)
    $glow.CenterColor = [System.Drawing.Color]::FromArgb(110, 1, 131, 255)
    $glow.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(0, 1, 131, 255))
    $g.FillPath($glow, $path)
}

# ---- the tall picture down the side of the first and last pages ----
$w = 328; $h = 628
$large = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($large)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'
New-Backdrop $g $w $h

$logoSize = 150
$g.DrawImage($logo, [int](($w - $logoSize) / 2), 70, $logoSize, $logoSize)

$word = [System.Drawing.Font]::new($marker, 40.0, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$centre = [System.Drawing.StringFormat]::new()
$centre.Alignment = [System.Drawing.StringAlignment]::Center
$centre.LineAlignment = [System.Drawing.StringAlignment]::Center
$shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(150, 0, 0, 0))

$state = $g.Save()
$g.TranslateTransform([single]($w / 2), 330.0)
$g.RotateTransform(-4.0)
$lines = @(@{ Text = 'TRAINER'; Y = -34 }, @{ Text = 'UPDATER'; Y = 28 })
foreach ($line in $lines) {
    $x0 = [single](-$w / 2)
    $y0 = [single]$line.Y
    $rect = [System.Drawing.RectangleF]::new($x0, $y0, [single]$w, 70.0)
    $shadow = [System.Drawing.RectangleF]::new(($x0 + 3), ($y0 + 4), [single]$w, 70.0)
    $g.DrawString($line.Text, $word, $shadowBrush, $shadow, $centre)
    $g.DrawString($line.Text, $word, [System.Drawing.Brushes]::White, $rect, $centre)
}
$g.Restore($state)

$smallFont = [System.Drawing.Font]::new($semibold, 10.5, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(180, 190, 205))
$credit = [System.Drawing.RectangleF]::new(0.0, [single]($h - 54), [single]$w, 24.0)
$g.DrawString('Made by os_uk', $smallFont, $muted, $credit, $centre)
$g.Dispose()
$large.Save((Join-Path $here 'wizard-large.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)

# ---- the small square picture at the top right of the other pages ----
$s = 110
$tile = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($tile)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
New-Backdrop $g $s $s
$g.DrawImage($logo, 13, 13, 84, 84)
$g.Dispose()
$tile.Save((Join-Path $here 'wizard-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)

Write-Host "Wrote wizard-large.bmp ($w x $h) and wizard-small.bmp ($s x $s) in $here"
