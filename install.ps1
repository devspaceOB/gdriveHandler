#Requires -Version 5.1
<#
.SYNOPSIS
    Installer and uninstaller for gdriveHandler.

.DESCRIPTION
    Downloads the latest release, prefers the smaller framework-dependent zip
    when the .NET Desktop Runtime is present and the app passes a smoke test,
    otherwise falls back to the self-contained zip. Uninstall is idempotent and
    removes app files, app data, shortcuts, and app-owned registry entries.

.PARAMETER Uninstall
    Remove gdriveHandler from the current user account.

.PARAMETER Channel
    Auto chooses framework-dependent when safe, then self-contained fallback.
    FrameworkDependent or SelfContained force a specific release asset.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [ValidateSet('Auto', 'FrameworkDependent', 'SelfContained')]
    [string]$Channel = 'Auto'
)

$ErrorActionPreference = 'Stop'
$repo = 'devspaceOB/gdriveHandler'
$appId = 'gdriveHandler'
$baseProgId = 'devSpaceOB.gdriveHandler'
$extensions = @('.gdoc', '.gsheet', '.gslides', '.gdraw', '.gform', '.gscript', '.gmap', '.glink', '.gsite', '.gtable', '.gjam')
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\gdriveHandler'
$installExe = Join-Path $installDir 'gdriveHandler.exe'
$dataDir = Join-Path $env:LOCALAPPDATA 'gdriveHandler'
$startMenuLink = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\gdriveHandler.lnk'
$uninstallSubKey = "Software\Microsoft\Windows\CurrentVersion\Uninstall\$appId"

function Write-Step { param([string]$Message) Write-Host "  $Message" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Message) Write-Host "  $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "  $Message" -ForegroundColor Yellow }
function Write-Fail { param([string]$Message) Write-Host "  $Message" -ForegroundColor Red; exit 1 }

function Get-LatestRelease {
    Write-Step "Fetching latest release from github.com/$repo ..."
    try {
        return Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'gdriveHandler-installer' }
    } catch {
        Write-Fail "Could not reach GitHub API.`n$_"
    }
}

function Get-ReleaseAsset {
    param(
        [Parameter(Mandatory = $true)]$Release,
        [Parameter(Mandatory = $true)]
        [ValidateSet('FrameworkDependent', 'SelfContained')]
        [string]$Kind
    )

    $pattern = if ($Kind -eq 'FrameworkDependent') { '*x64-fd.zip' } else { '*x64-selfcontained.zip' }
    $asset = $Release.assets | Where-Object { $_.name -like $pattern } | Select-Object -First 1
    if (-not $asset -and $Kind -eq 'SelfContained') {
        $asset = $Release.assets | Where-Object { $_.name -like '*x64.zip' -or $_.name -like '*.zip' } | Select-Object -First 1
    }
    return $asset
}

function Test-DotNetDesktopRuntime {
    param([int]$Major = 10)

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        return $false
    }

    try {
        $runtimes = & $dotnet.Source --list-runtimes 2>$null
        return [bool]($runtimes | Where-Object { $_ -match "^Microsoft\.WindowsDesktop\.App\s+$Major\." })
    } catch {
        return $false
    }
}

function New-TempInstallRoot {
    $root = Join-Path $env:TEMP ("gdriveHandler-install-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return $root
}

function Download-Asset {
    param(
        [Parameter(Mandatory = $true)]$Asset,
        [Parameter(Mandatory = $true)][string]$Path
    )

    Write-Step "Downloading $($Asset.name) ($([math]::Round($Asset.size / 1MB, 1)) MB)..."
    Invoke-WebRequest $Asset.browser_download_url -OutFile $Path -UseBasicParsing
}

function Expand-AssetAndFindExe {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$ExtractDir
    )

    Write-Step "Extracting..."
    Expand-Archive -Path $ZipPath -DestinationPath $ExtractDir -Force
    $exe = Get-ChildItem $ExtractDir -Recurse -Filter 'gdriveHandler.exe' | Select-Object -First 1
    if (-not $exe) {
        throw "gdriveHandler.exe not found in the downloaded archive."
    }
    return $exe.FullName
}

function Test-AppSmoke {
    param([Parameter(Mandatory = $true)][string]$ExePath)

    Write-Step "Running smoke test..."
    $process = Start-Process -FilePath $ExePath -ArgumentList '--smoke-test' -Wait -PassThru
    return ($process.ExitCode -eq 0)
}

function Install-FromAsset {
    param(
        [Parameter(Mandatory = $true)]$Asset,
        [Parameter(Mandatory = $true)]
        [ValidateSet('FrameworkDependent', 'SelfContained')]
        [string]$Kind,
        [switch]$SmokeTest
    )

    $tempRoot = New-TempInstallRoot
    $zipPath = Join-Path $tempRoot $Asset.name
    $extractDir = Join-Path $tempRoot 'extract'

    try {
        Download-Asset $Asset $zipPath
        $exe = Expand-AssetAndFindExe $zipPath $extractDir

        if ($SmokeTest -and -not (Test-AppSmoke $exe)) {
            throw "$Kind package failed the smoke test."
        }

        Write-Step "Installing for current user..."
        $process = Start-Process -FilePath $exe -ArgumentList '--install' -Wait -PassThru
        $code = $process.ExitCode
        if ($code -ne 0) {
            throw "Installation failed with exit code $code. Check $env:LOCALAPPDATA\gdriveHandler\logs\launcher.log"
        }
    } finally {
        Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Remove-RegistryClasses {
    param([Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey]$Root)

    $classes = $Root.OpenSubKey('Software\Classes', $true)
    if (-not $classes) {
        return
    }

    try {
        $classes.DeleteSubKeyTree($baseProgId, $false)
        foreach ($ext in $extensions) {
            $progId = "$baseProgId.$($ext.TrimStart('.').ToLowerInvariant())"
            $classes.DeleteSubKeyTree($progId, $false)

            $owp = $classes.OpenSubKey("$ext\OpenWithProgids", $true)
            if ($owp) {
                try {
                    $owp.DeleteValue($progId, $false)
                    $owp.DeleteValue($baseProgId, $false)
                } finally {
                    $owp.Dispose()
                }
            }

            $extKey = $classes.OpenSubKey($ext, $true)
            if ($extKey) {
                try {
                    $current = $extKey.GetValue($null) -as [string]
                    if ($current -eq $progId -or $current -eq $baseProgId) {
                        $extKey.DeleteValue('', $false)
                    }
                } finally {
                    $extKey.Dispose()
                }
            }
        }
    } finally {
        $classes.Dispose()
    }
}

function Remove-UninstallEntry {
    param([Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey]$Root)
    $Root.DeleteSubKeyTree($uninstallSubKey, $false)
}

function Remove-AppLeftovers {
    Write-Step "Removing leftover files, shortcuts, and registry entries..."

    if (Test-Path $startMenuLink) {
        Remove-Item $startMenuLink -Force -ErrorAction SilentlyContinue
    }

    try {
        Remove-RegistryClasses ([Microsoft.Win32.Registry]::CurrentUser)
        Remove-UninstallEntry ([Microsoft.Win32.Registry]::CurrentUser)
    } catch {
        Write-Warn "Could not remove every current-user registry entry: $($_.Exception.Message)"
    }

    if (Test-IsAdmin) {
        try {
            Remove-RegistryClasses ([Microsoft.Win32.Registry]::LocalMachine)
            Remove-UninstallEntry ([Microsoft.Win32.Registry]::LocalMachine)
        } catch {
            Write-Warn "Could not remove every machine-wide registry entry: $($_.Exception.Message)"
        }
    }

    foreach ($path in @($installDir, $dataDir)) {
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-Uninstall {
    $ranAppUninstaller = $false
    if (Test-Path $installExe) {
        Write-Step "Running installed uninstaller..."
        $process = Start-Process -FilePath $installExe -ArgumentList '--uninstall --quiet' -Wait -PassThru
        if ($process.ExitCode -ne 0) {
            Write-Warn "Installed uninstaller exited with $($process.ExitCode); continuing cleanup."
        } else {
            $ranAppUninstaller = $true
        }
    }

    Remove-AppLeftovers
    if ($ranAppUninstaller) {
        Write-Ok "gdriveHandler uninstalled successfully."
    } else {
        Write-Ok "gdriveHandler is not installed; known leftovers were cleaned."
    }
}

function Invoke-Install {
    $release = Get-LatestRelease
    $version = $release.tag_name
    Write-Ok "Latest: $version"
    Write-Step "Channel: $Channel"

    $fdAsset = Get-ReleaseAsset $release 'FrameworkDependent'
    $selfAsset = Get-ReleaseAsset $release 'SelfContained'
    if (-not $selfAsset) {
        Write-Fail "No self-contained zip asset found in release $version."
    }

    if ($Channel -eq 'SelfContained') {
        Install-FromAsset $selfAsset 'SelfContained'
        Write-Ok "gdriveHandler $version installed successfully."
        return
    }

    if ($Channel -eq 'FrameworkDependent' -or $Channel -eq 'Auto') {
        $hasRuntime = Test-DotNetDesktopRuntime 10
        if ($hasRuntime -and $fdAsset) {
            try {
                Install-FromAsset $fdAsset 'FrameworkDependent' -SmokeTest
                Write-Ok "gdriveHandler $version installed successfully."
                Write-Host "  Package: framework-dependent" -ForegroundColor DarkGray
                return
            } catch {
                if ($Channel -eq 'FrameworkDependent') {
                    Write-Fail "Framework-dependent install failed.`n$_"
                }
                Write-Warn "Framework-dependent package failed; falling back to self-contained."
            }
        } elseif ($Channel -eq 'FrameworkDependent') {
            Write-Fail "Required .NET Desktop Runtime 10.x or framework-dependent zip is missing."
        } else {
            Write-Step "Required .NET Desktop Runtime 10.x not found; using self-contained package."
        }
    }

    Install-FromAsset $selfAsset 'SelfContained'
    Write-Ok "gdriveHandler $version installed successfully."
    Write-Host "  Package: self-contained" -ForegroundColor DarkGray
}

if ($env:GDRIVEHANDLER_INSTALLER_TEST_MODE -eq '1') {
    return
}

Write-Host "`ngdriveHandler installer" -ForegroundColor White
Write-Host "=======================`n" -ForegroundColor DarkGray

if ($Uninstall) {
    Invoke-Uninstall
    exit 0
}

Invoke-Install
Write-Host "  Location: $installDir" -ForegroundColor DarkGray
Write-Host "  Open 'gdriveHandler' from the Start Menu to configure.`n" -ForegroundColor DarkGray
