#requires -Version 5.1
<#
  Dev-time asset generator (NOT a build/runtime dependency).
  Renders a slightly-recolored Google-Drive-style trifold mark into a
  multi-resolution .ico using classic 32bpp BMP (DIB) entries, which are
  universally supported by the C# compiler's icon embedder and the shell.

  Palette is hue-shifted off Drive's blue/green/yellow so the result is a
  distinct derivative rather than a copy.

  Usage:
    powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -OutFile <path>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutFile
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# Recolored palette (hue-shifted from Drive blue/green/yellow).
$cLeft  = [System.Drawing.Color]::FromArgb(255, 0x2E, 0x6F, 0xD6)   # indigo-blue
$cRight = [System.Drawing.Color]::FromArgb(255, 0x1E, 0xA8, 0x7A)   # teal-green
$cTop   = [System.Drawing.Color]::FromArgb(255, 0xF0, 0x9A, 0x22)   # amber-orange

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        $pad = $size * 0.12
        $top    = New-Object System.Drawing.PointF(($size * 0.5), $pad)
        $left   = New-Object System.Drawing.PointF($pad, ($size - $pad))
        $right  = New-Object System.Drawing.PointF(($size - $pad), ($size - $pad))
        $center = New-Object System.Drawing.PointF((($top.X + $left.X + $right.X) / 3.0), (($top.Y + $left.Y + $right.Y) / 3.0))

        $faces = @(
            @{ Color = $cLeft;  Pts = @($top,  $left,  $center) },
            @{ Color = $cRight; Pts = @($top,  $right, $center) },
            @{ Color = $cTop;   Pts = @($left, $right, $center) }
        )
        foreach ($f in $faces) {
            $brush = New-Object System.Drawing.SolidBrush($f.Color)
            try { $g.FillPolygon($brush, [System.Drawing.PointF[]]$f.Pts) }
            finally { $brush.Dispose() }
        }
    }
    finally { $g.Dispose() }
    return $bmp
}

# Returns the DIB bytes for one icon image: BITMAPINFOHEADER + XOR(32bpp,bottom-up) + AND mask.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $buf = [byte[]]::new($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    } finally {
        $bmp.UnlockBits($data)
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER
    $bw.Write([UInt32]40)        # biSize
    $bw.Write([Int32]$w)         # biWidth
    $bw.Write([Int32]($h * 2))   # biHeight (XOR + AND)
    $bw.Write([UInt16]1)         # biPlanes
    $bw.Write([UInt16]32)        # biBitCount
    $bw.Write([UInt32]0)         # biCompression = BI_RGB
    $bw.Write([UInt32]0)         # biSizeImage
    $bw.Write([Int32]0); $bw.Write([Int32]0)   # ppm x/y
    $bw.Write([UInt32]0); $bw.Write([UInt32]0) # clrUsed / clrImportant

    # XOR: 32bpp BGRA, bottom-up rows. LockBits buffer is top-down BGRA.
    for ($y = $h - 1; $y -ge 0; $y--) {
        $bw.Write($buf, ($y * $stride), ($w * 4))
    }
    # AND mask: 1bpp, rows padded to 32-bit, bottom-up; all zero (alpha governs).
    $maskRow = [int][math]::Floor((($w + 31) / 32)) * 4
    $zeros = [byte[]]::new($maskRow * $h)
    $bw.Write($zeros, 0, $zeros.Length)
    $bw.Flush()
    return ,$ms.ToArray()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) {
    $bmp = New-Frame $s
    $dib = [byte[]](Get-DibBytes $bmp)
    $images.Add($dib)
    $bmp.Dispose()
    Write-Host ("  {0,3}px -> {1} bytes" -f $s, $dib.Length)
}

# Assemble ICO (ICONDIR + ICONDIRENTRY[] + DIB payloads).
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$images.Count)
$offset = 6 + (16 * $images.Count)
for ($i = 0; $i -lt $images.Count; $i++) {
    $s = $sizes[$i]; $bytes = $images[$i]
    $dim = [byte]($(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]$dim)            # width  (0 => 256)
    $bw.Write([Byte]$dim)            # height (0 => 256)
    $bw.Write([Byte]0)               # palette
    $bw.Write([Byte]0)               # reserved
    $bw.Write([UInt16]1)             # planes
    $bw.Write([UInt16]32)            # bpp
    $bw.Write([UInt32]$bytes.Length) # bytes in resource
    $bw.Write([UInt32]$offset)       # offset
    $offset += $bytes.Length
}
foreach ($bytes in $images) { $bw.Write($bytes) }
$bw.Flush()

$resolved = [System.IO.Path]::GetFullPath($OutFile)
[System.IO.File]::WriteAllBytes($resolved, $out.ToArray())
$bw.Dispose(); $out.Dispose()
Write-Host "Wrote $resolved ($([math]::Round((Get-Item $resolved).Length/1KB,1)) KB, $($images.Count) sizes)"
