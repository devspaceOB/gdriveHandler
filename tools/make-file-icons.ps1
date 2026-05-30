#requires -Version 5.1
<#
  Builds Windows .ico files from the Google Workspace PNG asset folders.

  The registry installer points DefaultIcon at .ico files, so these are generated
  once from the checked-in PNG sources and copied beside the app at build time.

  Usage:
    powershell -ExecutionPolicy Bypass -File tools\make-file-icons.ps1
#>
[CmdletBinding()]
param(
    [string]$AssetsRoot = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($AssetsRoot)) {
    $scriptRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { Split-Path -Parent $MyInvocation.MyCommand.Path } else { $PSScriptRoot }
    $AssetsRoot = Join-Path $scriptRoot '..\src\gdriveHandler\Assets'
}

$iconSets = [ordered]@{
    Docs   = 'Docs'
    Drive  = 'Drive'
    Forms  = 'Forms'
    Sheets = 'Sheets'
    Sites  = 'Sites'
    Slides = 'Slides'
}

function Get-PngBytes([System.Drawing.Image]$image, [int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($image, 0, 0, $size, $size)

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-Ico([string]$outFile, [object[]]$entries) {
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$entries.Count)

        $offset = 6 + (16 * $entries.Count)
        foreach ($entry in $entries) {
            $writer.Write([Byte]($(if ($entry.Width -ge 256) { 0 } else { $entry.Width })))
            $writer.Write([Byte]($(if ($entry.Height -ge 256) { 0 } else { $entry.Height })))
            $writer.Write([Byte]0)
            $writer.Write([Byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$entry.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $entry.Bytes.Length
        }

        foreach ($entry in $entries) {
            $writer.Write([byte[]]$entry.Bytes)
        }

        $writer.Flush()
        [System.IO.File]::WriteAllBytes($outFile, $stream.ToArray())
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$sourceRoot = Join-Path $AssetsRoot 'news'
$outputRoot = Join-Path $AssetsRoot 'FileIcons'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

foreach ($set in $iconSets.GetEnumerator()) {
    $sourceDir = Join-Path $sourceRoot $set.Value
    if (-not (Test-Path -LiteralPath $sourceDir)) {
        throw "Missing icon source folder: $sourceDir"
    }

    $imagesByWidth = @{}
    foreach ($file in Get-ChildItem -LiteralPath $sourceDir -Filter '*.png' | Sort-Object Name) {
        $image = [System.Drawing.Image]::FromFile($file.FullName)
        try {
            if (-not $imagesByWidth.ContainsKey($image.Width)) {
                $imagesByWidth[$image.Width] = [pscustomobject]@{
                    Width = $image.Width
                    Height = $image.Height
                    Bytes = [System.IO.File]::ReadAllBytes($file.FullName)
                }
            }
        }
        finally {
            $image.Dispose()
        }
    }

    if (-not $imagesByWidth.ContainsKey(32)) {
        throw "Missing 32px PNG source in $sourceDir"
    }

    $baseImagePath = (Get-ChildItem -LiteralPath $sourceDir -Filter '*_1x_web_32dp.png' | Select-Object -First 1).FullName
    $baseImage = [System.Drawing.Image]::FromFile($baseImagePath)
    try {
        $entries = @(
            [pscustomobject]@{
                Width = 16
                Height = 16
                Bytes = Get-PngBytes $baseImage 16
            }
        )
    }
    finally {
        $baseImage.Dispose()
    }

    $entries += $imagesByWidth.GetEnumerator() |
        Sort-Object { [int]$_.Key } |
        ForEach-Object { $_.Value }

    $outFile = Join-Path $outputRoot ($set.Key + '.ico')
    Write-Ico $outFile $entries
    Write-Host ("Wrote {0} ({1} frames)" -f $outFile, $entries.Count)
}
