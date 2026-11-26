# Draws Mochi (the mascot) into a multi-size .ico using only built-in System.Drawing.
param([string]$Out = (Join-Path $PSScriptRoot 'assets\notch.ico'))
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$S) {
  $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
  $k = $S / 100.0
  $g.ScaleTransform($k, $k)
  $g.TranslateTransform(0, -5)
  if ($S -ge 32) {
    $leaf = New-Object System.Drawing.Drawing2D.GraphicsPath
    $leaf.AddBezier(53,11, 56,3, 67,2, 70,6); $leaf.AddBezier(70,6, 66,13, 58,15, 53,11)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,134,230,164))), $leaf)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,95,191,122)), 3
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawBezier($pen, 50,24, 49.5,19, 50,15, 53,10)
  }
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddBezier(50,22, 77,22, 92,42, 92,62); $p.AddBezier(92,62, 92,82, 75,90, 50,90)
  $p.AddBezier(50,90, 25,90, 8,82, 8,62);   $p.AddBezier(8,62, 8,42, 23,22, 50,22)
  $rect = New-Object System.Drawing.RectangleF 8, 22, 84, 68
  $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255,255,248,240)), ([System.Drawing.Color]::FromArgb(255,255,190,165)), 90
  $g.FillPath($fill, $p)
  $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,42,34,51))
  $pink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(170,255,143,168))
  if ($S -le 16) { $ew = 13; $eh = 16 } else { $ew = 10; $eh = 13 }
  $g.FillEllipse($pink, 18, 61, 14, 9); $g.FillEllipse($pink, 68, 61, 14, 9)
  $g.FillEllipse($ink, (38 - $ew/2), 46, $ew, $eh); $g.FillEllipse($ink, (62 - $ew/2), 46, $ew, $eh)
  if ($S -ge 32) {
    $w = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillEllipse($w, 39, 47.5, 4, 4); $g.FillEllipse($w, 63, 47.5, 4, 4)
    $mp = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,42,34,51)), 2.8
    $mp.StartCap = 'Round'; $mp.EndCap = 'Round'
    $g.DrawBezier($mp, 45,64, 47.5,68.5, 52.5,68.5, 55,64)
  }
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 48, 64, 256
$frames = @(); foreach ($s in $sizes) { $frames += ,(New-Frame $s) }
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $d = $frames[$i]
  $dim = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$d.Length); $bw.Write([UInt32]$offset)
  $offset += $d.Length
}
foreach ($d in $frames) { $bw.Write([byte[]]$d) }
$bw.Close()
Write-Host "icon -> $Out"
