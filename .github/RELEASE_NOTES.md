## Downloads

| File | Description |
|------|-------------|
| `gdriveHandler-<version>-x64.msix` | Primary installer package. Signed with the project self-signed certificate. |
| `gdriveHandler-<version>-x64.cer` | Public certificate used by `install.ps1` before installing the MSIX. |
| `gdriveHandler-<version>-x64-selfcontained.zip` | Portable fallback with .NET and Windows App SDK runtime files bundled. |
| `gdriveHandler-<version>-x64-fd.exe` | Framework-dependent launcher asset for advanced/manual scenarios. |
| `install.ps1` | PowerShell installer that prefers MSIX and falls back to the zip when needed. |

## Quick Install

```powershell
irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1 | iex
```

The MSIX install is managed by Windows. Uninstall from Windows Settings, or run:

```powershell
$s = irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1
& ([scriptblock]::Create($s)) -Uninstall
```

## Manual Install

1. Download `gdriveHandler-<version>-x64.cer` and install it to Trusted People.
2. Download and open `gdriveHandler-<version>-x64.msix`.
3. If MSIX install is unavailable, use the self-contained zip fallback and run `gdriveHandler.exe --install`.

Settings and aliases live under `%LOCALAPPDATA%\gdriveHandler\` and survive uninstall/reinstall.
