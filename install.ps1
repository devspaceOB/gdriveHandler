#Requires -Version 5.1
<#
.SYNOPSIS
    Silent installer for gdriveHandler — downloads and installs the latest release.

.DESCRIPTION
    Fetches the latest self-contained release from GitHub, extracts it, and runs
    the per-user install (no administrator rights required).

    Usage (run in PowerShell):
        irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1 | iex

    Or with -Uninstall to remove:
        $s = irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1
        & ([scriptblock]::Create($s)) -Uninstall

.PARAMETER Uninstall
    Remove gdriveHandler from the current user account.

.PARAMETER Force
    Overwrite an existing installation without prompting.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repo       = 'devspaceOB/gdriveHandler'
$exeName    = 'gdriveHandler.exe'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\gdriveHandler'
$installExe = Join-Path $installDir $exeName

function Write-Step { param([string]$msg) Write-Host "  $msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$msg) Write-Host "  $msg" -ForegroundColor Green }
function Write-Fail { param([string]$msg) Write-Host "  $msg" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "gdriveHandler installer" -ForegroundColor White
Write-Host "=======================" -ForegroundColor DarkGray
Write-Host ""

# ── Uninstall path ──────────────────────────────────────────────────────────
if ($Uninstall) {
    if (-not (Test-Path $installExe)) {
        Write-Fail "gdriveHandler does not appear to be installed at: $installExe"
    }
    Write-Step "Uninstalling..."
    & $installExe --uninstall
    Write-Ok "Uninstalled."
    exit 0
}

# ── Fetch latest release from GitHub API ───────────────────────────────────
Write-Step "Fetching latest release from github.com/$repo ..."
try {
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
} catch {
    Write-Fail "Could not reach GitHub API. Check your internet connection.`n$_"
}

$version = $release.tag_name
Write-Ok "Latest: $version"

# Prefer self-contained exe; fall back to any .exe asset
$asset = $release.assets | Where-Object { $_.name -like '*x64.exe' -and $_.name -notlike '*fd*' } | Select-Object -First 1
if (-not $asset) {
    $asset = $release.assets | Where-Object { $_.name -like '*.exe' } | Select-Object -First 1
}
if (-not $asset) {
    Write-Fail "No executable asset found in release $version."
}

# ── Check existing install ──────────────────────────────────────────────────
if ((Test-Path $installExe) -and -not $Force) {
    $choice = Read-Host "  gdriveHandler is already installed. Overwrite? [Y/n]"
    if ($choice -match '^[Nn]') { Write-Host "  Aborted."; exit 0 }
}

# ── Download ────────────────────────────────────────────────────────────────
$tmp = Join-Path $env:TEMP "gdriveHandler-setup-$version.exe"
Write-Step "Downloading $($asset.name) ($([math]::Round($asset.size/1MB,1)) MB)..."
try {
    Invoke-WebRequest $asset.browser_download_url -OutFile $tmp -UseBasicParsing
} catch {
    Write-Fail "Download failed: $_"
}
Write-Ok "Download complete."

# ── Install ──────────────────────────────────────────────────────────────────
Write-Step "Installing for current user (no admin required)..."
& $tmp --install
if ($LASTEXITCODE -ne 0) {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    Write-Fail "Installation failed (exit code $LASTEXITCODE). Check logs at $installDir\logs\launcher.log"
}

Remove-Item $tmp -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Ok "gdriveHandler $version installed successfully!"
Write-Host "  Location: $installDir" -ForegroundColor DarkGray
Write-Host "  Open 'gdriveHandler' from the Start Menu to configure." -ForegroundColor DarkGray
Write-Host ""
