#requires -Version 5.1
<#
.SYNOPSIS
    Builds and publishes gdriveHandler as a self-contained WinUI 3 app folder + zip.

.DESCRIPTION
    WinUI 3 ships as a FOLDER (exe + Windows App SDK runtime DLLs), not a single
    file: PublishSingleFile self-extracts native DLLs to %TEMP% and fail-fasts on
    launch. The whole folder installs into one directory on the target machine.

    Produces:
        dist\gdriveHandler-x64\        - the published app folder
        dist\gdriveHandler-x64.zip     - zipped folder (release asset)

.EXAMPLE
    pwsh build.ps1
    pwsh build.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = if ($PSScriptRoot) { $PSScriptRoot }
        elseif ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path }
        else { (Get-Location).Path }

$proj        = Join-Path $root "src\gdriveHandler\gdriveHandler.csproj"
$testsCsproj = Join-Path $root "tests\gdriveHandler.Tests\gdriveHandler.Tests.csproj"
$dist        = Join-Path $root "dist"
$appDir      = Join-Path $dist "gdriveHandler-x64"
$zipPath     = Join-Path $dist "gdriveHandler-x64.zip"

# Validate dotnet
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
}
Write-Host "dotnet $((& dotnet --version))" -ForegroundColor Cyan

# Tests
if (-not $SkipTests -and (Test-Path $testsCsproj)) {
    Write-Host "Running tests..." -ForegroundColor Cyan
    & dotnet test $testsCsproj -c $Configuration -r win-x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

# --- Self-contained folder publish (NOT single-file) ---
Write-Host "`nPublishing self-contained folder (win-x64)..." -ForegroundColor Cyan
& dotnet publish $proj -c $Configuration -r win-x64 --self-contained true `
    -o $appDir --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$exe = Join-Path $appDir "gdriveHandler.exe"
if (-not (Test-Path $exe)) { throw "Expected output not found: $exe" }

# --- Zip the folder for release ---
Write-Host "Zipping..." -ForegroundColor Cyan
Compress-Archive -Path "$appDir\*" -DestinationPath $zipPath -Force

$folderSize = [math]::Round((Get-ChildItem $appDir -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
$zipSize    = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host "`nDone." -ForegroundColor Green
Write-Host "  App folder : $appDir  ($folderSize MB)"
Write-Host "  Release zip: $zipPath  ($zipSize MB)"
Write-Host "`nInstall (current user):" -ForegroundColor Yellow
Write-Host "  & `"$exe`" --install"
Write-Host "Install (all users, UAC):" -ForegroundColor Yellow
Write-Host "  & `"$exe`" --install --system"
Write-Host "Uninstall:" -ForegroundColor Yellow
Write-Host "  & `"$($env:LOCALAPPDATA)\Programs\gdriveHandler\gdriveHandler.exe`" --uninstall"
