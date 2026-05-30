# gdriveHandler

[![Build](https://github.com/devspaceOB/gdriveHandler/actions/workflows/build.yml/badge.svg)](https://github.com/devspaceOB/gdriveHandler/actions/workflows/build.yml)
[![Latest Release](https://img.shields.io/github/v/release/devspaceOB/gdriveHandler?label=release)](https://github.com/devspaceOB/gdriveHandler/releases/latest)
[![License: Personal Use](https://img.shields.io/badge/license-Personal%20Use-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2B-0078d4)](https://github.com/devspaceOB/gdriveHandler/releases/latest)

**Open Google Workspace shortcuts in the right Chrome/Edge profile — automatically.**

If you use multiple Google accounts, you know the frustration: double-click a `.gdoc` or `.gsheet` file and it opens in the wrong profile, showing _"You need access."_ gdriveHandler fixes this by reading the account email embedded in the shortcut file and routing it directly to the matching browser profile.

---

## Features

- **Automatic profile matching** — reads the email from `.gdoc`, `.gsheet`, `.gslides`, and 8 other formats, finds the Chrome/Edge profile signed in with that account, and launches it directly
- **Email alias support** — handles Gmail → Google Workspace conversions (`user@gmail.com` → `user@domain.com`)
- **Chrome and Edge** — searches all installed channels (Stable, Beta, Dev, Canary) on both browsers
- **No elevation required** — per-user install with no admin rights needed
- **WinUI 3 native UI** — Fluent Design, Mica backdrop, system dark/light theme
- **Zero-friction file handling** — the headless path (file opening) adds no UI overhead; only loading the settings window initializes the UI framework
- **One folder** — user install puts everything (exe + config + logs) under `%LocalAppData%\Programs\gdriveHandler\`
- **Clean uninstall** — removes all registry entries and file associations; your config is preserved for reinstalls

---

## Supported File Types

| Extension | Service |
|-----------|---------|
| `.gdoc` | Google Docs |
| `.gsheet` | Google Sheets |
| `.gslides` | Google Slides |
| `.gdraw` | Google Drawings |
| `.gform` | Google Forms |
| `.gscript` | Google Apps Script |
| `.gmap` | Google Maps |
| `.glink` | Google Drive generic link |
| `.gsite` | Google Sites |
| `.gtable` | Fusion Tables |
| `.gjam` | Google Jamboard |

---

## Installation

### Option A — One-liner (PowerShell)

```powershell
irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1 | iex
```

Downloads the latest release and installs it for your user account. If the .NET Desktop Runtime 10.x is present, the installer uses the smaller framework-dependent package after a smoke test; otherwise it falls back to the self-contained package. No admin required.

### Option B — Manual

1. Download **`gdriveHandler-<version>-x64-selfcontained.zip`** from the [latest release](https://github.com/devspaceOB/gdriveHandler/releases/latest)
2. Extract it anywhere, then run:
   ```
   gdriveHandler.exe --install
   ```
   This copies the app into `%LocalAppData%\Programs\gdriveHandler\` and registers the file associations. (Or just run `gdriveHandler.exe` and click **Install for me** on the Home page.)
3. If Windows asks you to choose a default app for `.gdoc`, select **gdriveHandler**

> **System-wide install** (all users, requires admin):
> ```
> gdriveHandler.exe --install --system
> ```

### After Installing

Open `.gdoc` or `.gsheet` files normally — gdriveHandler handles them automatically.

To configure aliases or settings, open **gdriveHandler** from the Start Menu (or run without arguments).

---

## Settings & Configuration

### GUI

Open **gdriveHandler** from the Start Menu. The settings window has six pages:

| Page | Description |
|------|-------------|
| **Home** | Install status, install/repair/uninstall buttons, utilities |
| **Guide** | How matching works, sample file format, Edge caveat |
| **Aliases** | Map old email addresses to current ones |
| **Settings** | Open-in-new-window toggle, Include Edge toggle |
| **Logs** | Tail view of the application log |
| **About** | Version, paths, GitHub link |

### config.ini

Located at `%LocalAppData%\gdriveHandler\config.ini` (user install).

```ini
[settings]
openInNewWindow=false    ; true = --new-window flag when launching Chrome/Edge
includeEdge=true         ; false = skip Edge profiles during matching

[aliases]
; Map shortcuts with old addresses to the current signed-in address
user@gmail.com=user@domain.com
old.name@gmail.com=new.name@company.org
```

Uninstall removes app files, file associations, logs, config, and aliases. Use Repair/Reinstall inside the app when you want to refresh registrations without deleting settings.

---

## Command-Line Reference

```
gdriveHandler <file>               Open a Google Workspace shortcut
gdriveHandler                      Open the settings GUI
gdriveHandler --settings           Open the settings GUI (same as above)
gdriveHandler --install            Install for current user (no admin)
gdriveHandler --install --system   Install for all users (admin required)
gdriveHandler --uninstall          Remove associations, app data, and uninstall
gdriveHandler --repair             Re-register file associations
gdriveHandler --diagnose [file]    List browsers/profiles; optionally parse a file
gdriveHandler --help               Show usage
```

### Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 2 | Invalid arguments |
| 3 | File not found |
| 4 | Unsupported extension |
| 5 | Parse failed |
| 7 | Could not determine URL from shortcut |
| 8 | Browser launch failed |
| 10 | Install failed |
| 11 | Uninstall failed |
| 99 | Unhandled exception |

---

## Builds

| File | Download | Installed | Requirements |
|------|----------|-----------|--------------|
| `gdriveHandler-<version>-x64-selfcontained.zip` | ~84 MB | ~215 MB | Windows 10 1809+ / Windows 11 x64 - **nothing else needed** |
| `gdriveHandler-<version>-x64-fd.zip` | smaller | smaller | Windows 10/11 x64 plus .NET Desktop Runtime 10.x |

The one-line installer selects the framework-dependent zip only when the required runtime is present and the app passes a smoke test. The self-contained zip bundles the .NET 10 runtime and Windows App SDK. Both variants ship as folders rather than a single `.exe`, because WinUI 3 needs its adjacent runtime files.

---

## Building from Source

### Prerequisites

- **Windows 10 1809+** or Windows 11 (x64)
- [**.NET 10 SDK**](https://dotnet.microsoft.com/download/dotnet/10.0) (version pinned in `global.json`)
- **Windows App SDK 2.0** workload (installed automatically by NuGet restore)
- [PowerShell 5.1+](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-windows) (for `build.ps1`)

### Steps

```powershell
# 1. Clone the repository
git clone https://github.com/devspaceOB/gdriveHandler.git
cd gdriveHandler

# 2. Build both variants (runs tests first)
.\build.ps1

# 3. Self-contained zip is in dist\gdriveHandler-<version>-x64-selfcontained.zip
# 4. Framework-dependent zip is in dist\gdriveHandler-<version>-x64-fd.zip
```

#### Build flags

```powershell
.\build.ps1 -SkipTests           # Skip unit tests
.\build.ps1 -Configuration Debug # Debug build
```

### Project Layout

```
gdriveHandler/
├── src/gdriveHandler/
│   ├── Core/           # Business logic — ShortcutParser, BrowserDiscovery, etc.
│   ├── Installer/      # Per-user and system-wide install/uninstall
│   ├── Pages/          # WinUI 3 XAML pages
│   ├── App.xaml(.cs)   # WinUI 3 Application
│   ├── MainWindow.xaml(.cs)  # NavigationView shell
│   └── Program.cs      # Entry point (dual-mode: headless file handler + GUI)
├── tests/              # xUnit tests for pure functions
├── tools/              # Icon generation scripts
├── build.ps1           # Build script
└── install.ps1         # PowerShell one-liner installer
```

---

## How It Works

1. Windows calls `gdriveHandler.exe <path-to-shortcut>` when you open a `.gdoc` / `.gsheet` / etc. file
2. gdriveHandler reads the shortcut (JSON) and extracts the embedded email address and document ID
3. It scans all installed Chrome and Edge profiles via their `Local State` JSON files
4. The profile whose `user_name` matches the email is launched with the correct URL
5. If no match is found, it tries the alias table from `config.ini`, then falls back to Chrome's active profile

---

## Roadmap

See [ROADMAP.md](ROADMAP.md) for planned features and known limitations.

---

## License

[Personal Use License](LICENSE) — free for personal, non-commercial use.
For commercial licensing inquiries, open an issue on GitHub.

---

*Built with WinUI 3 / Windows App SDK 2.0 / .NET 10*
