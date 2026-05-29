#requires -Version 5.1
<#
.SYNOPSIS
    Builds and publishes gdriveHandler as a WinUI 3 unpackaged EXE.

.DESCRIPTION
    Produces:
        dist\gdriveHandler-x64.exe        - self-contained single-file (primary)
        dist\gdriveHandler-x64-Setup.exe  - identical self-installing copy
        dist\gdriveHandler-x64-fd.exe     - framework-dependent (requires .NET 10 + WinAppSDK 2.0)

.EXAMPLE
    pwsh build.ps1
    pwsh build.ps1 -SkipTests
    pwsh build.ps1 -SelfContainedOnly
    pwsh build.ps1 -FrameworkDependentOnly
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SelfContainedOnly,
    [switch]$FrameworkDependentOnly
)

$ErrorActionPreference = "Stop"
$root = if ($PSScriptRoot) { $PSScriptRoot }
        elseif ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path }
        else { (Get-Location).Path }

$proj        = Join-Path $root "src\gdriveHandler\gdriveHandler.csproj"
$testsCsproj = Join-Path $root "tests\gdriveHandler.Tests\gdriveHandler.Tests.csproj"
$dist        = Join-Path $root "dist"
$tfm         = "net10.0-windows10.0.26100.0"

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

# --- Self-contained build (primary) ---
if (-not $FrameworkDependentOnly) {
    Write-Host "`nPublishing self-contained (win-x64)..." -ForegroundColor Cyan
    $scPublish = Join-Path $root "src\gdriveHandler\bin\$Configuration\$tfm\win-x64\publish"
    & dotnet publish $proj -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "self-contained publish failed" }

    $scExe = Join-Path $scPublish "gdriveHandler.exe"
    if (-not (Test-Path $scExe)) { throw "Expected output not found: $scExe" }
    Copy-Item $scExe (Join-Path $dist "gdriveHandler-x64.exe") -Force
    Copy-Item $scExe (Join-Path $dist "gdriveHandler-x64-Setup.exe") -Force
    $size = [math]::Round((Get-Item $scExe).Length / 1MB, 1)
    Write-Host "Self-contained: $size MB" -ForegroundColor Green
}

# --- Framework-dependent build (secondary) ---
if (-not $SelfContainedOnly) {
    Write-Host "`nPublishing framework-dependent (win-x64)..." -ForegroundColor Cyan
    $fdPublish = Join-Path $root "src\gdriveHandler\bin\$Configuration\$tfm\win-x64-fd\publish"
    & dotnet publish $proj -c $Configuration -r win-x64 --self-contained false `
        -p:PublishSingleFile=true `
        -o $fdPublish `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "framework-dependent publish failed" }

    $fdExe = Join-Path $fdPublish "gdriveHandler.exe"
    if (-not (Test-Path $fdExe)) { throw "Expected output not found: $fdExe" }
    Copy-Item $fdExe (Join-Path $dist "gdriveHandler-x64-fd.exe") -Force
    $fdSize = [math]::Round((Get-Item $fdExe).Length / 1MB, 1)
    Write-Host "Framework-dependent: $fdSize MB  (requires .NET 10 + WinAppSDK 2.0 runtime)" -ForegroundColor Green
}

Write-Host "`nDone. Outputs in $dist" -ForegroundColor Green
Write-Host "`nInstall (current user):" -ForegroundColor Yellow
Write-Host "  `"$(Join-Path $dist 'gdriveHandler-x64-Setup.exe')`" --install"
Write-Host "Install (all users, UAC):" -ForegroundColor Yellow
Write-Host "  `"$(Join-Path $dist 'gdriveHandler-x64-Setup.exe')`" --install --system"
Write-Host "Uninstall:" -ForegroundColor Yellow
Write-Host "  `"$($env:LOCALAPPDATA)\Programs\gdriveHandler\gdriveHandler.exe`" --uninstall"
