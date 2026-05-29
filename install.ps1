#Requires -Version 5.1
<#
.SYNOPSIS
    Installer for gdriveHandler.

.DESCRIPTION
    Downloads the latest self-contained zip release, extracts it to a temporary
    folder, and runs gdriveHandler.exe --install. The app then copies itself into
    the per-user install folder and registers file associations.

.PARAMETER Uninstall
    Remove gdriveHandler from the current user account.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repo = 'devspaceOB/gdriveHandler'
$legacyInstallDir = Join-Path $env:LOCALAPPDATA 'Programs\gdriveHandler'
$legacyInstallExe = Join-Path $legacyInstallDir 'gdriveHandler.exe'

function Write-Step { param($m) Write-Host "  $m" -ForegroundColor Cyan }
function Write-Ok   { param($m) Write-Host "  $m" -ForegroundColor Green }
function Write-Fail { param($m) Write-Host "  $m" -ForegroundColor Red; exit 1 }

function Get-LatestRelease {
    Write-Step "Fetching latest release from github.com/$repo ..."
    try {
        return Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'gdriveHandler-installer' }
    } catch {
        Write-Fail "Could not reach GitHub API.`n$_"
    }
}

function Download-Asset {
    param($Asset, [string]$Path)
    Write-Step "Downloading $($Asset.name) ($([math]::Round($Asset.size / 1MB, 1)) MB)..."
    Invoke-WebRequest $Asset.browser_download_url -OutFile $Path -UseBasicParsing
}

Write-Host "`ngdriveHandler installer" -ForegroundColor White
Write-Host "=======================`n" -ForegroundColor DarkGray

if ($Uninstall) {
    if (-not (Test-Path $legacyInstallExe)) { Write-Fail "Not installed." }
    Write-Step "Uninstalling..."
    & $legacyInstallExe --uninstall
    if ($LASTEXITCODE -ne 0) { Write-Fail "Uninstall failed with exit code $LASTEXITCODE." }
    Write-Ok "Uninstalled."
    exit 0
}

$release = Get-LatestRelease
$version = $release.tag_name
Write-Ok "Latest: $version"

$asset = $release.assets | Where-Object { $_.name -like '*x64-selfcontained.zip' } | Select-Object -First 1
if (-not $asset) { $asset = $release.assets | Where-Object { $_.name -like '*x64.zip' } | Select-Object -First 1 }
if (-not $asset) { $asset = $release.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1 }
if (-not $asset) { Write-Fail "No self-contained zip asset found in release $version." }

$tmpZip = Join-Path $env:TEMP "gdriveHandler-$version.zip"
$tmpDir = Join-Path $env:TEMP "gdriveHandler-$version"

try {
    Download-Asset $asset $tmpZip
    Write-Step "Extracting..."
    if (Test-Path $tmpDir) { Remove-Item -Recurse -Force $tmpDir }
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force

    $exeInZip = Get-ChildItem $tmpDir -Recurse -Filter 'gdriveHandler.exe' | Select-Object -First 1
    if (-not $exeInZip) { Write-Fail "gdriveHandler.exe not found in the downloaded archive." }

    Write-Step "Installing for current user..."
    & $exeInZip.FullName --install
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Write-Fail "Installation failed (exit code $code). Check $env:LOCALAPPDATA\gdriveHandler\logs\launcher.log"
    }
} finally {
    Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
    Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Ok "gdriveHandler $version installed successfully."
Write-Host "  Location: $legacyInstallDir" -ForegroundColor DarkGray
Write-Host "  Open 'gdriveHandler' from the Start Menu to configure.`n" -ForegroundColor DarkGray
