# Generates build\app.ico (ASCII-only source: Windows PowerShell 5.1 reads .ps1 as ANSI).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File build\make_icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\build\app.ico'
$out = [System.IO.Path]::GetFullPath($out)

$size = 64
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

# rounded-rect background
$r = 14
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc(1, 1, $r, $r, 180, 90)
$path.AddArc($size - $r - 1, 1, $r, $r, 270, 90)
$path.AddArc($size - $r - 1, $size - $r - 1, $r, $r, 0, 90)
$path.AddArc(1, $size - $r - 1, $r, $r, 90, 90)
$path.CloseFigure()

$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(255, 62, 128, 255),
    [System.Drawing.Color]::FromArgb(255, 18, 52, 150),
    55.0)
$g.FillPath($brush, $path)

# two white arrows = bidirectional local port forwarding
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 6)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
$g.DrawLine($pen, 14, 24, 44, 24)

$pen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(210, 235, 245, 255), 6)
$pen2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen2.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
$g.DrawLine($pen2, 50, 42, 20, 42)

$g.Dispose()

$hicon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hicon)
$fs = [System.IO.File]::Create($out)
$icon.Save($fs)
$fs.Close()
$icon.Dispose()
$bmp.Dispose()

"icon written: {0} ({1:N0} bytes)" -f $out, (Get-Item $out).Length
