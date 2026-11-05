param([string]$Out = (Join-Path $env:TEMP "MinecraftStudio.Tests\png-oracle"))
# Writes PNG files in every colour type and bit depth with Windows' own encoder (WPF/WIC), interlaced and not, and
# next to each one the exact pixels it holds as raw ARGB (little-endian uint per pixel). The core's PNG reader is
# tested against these (McTests "skins" mode), so it is checked against an independent implementation.
Add-Type -AssemblyName PresentationCore, WindowsBase
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$W = 37; $H = 23
$rng = New-Object System.Random 1234

function Save([string]$name, [System.Windows.Media.Imaging.BitmapSource]$src, [uint32[]]$argb) {
  foreach ($il in @($false, $true)) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Interlace = if ($il) { [System.Windows.Media.Imaging.PngInterlaceOption]::On } else { [System.Windows.Media.Imaging.PngInterlaceOption]::Off }
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($src))
    $file = Join-Path $Out ("{0}{1}.png" -f $name, $(if ($il) { "-interlaced" } else { "" }))
    $fs = [System.IO.File]::Create($file)
    $enc.Save($fs)
    $fs.Close()
    $bytes = New-Object byte[] ($argb.Length * 4)
    [System.Buffer]::BlockCopy($argb, 0, $bytes, 0, $bytes.Length)
    [System.IO.File]::WriteAllBytes([System.IO.Path]::ChangeExtension($file, ".argb"), $bytes)
  }
}

function Argb([int]$a, [int]$r, [int]$g, [int]$b) { [uint32](([uint32]$a -shl 24) -bor ([uint32]$r -shl 16) -bor ([uint32]$g -shl 8) -bor [uint32]$b) }

$PF = [System.Windows.Media.PixelFormats]
$n = $W * $H

# 8-bit RGBA, RGB and grey
$px = New-Object byte[] ($n * 4); $rng.NextBytes($px); $exp = New-Object uint32[] $n
for ($i = 0; $i -lt $n; $i++) { $exp[$i] = Argb $px[$i*4+3] $px[$i*4+2] $px[$i*4+1] $px[$i*4] }
Save "rgba8" ([System.Windows.Media.Imaging.BitmapSource]::Create($W, $H, 96, 96, $PF::Bgra32, $null, $px, $W * 4)) $exp

$stride = [int][Math]::Ceiling($W * 3 / 4) * 4
$px = New-Object byte[] ($stride * $H); $rng.NextBytes($px); $exp = New-Object uint32[] $n
for ($y = 0; $y -lt $H; $y++) { for ($x = 0; $x -lt $W; $x++) { $o = $y * $stride + $x * 3; $exp[$y*$W+$x] = Argb 255 $px[$o+2] $px[$o+1] $px[$o] } }
Save "rgb8" ([System.Windows.Media.Imaging.BitmapSource]::Create($W, $H, 96, 96, $PF::Bgr24, $null, $px, $stride)) $exp

$stride = [int][Math]::Ceiling($W / 4) * 4
$px = New-Object byte[] ($stride * $H); $rng.NextBytes($px); $exp = New-Object uint32[] $n
for ($y = 0; $y -lt $H; $y++) { for ($x = 0; $x -lt $W; $x++) { $v = $px[$y * $stride + $x]; $exp[$y*$W+$x] = Argb 255 $v $v $v } }
Save "grey8" ([System.Windows.Media.Imaging.BitmapSource]::Create($W, $H, 96, 96, $PF::Gray8, $null, $px, $stride)) $exp

# 16-bit: every sample is v * 257, so its high byte (and its rounded 8-bit value) is exactly v.
function Px16([int]$channels, [System.Windows.Media.PixelFormat]$fmt, [string]$name) {
  $stride = $W * $channels * 2
  $px = New-Object byte[] ($stride * $H); $exp = New-Object uint32[] $n
  for ($i = 0; $i -lt $n; $i++) {
    $v = New-Object int[] $channels
    for ($c = 0; $c -lt $channels; $c++) {
      $v[$c] = $rng.Next(256); $s = $v[$c] * 257
      $px[$i * $channels * 2 + $c * 2] = [byte]($s -band 255); $px[$i * $channels * 2 + $c * 2 + 1] = [byte]($s -shr 8)
    }
    $exp[$i] = switch ($channels) { 1 { Argb 255 $v[0] $v[0] $v[0] } 3 { Argb 255 $v[0] $v[1] $v[2] } 4 { Argb $v[3] $v[0] $v[1] $v[2] } }
  }
  Save $name ([System.Windows.Media.Imaging.BitmapSource]::Create($W, $H, 96, 96, $fmt, $null, $px, $stride)) $exp
}
Px16 1 $PF::Gray16 "grey16"
Px16 3 $PF::Rgb48 "rgb16"
Px16 4 $PF::Rgba64 "rgba16"

# Palettes of 2, 4, 16 and 256 colours with see-through entries (written as tRNS).
foreach ($bits in 1, 2, 4, 8) {
  $count = 1 -shl $bits
  $colors = New-Object 'System.Collections.Generic.List[System.Windows.Media.Color]'
  $pal = New-Object uint32[] $count
  for ($c = 0; $c -lt $count; $c++) {
    $a = if ($c % 3 -eq 0) { 255 } elseif ($c % 3 -eq 1) { $rng.Next(256) } else { 0 }
    $r = $rng.Next(256); $g = $rng.Next(256); $b = $rng.Next(256)
    $colors.Add([System.Windows.Media.Color]::FromArgb($a, $r, $g, $b)); $pal[$c] = Argb $a $r $g $b
  }
  $palette = New-Object System.Windows.Media.Imaging.BitmapPalette (,$colors)
  $stride = [int][Math]::Ceiling($W * $bits / 32) * 4
  $px = New-Object byte[] ($stride * $H); $exp = New-Object uint32[] $n
  for ($y = 0; $y -lt $H; $y++) { for ($x = 0; $x -lt $W; $x++) {
    $idx = $rng.Next($count); $bit = $x * $bits; $o = $y * $stride + [int][Math]::Floor($bit / 8)
    $px[$o] = [byte]($px[$o] -bor ($idx -shl (8 - $bits - ($bit % 8))))
    $exp[$y*$W+$x] = $pal[$idx]
  } }
  $fmt = switch ($bits) { 1 { $PF::Indexed1 } 2 { $PF::Indexed2 } 4 { $PF::Indexed4 } 8 { $PF::Indexed8 } }
  Save ("palette$bits") ([System.Windows.Media.Imaging.BitmapSource]::Create($W, $H, 96, 96, $fmt, $palette, $px, $stride)) $exp
}

# Grey at 1, 2 and 4 bits.
foreach ($bits in 1, 2, 4) {
  $max = (1 -shl $bits) - 1
  $stride = [int][Math]::Ceiling($W * $bits / 32) * 4
  $px = New-Object byte[] ($stride * $H); $exp = New-Object uint32[] $n
  for ($y = 0; $y -lt $H; $y++) { for ($x = 0; $x -lt $W; $x++) {
    $v = $rng.Next($max + 1); $bit = $x * $bits; $o = $y * $stride + [int][Math]::Floor($bit / 8)
    $px[$o] = [byte]($px[$o] -bor ($v -shl (8 - $bits - ($bit % 8))))
    $g = [int]($v * 255 / $max); $exp[$y*$W+$x] = Argb 255 $g $g $g
  } }
  $fmt = switch ($bits) { 1 { $PF::BlackWhite } 2 { $PF::Gray2 } 4 { $PF::Gray4 } }
  Save ("grey$bits") ([System.Windows.Media.Imaging.BitmapSource]::Create($W, $H, 96, 96, $fmt, $null, $px, $stride)) $exp
}
Get-ChildItem $Out -Filter *.png | ForEach-Object {
  $b = [System.IO.File]::ReadAllBytes($_.FullName)
  "{0,-26} depth {1,2} colour type {2} interlace {3}" -f $_.Name, $b[24], $b[25], $b[28]
}
