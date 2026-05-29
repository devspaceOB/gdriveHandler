## Downloads

| File | Description |
|------|-------------|
| `gdriveHandler-x64.zip` | **Self-contained app (~84 MB download)** — bundles .NET 10 + Windows App SDK, no prerequisites. Runs on any Windows 10 (1809+) / 11 x64 machine. |
| `install.ps1` | PowerShell silent installer (see Quick Install below) |

## Quick Install (PowerShell)

```powershell
irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1 | iex
```

## Manual Install

1. Download and extract `gdriveHandler-x64.zip`
2. Run `gdriveHandler.exe --install` (or run `gdriveHandler.exe` and click **Install for me**)

Everything installs into one folder: `%LocalAppData%\Programs\gdriveHandler\`. Uninstall cleanly from **Settings → Apps**, or run `gdriveHandler.exe --uninstall`.

## What's new

See [ROADMAP.md](https://github.com/devspaceOB/gdriveHandler/blob/main/ROADMAP.md) for what's planned.
