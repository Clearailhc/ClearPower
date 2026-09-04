# Rasterise icons/org.clearpower.ClearPower.svg (rounded gradient square + white bolt) into a
# multi-size .ico with System.Drawing. Run from anywhere; writes Sources/ClearPower/Resources/ClearPower.ico.
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "Sources\ClearPower\Resources\ClearPower.ico"
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)
  $k = $s / 128.0
  $g.ScaleTransform($k, $k)
  # rounded square 8..120, rx 28
  $rr = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 56
  $rr.AddArc(8, 8, $d, $d, 180, 90); $rr.AddArc(120 - $d, 8, $d, $d, 270, 90)
  $rr.AddArc(120 - $d, 120 - $d, $d, $d, 0, 90); $rr.AddArc(8, 120 - $d, $d, $d, 90, 90); $rr.CloseFigure()
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 8)), ([System.Drawing.PointF]::new(0, 120)), ([System.Drawing.Color]::FromArgb(255, 0x6F, 0xB4, 0xF2)), ([System.Drawing.Color]::FromArgb(255, 0x4F, 0xC3, 0x86))
  $g.FillPath($grad, $rr)
  $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(8, 8)), ([System.Drawing.PointF]::new(120, 120)), ([System.Drawing.Color]::FromArgb(71, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
  $g.FillPath($sheen, $rr)
  # bolt
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddBezier(72, 22, 66, 24, 63, 30, 60, 36)
  $p.AddLine(60, 36, 46, 66)
  $p.AddBezier(46, 66, 44.5, 69.5, 46.5, 73, 50, 73)
  $p.AddLine(50, 73, 61, 73); $p.AddLine(61, 73, 55, 99)
  $p.AddBezier(55, 99, 54, 103.5, 59, 105.5, 61.5, 102)
  $p.AddLine(61.5, 102, 82, 62)
  $p.AddBezier(82, 62, 84, 58.5, 82, 55, 78.5, 55)
  $p.AddLine(78.5, 55, 67, 55); $p.AddLine(67, 55, 74, 30)
  $p.AddBezier(74, 30, 75.5, 25, 76, 21, 72, 22)
  $p.CloseFigure()
  if ($s -ge 48) {
    $g.TranslateTransform(0, 2)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(64, 0, 0, 0))), $p)
    $g.TranslateTransform(0, -2)
  }
  $g.FillPath([System.Drawing.Brushes]::White, $p)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += ,@($s, $ms.ToArray())
  $bmp.Dispose()
}
# ICO container with PNG-compressed entries
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($e in $pngs) {
  $s = $e[0]; $data = $e[1]
  $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
  $offset += $data.Length
}
foreach ($e in $pngs) { $w.Write($e[1]) }
$w.Close()
"wrote $out ($((Get-Item $out).Length) bytes)"
