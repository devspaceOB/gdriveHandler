#requires -Version 5.1
<#
.SYNOPSIS
    Builds release artifacts for gdriveHandler.

.DESCRIPTION
    Produces the portable self-contained zip and a small framework-dependent exe.
    MSIX packaging is retained for later but hidden behind -IncludeMsix.

.EXAMPLE
    pwsh build.ps1
    pwsh build.ps1 -SkipTests
    pwsh build.ps1 -IncludeMsix -PfxPath .\cert.pfx -PfxPassword $env:SIGNING_PFX_PASSWORD
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$IncludeMsix,
    [string]$PfxPath,
    [string]$PfxPassword
)

$ErrorActionPreference = "Stop"
$root = if ($PSScriptRoot) { $PSScriptRoot }
        elseif ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path }
        else { (Get-Location).Path }

$proj         = Join-Path $root "src\gdriveHandler\gdriveHandler.csproj"
$testsCsproj  = Join-Path $root "tests\gdriveHandler.Tests\gdriveHandler.Tests.csproj"
$dist         = Join-Path $root "dist"
$msixBuildDir = Join-Path $dist "msix"

[xml]$projXml = Get-Content $proj
$version = ($projXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = "0.0.0" }

$appDir    = Join-Path $dist "gdriveHandler-$version-x64-selfcontained"
$zipPath   = Join-Path $dist "gdriveHandler-$version-x64-selfcontained.zip"
$fdDir     = Join-Path $dist "gdriveHandler-$version-x64-fd"
$fdExePath = Join-Path $dist "gdriveHandler-$version-x64-fd.exe"
$msixPath  = Join-Path $dist "gdriveHandler-$version-x64.msix"
$cerPath   = Join-Path $dist "gdriveHandler-$version-x64.cer"

function Invoke-Native {
    param(
        [string]$FileName,
        [string[]]$Arguments
    )
    & $FileName @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FileName failed with exit code $LASTEXITCODE"
    }
}

function Find-SignTool {
    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
    $nugetTool = Join-Path $nugetRoot "microsoft.windows.sdk.buildtools\10.0.26100.8249\bin\10.0.26100.0\x64\signtool.exe"
    if (Test-Path $nugetTool) {
        return $nugetTool
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path $kitsRoot) {
        $tool = Get-ChildItem $kitsRoot -Recurse -Filter signtool.exe |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($tool) { return $tool.FullName }
    }

    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    throw "signtool.exe not found. Install the Windows SDK or run without -PfxPath."
}

function Sign-Msix {
    param([string]$Path)

    if (-not $PfxPath) {
        Write-Host "MSIX signing skipped; no -PfxPath supplied." -ForegroundColor Yellow
        return
    }
    if (-not (Test-Path $PfxPath)) {
        throw "PFX not found: $PfxPath"
    }

    $signtool = Find-SignTool
    $args = @(
        "sign", "/fd", "SHA256",
        "/f", $PfxPath,
        "/tr", "http://timestamp.digicert.com",
        "/td", "SHA256"
    )
    if ($PfxPassword) {
        $args += @("/p", $PfxPassword)
    }
    $args += $Path

    Write-Host "Signing MSIX..." -ForegroundColor Cyan
    & $signtool @args
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }

    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword, $flags)
    [IO.File]::WriteAllBytes($cerPath, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
}
Write-Host "dotnet $((& dotnet --version))" -ForegroundColor Cyan

if (-not $SkipTests -and (Test-Path $testsCsproj)) {
    Write-Host "Running tests..." -ForegroundColor Cyan
    Invoke-Native dotnet @("test", $testsCsproj, "-c", $Configuration, "-r", "win-x64", "--nologo")
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
foreach ($path in @($appDir, $fdDir, $msixBuildDir)) {
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}
foreach ($path in @($zipPath, $fdExePath, $msixPath, $cerPath)) {
    if (Test-Path $path) { Remove-Item -Force $path }
}

Write-Host "`nPublishing portable self-contained folder..." -ForegroundColor Cyan
Invoke-Native dotnet @(
    "publish", $proj,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:WindowsPackageType=None",
    "-o", $appDir,
    "--nologo"
)

$exe = Join-Path $appDir "gdriveHandler.exe"
if (-not (Test-Path $exe)) { throw "Expected output not found: $exe" }

Write-Host "Zipping portable fallback..." -ForegroundColor Cyan
Compress-Archive -Path "$appDir\*" -DestinationPath $zipPath -Force

Write-Host "Publishing framework-dependent exe..." -ForegroundColor Cyan
Invoke-Native dotnet @(
    "publish", $proj,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "false",
    "-p:WindowsPackageType=None",
    "-p:WindowsAppSDKSelfContained=false",
    "-p:PublishSingleFile=false",
    "-o", $fdDir,
    "--nologo"
)
$fdPublishedExe = Join-Path $fdDir "gdriveHandler.exe"
if (Test-Path $fdPublishedExe) {
    Copy-Item $fdPublishedExe $fdExePath -Force
} else {
    Write-Host "Framework-dependent exe was not produced; skipping fd asset." -ForegroundColor Yellow
}

if ($IncludeMsix) {
    Write-Host "Publishing MSIX..." -ForegroundColor Cyan
    Invoke-Native dotnet @(
        "publish", $proj,
        "-c", $Configuration,
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:GenerateAppxPackageOnBuild=true",
        "-p:AppxPackageSigningEnabled=false",
        "-p:AppxBundle=Never",
        "-p:AppxPackageDir=$msixBuildDir\",
        "--nologo"
    )

    $builtMsix = Get-ChildItem $msixBuildDir -Recurse -Filter *.msix |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $builtMsix) { throw "MSIX package was not produced in $msixBuildDir" }

    Copy-Item $builtMsix.FullName $msixPath -Force
    Sign-Msix $msixPath
} else {
    Write-Host "MSIX packaging skipped. Use -IncludeMsix for internal package builds." -ForegroundColor Yellow
}

$artifacts = Get-ChildItem $dist -File | Where-Object {
    $_.Name -like "gdriveHandler-$version-x64*" -or $_.Name -eq "install.ps1"
}

Write-Host "`nDone." -ForegroundColor Green
foreach ($artifact in $artifacts | Sort-Object Name) {
    $sizeMb = [math]::Round($artifact.Length / 1MB, 1)
    Write-Host "  $($artifact.FullName)  ($sizeMb MB)"
}
