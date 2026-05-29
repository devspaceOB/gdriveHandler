#Requires -Version 5.1
<#
.SYNOPSIS
    Installer for gdriveHandler.

.DESCRIPTION
    Prefers the signed MSIX release asset. If a release has no MSIX, falls back
    to the legacy self-contained zip installer.

.PARAMETER Uninstall
    Remove gdriveHandler from the current user account.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repo = 'devspaceOB/gdriveHandler'
$packageName = 'devSpaceOB.gdriveHandler'
$legacyInstallDir = Join-Path $env:LOCALAPPDATA 'Programs\gdriveHandler'
$legacyInstallExe = Join-Path $legacyInstallDir 'gdriveHandler.exe'

function Write-Step { param($m) Write-Host "  $m" -ForegroundColor Cyan }
function Write-Ok   { param($m) Write-Host "  $m" -ForegroundColor Green }
function Write-Fail { param($m) Write-Host "  $m" -ForegroundColor Red; exit 1 }

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Import-CertificateForMsix {
    param([string]$CerPath)

    if (Test-Admin) {
        Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        return
    }

    Write-Step "Trusting signing certificate (administrator prompt)..."
    $escaped = $CerPath.Replace("'", "''")
    $script = @"
`$ErrorActionPreference = 'Stop'
Import-Certificate -FilePath '$escaped' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Import-Certificate -FilePath '$escaped' -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    $proc = Start-Process powershell.exe `
        -Verb RunAs `
        -Wait `
        -PassThru `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
    if ($proc.ExitCode -ne 0) {
        throw "certificate trust import failed or was cancelled"
    }
}

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
    $pkg = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
    if ($pkg) {
        Write-Step "Removing MSIX package..."
        Remove-AppxPackage -Package $pkg.PackageFullName
        Write-Ok "Uninstalled."
        exit 0
    }

    if (-not (Test-Path $legacyInstallExe)) { Write-Fail "Not installed." }
    Write-Step "Uninstalling legacy install..."
    & $legacyInstallExe --uninstall
    if ($LASTEXITCODE -ne 0) { Write-Fail "Legacy uninstall failed with exit code $LASTEXITCODE." }
    Write-Ok "Uninstalled."
    exit 0
}

$release = Get-LatestRelease
$version = $release.tag_name
Write-Ok "Latest: $version"

$msixAsset = $release.assets | Where-Object { $_.name -like '*-x64.msix' } | Select-Object -First 1
$cerAsset = $release.assets | Where-Object { $_.name -like '*-x64.cer' } | Select-Object -First 1

if ($msixAsset -and $cerAsset) {
    $tmpDir = Join-Path $env:TEMP "gdriveHandler-$version-msix"
    if (Test-Path $tmpDir) { Remove-Item -Recurse -Force $tmpDir }
    New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

    $tmpMsix = Join-Path $tmpDir $msixAsset.name
    $tmpCer = Join-Path $tmpDir $cerAsset.name

    try {
        Download-Asset $cerAsset $tmpCer
        Download-Asset $msixAsset $tmpMsix
        Import-CertificateForMsix $tmpCer

        Write-Step "Installing MSIX..."
        Add-AppxPackage -Path $tmpMsix
        Write-Ok "gdriveHandler $version installed successfully."
        Write-Host "  Open 'gdriveHandler' from the Start Menu to configure.`n" -ForegroundColor DarkGray
        exit 0
    } catch {
        Write-Fail "MSIX installation failed.`n$_"
    } finally {
        Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Step "No MSIX + certificate assets found; using legacy zip fallback."
$asset = $release.assets | Where-Object { $_.name -like '*x64-selfcontained.zip' } | Select-Object -First 1
if (-not $asset) { $asset = $release.assets | Where-Object { $_.name -like '*x64.zip' } | Select-Object -First 1 }
if (-not $asset) { $asset = $release.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1 }
if (-not $asset) { Write-Fail "No installable asset found in release $version." }

$tmpZip = Join-Path $env:TEMP "gdriveHandler-$version.zip"
$tmpDir = Join-Path $env:TEMP "gdriveHandler-$version"

try {
    Download-Asset $asset $tmpZip
    Write-Step "Extracting..."
    if (Test-Path $tmpDir) { Remove-Item -Recurse -Force $tmpDir }
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force

    $exeInZip = Get-ChildItem $tmpDir -Recurse -Filter 'gdriveHandler.exe' | Select-Object -First 1
    if (-not $exeInZip) { Write-Fail "gdriveHandler.exe not found in the downloaded archive." }

    Write-Step "Installing legacy app for current user..."
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
