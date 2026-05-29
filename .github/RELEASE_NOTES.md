## Downloads

| File | Description |
|------|-------------|
| `gdriveHandler-<version>-x64-selfcontained.zip` | Portable app folder with .NET and Windows App SDK runtime files bundled. |
| `gdriveHandler-<version>-x64-fd.exe` | Framework-dependent launcher asset for advanced/manual scenarios. |
| `install.ps1` | PowerShell installer that downloads the zip and runs `gdriveHandler.exe --install`. |

## Quick Install

```powershell
irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1 | iex
```

## First Launch Self-Install

You can also download and extract the self-contained zip, then run `gdriveHandler.exe`.
If the app is not installed for your user yet, it will offer to install itself,
register Google Workspace file associations, relaunch from the installed location,
and close the portable copy.

## Uninstall

```powershell
$s = irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1
& ([scriptblock]::Create($s)) -Uninstall
```

Settings and aliases live under `%LOCALAPPDATA%\gdriveHandler\` and survive uninstall/reinstall.
