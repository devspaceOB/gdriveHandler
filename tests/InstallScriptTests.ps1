#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $root 'install.ps1'
$env:GDRIVEHANDLER_INSTALLER_TEST_MODE = '1'
. $scriptPath
Remove-Item Env:\GDRIVEHANDLER_INSTALLER_TEST_MODE -ErrorAction SilentlyContinue

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function New-FakeRelease {
    [pscustomobject]@{
        tag_name = 'v9.9.9'
        assets = @(
            [pscustomobject]@{ name = 'gdriveHandler-9.9.9-x64-fd.exe'; size = 1; browser_download_url = 'https://example.invalid/fd.exe' },
            [pscustomobject]@{ name = 'gdriveHandler-9.9.9-x64-fd.zip'; size = 2; browser_download_url = 'https://example.invalid/fd.zip' },
            [pscustomobject]@{ name = 'gdriveHandler-9.9.9-x64-selfcontained.zip'; size = 3; browser_download_url = 'https://example.invalid/sc.zip' }
        )
    }
}

function With-FakeDotnet {
    param([string]$Output, [scriptblock]$Body)

    $dir = Join-Path $env:TEMP ("gdrivehandler-dotnet-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $cmd = Join-Path $dir 'dotnet.cmd'
    Set-Content -Path $cmd -Value "@echo off`r`necho $Output`r`nexit /b 0`r`n" -Encoding ASCII

    $oldPath = $env:PATH
    try {
        $env:PATH = "$dir;$oldPath"
        & $Body
    } finally {
        $env:PATH = $oldPath
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function New-ZipWithSuccessfulExe {
    param([string]$OutPath)

    $buildDir = Join-Path $env:TEMP ("gdrivehandler-test-exe-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
    try {
        $source = Join-Path $buildDir 'Program.cs'
        Set-Content -Path $source -Value 'public static class Program { public static int Main(string[] args) { return 0; } }' -Encoding ASCII
        Add-Type -OutputType ConsoleApplication -OutputAssembly (Join-Path $buildDir 'gdriveHandler.exe') -Path $source
        Compress-Archive -Path (Join-Path $buildDir 'gdriveHandler.exe') -DestinationPath $OutPath -Force
    } finally {
        Remove-Item $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$release = New-FakeRelease
Assert-Equal 'gdriveHandler-9.9.9-x64-fd.zip' (Get-ReleaseAsset $release FrameworkDependent).name 'Framework-dependent selection must use the zip asset.'
Assert-Equal 'gdriveHandler-9.9.9-x64-selfcontained.zip' (Get-ReleaseAsset $release SelfContained).name 'Self-contained selection must use the self-contained zip.'

With-FakeDotnet 'Microsoft.WindowsDesktop.App 10.0.0 [C:\dotnet\shared\Microsoft.WindowsDesktop.App]' {
    Assert-True (Test-DotNetDesktopRuntime 10) 'Desktop runtime 10.x should be detected.'
}
With-FakeDotnet 'Microsoft.NETCore.App 10.0.0 [C:\dotnet\shared\Microsoft.NETCore.App]' {
    Assert-True (-not (Test-DotNetDesktopRuntime 10)) 'Core runtime alone must not qualify for the FD package.'
}

$before = @(Get-ChildItem $env:TEMP -Directory -Filter 'gdriveHandler-install-*' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
function Download-Asset { throw 'synthetic download failure' }
try {
    Install-FromAsset ([pscustomobject]@{ name = 'broken.zip'; size = 1; browser_download_url = 'unused' }) SelfContained
    throw 'Install-FromAsset should have failed.'
} catch {
    Assert-True ($_.Exception.Message -match 'synthetic download failure') 'Expected synthetic failure.'
}
$after = @(Get-ChildItem $env:TEMP -Directory -Filter 'gdriveHandler-install-*' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
Assert-Equal 0 (@($after | Where-Object { $before -notcontains $_ }).Count) 'Temp install folders must be removed after failure.'

$zipPath = Join-Path $env:TEMP ("gdrivehandler-success-" + [guid]::NewGuid().ToString('N') + '.zip')
New-ZipWithSuccessfulExe $zipPath
function Download-Asset { param($Asset, [string]$Path) Copy-Item $script:zipPath $Path -Force }
try {
    $before = @(Get-ChildItem $env:TEMP -Directory -Filter 'gdriveHandler-install-*' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    Install-FromAsset ([pscustomobject]@{ name = 'success.zip'; size = 1; browser_download_url = 'unused' }) SelfContained
    $after = @(Get-ChildItem $env:TEMP -Directory -Filter 'gdriveHandler-install-*' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    Assert-Equal 0 (@($after | Where-Object { $before -notcontains $_ }).Count) 'Temp install folders must be removed after success.'
} finally {
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
}

function Remove-RegistryClasses { param($Root) }
function Remove-UninstallEntry { param($Root) }
$oldInstallExe = $script:installExe
$oldInstallDir = $script:installDir
$oldDataDir = $script:dataDir
$oldStartMenuLink = $script:startMenuLink
$sandbox = Join-Path $env:TEMP ("gdrivehandler-uninstall-" + [guid]::NewGuid().ToString('N'))
try {
    $script:installDir = Join-Path $sandbox 'install'
    $script:installExe = Join-Path $script:installDir 'gdriveHandler.exe'
    $script:dataDir = Join-Path $sandbox 'data'
    $script:startMenuLink = Join-Path $sandbox 'gdriveHandler.lnk'
    New-Item -ItemType Directory -Path $script:installDir, $script:dataDir -Force | Out-Null
    New-Item -ItemType File -Path $script:startMenuLink -Force | Out-Null

    Invoke-Uninstall

    Assert-True (-not (Test-Path $script:installDir)) 'Uninstall should remove the install directory.'
    Assert-True (-not (Test-Path $script:dataDir)) 'Uninstall should remove the data directory.'
    Assert-True (-not (Test-Path $script:startMenuLink)) 'Uninstall should remove the shortcut.'
} finally {
    $script:installExe = $oldInstallExe
    $script:installDir = $oldInstallDir
    $script:dataDir = $oldDataDir
    $script:startMenuLink = $oldStartMenuLink
    Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'InstallScriptTests passed.' -ForegroundColor Green
