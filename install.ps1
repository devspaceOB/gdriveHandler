#Requires -Version 5.1
<#
.SYNOPSIS
    Silent installer for gdriveHandler — downloads and installs the latest release.

.DESCRIPTION
    Fetches the latest release zip from GitHub, extracts it into the per-user
    install folder, and registers file associations (no administrator rights).

    Install:
        irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1 | iex

    Uninstall:
        $s = irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1
        & ([scriptblock]::Create($s)) -Uninstall

.PARAMETER Uninstall
    Remove gdriveHandler from the current user account.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repo       = 'devspaceOB/gdriveHandler'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\gdriveHandler'
$installExe = Join-Path $installDir 'gdriveHandler.exe'

function Write-Step { param($m) Write-Host "  $m" -ForegroundColor Cyan }
function Write-Ok   { param($m) Write-Host "  $m" -ForegroundColor Green }
function Write-Fail { param($m) Write-Host "  $m" -ForegroundColor Red; exit 1 }

Write-Host "`ngdriveHandler installer" -ForegroundColor White
Write-Host "=======================`n" -ForegroundColor DarkGray

# ── Uninstall ────────────────────────────────────────────────────────────────
if ($Uninstall) {
    if (-not (Test-Path $installExe)) { Write-Fail "Not installed at: $installExe" }
    Write-Step "Uninstalling..."
    & $installExe --uninstall
    Write-Ok "Uninstalled."
    exit 0
}

# ── Latest release ────────────────────────────────────────────────────────────
Write-Step "Fetching latest release from github.com/$repo ..."
try {
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'gdriveHandler-installer' }
} catch {
    Write-Fail "Could not reach GitHub API.`n$_"
}
$version = $release.tag_name
Write-Ok "Latest: $version"

$asset = $release.assets | Where-Object { $_.name -like '*x64.zip' } | Select-Object -First 1
if (-not $asset) { $asset = $release.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1 }
if (-not $asset) { Write-Fail "No .zip asset found in release $version." }

# ── Download ──────────────────────────────────────────────────────────────────
$tmpZip = Join-Path $env:TEMP "gdriveHandler-$version.zip"
$tmpDir = Join-Path $env:TEMP "gdriveHandler-$version"
Write-Step "Downloading $($asset.name) ($([math]::Round($asset.size/1MB,1)) MB)..."
try {
    Invoke-WebRequest $asset.browser_download_url -OutFile $tmpZip -UseBasicParsing
} catch {
    Write-Fail "Download failed: $_"
}

# ── Extract ───────────────────────────────────────────────────────────────────
Write-Step "Extracting..."
if (Test-Path $tmpDir) { Remove-Item -Recurse -Force $tmpDir }
Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force

# Some zips wrap contents in a top-level folder; find the exe.
$exeInZip = Get-ChildItem $tmpDir -Recurse -Filter 'gdriveHandler.exe' | Select-Object -First 1
if (-not $exeInZip) { Write-Fail "gdriveHandler.exe not found in the downloaded archive." }

# ── Install (the exe copies its own folder into installDir and registers) ─────
Write-Step "Installing for current user (no admin required)..."
& $exeInZip.FullName --install
$code = $LASTEXITCODE

Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue

if ($code -ne 0) {
    Write-Fail "Installation failed (exit code $code). Check $installDir\logs\launcher.log"
}

Write-Host ""
Write-Ok "gdriveHandler $version installed successfully!"
Write-Host "  Location: $installDir" -ForegroundColor DarkGray
Write-Host "  Open 'gdriveHandler' from the Start Menu to configure.`n" -ForegroundColor DarkGray
