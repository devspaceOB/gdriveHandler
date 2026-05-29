# gdriveHandler — Project Specification & Build Guide

> **Single source of truth for this project.** This document captures the goals, architecture, tech stack, build/deploy process, hard-won gotchas, and changelog. It is detailed enough to rebuild a near-identical project from scratch, and to onboard future iteration.

- **Repo:** https://github.com/devspaceOB/gdriveHandler
- **Current version:** 1.0.1
- **Author / publisher:** devSpaceOB (devspaceob@gmail.com)
- **License:** Personal Use (free for personal, non-commercial use) — see [LICENSE](LICENSE)
- **Platform:** Windows 10 (1809+) / Windows 11, x64

---

## 1. Overview

### Problem
Windows users with multiple Google accounts get the wrong browser profile when opening Google Workspace shortcut files (`.gdoc`, `.gsheet`, …). The file opens in whatever profile is active, often showing *"You need access."*

### Solution
gdriveHandler registers itself as the handler for Google Workspace shortcut extensions. When a shortcut is opened, it:
1. Reads the **email** and **doc ID** embedded in the file,
2. Scans all installed Chrome/Edge profiles for the one signed in with that email,
3. Launches that exact profile with the correct document URL.

It also provides a **WinUI 3 settings GUI** for installation, email-alias management, settings, logs, and diagnostics.

### Two execution modes (critical design point)
| Mode | Trigger | Behavior |
|------|---------|----------|
| **Headless** (hot path) | `gdriveHandler.exe <file>` | Parse → match → launch browser. **Never initializes WinUI/WinAppSDK** — must be fast. |
| **GUI** | `gdriveHandler.exe` (no args) or `--settings` | Boots WinUI 3, shows the NavigationView window. |

---

## 2. Tech Stack

| Layer | Choice | Notes |
|-------|--------|-------|
| Language | C# 13 (`LangVersion=latest`) | |
| Runtime | .NET 10 (`net10.0-windows10.0.26100.0`) | Pinned via `global.json` (`10.0.300`) |
| UI framework | WinUI 3 / Windows App SDK **2.0.1** | Unpackaged (`WindowsPackageType=None`) |
| Build tools | Microsoft.Windows.SDK.BuildTools `10.0.28000.1839` | |
| Min OS | `TargetPlatformMinVersion=10.0.22000.0` | Win11; runs on Win10 1809+ |
| JSON | `System.Text.Json` | No reflection; direct element access |
| Registry / shell | `Microsoft.Win32.Registry`, P/Invoke | File assoc, `SHChangeNotify`, COM `WScript.Shell` |
| Tests | xUnit (`Microsoft.NET.Test.Sdk` 17.x) | Pure-function tests, 43 cases |
| Build script | PowerShell 5.1+ (`build.ps1`) | |
| CI/CD | GitHub Actions (`windows-latest`) | Tag-triggered releases |
| Distribution | GitHub Releases (zip) + `install.ps1` (`irm \| iex`) | |

**No other NuGet packages.** Do not add CommunityToolkit or third-party UI libs.

---

## 3. Architecture

### Entry point — `Program.cs`
- `[STAThread] Main(string[] args)` is the **sole** entry point. The XAML-generated `Main` is disabled via `DISABLE_XAML_GENERATED_MAIN`.
- Wraps everything in try/catch → logs unhandled exceptions and shows a native error dialog.
- `Run()` routes: no args → GUI (home); `--xxx` flags → installer/diagnose/help/settings; otherwise → `HandleFile`.

### GUI bootstrap pattern (self-contained — **no Bootstrap.Initialize**)
```csharp
private static ExitCode LaunchGui(string initialPage)
{
    // Self-contained WinAppSDK: runtime DLLs ship next to the exe and load
    // directly. Do NOT call Bootstrap.Initialize (that searches for a framework
    // package that self-contained apps never register → fails).
    ComWrappersSupport.InitializeComWrappers();
    Application.Start(p =>
    {
        var ctx = new DispatcherQueueSynchronizationContext(
            DispatcherQueue.GetForCurrentThread());
        SynchronizationContext.SetSynchronizationContext(ctx);
        _ = new App(initialPage);
    });
    return ExitCode.Success;
}
```

### Headless file-handling flow — `HandleFile`
```
file → validate ext → read → ShortcutParser.Parse → UrlBuilder.BuildFinalUrl
     → Settings.Load → BrowserDiscovery.Discover → ProfileMatcher.FindBest(email)
     → (no match) alias retry → (no match) Chrome active profile / system default
     → BrowserLauncher.Launch
```

### Component responsibilities (`Core/`)
| File | Responsibility |
|------|----------------|
| `ShortcutParser.cs` | JSON-first parse with regex fallback; extracts email/docId/url/resourceKey (multiple key aliases) |
| `ShortcutInfo.cs` | Parsed result record |
| `UrlBuilder.cs` | Doc-ID normalization, URL template fill, resource-key append, scheme validation |
| `BrowserDiscovery.cs` | Enumerate Chrome/Edge channels via registry App Paths; rank by priority |
| `ProfileMatcher.cs` | Read each browser's `Local State` JSON; match `profile.info_cache[*].user_name` to email; expose gaia_id |
| `BrowserLauncher.cs` | Launch `--profile-directory=` with URL as escaped arg; fallback launch |
| `Settings.cs` | INI parse/serialize (`[settings]`, `[aliases]`); single-hop alias resolution; pure static `Parse`/`ToIni` for tests |
| `Logger.cs` | Append-only file log; all failures swallowed (logging must never break file opening) |
| `AppConstants.cs` | Identity, extensions, URL templates, all paths, `IsSystemInstall` |
| `NativeMethods.cs` | P/Invoke: `MessageBoxW`, `SHChangeNotify`, `AttachConsole`, `TaskDialog` |

### Installer (`Installer/Installer.cs`)
- **User install** (`--install`): HKCU `Software\Classes`, no elevation. `CopyAppFolder` copies the entire app folder into `InstallDir`.
- **System install** (`--install --system`): HKLM `SOFTWARE\Classes`; re-launches self with `runas` verb (UAC) if not admin.
- **Uninstall**: auto-detects type from exe location (`IsSystemInstall`), removes registry + Start-Menu shortcut, schedules self-delete of the install folder via detached `cmd /c timeout & rmdir`.
- Registers ProgID + 11 extensions + `OpenWithProgids` + ARP uninstall entry; fires `SHChangeNotify(SHCNE_ASSOCCHANGED)`.

### WinUI 3 UI
- `App.xaml` / `App.xaml.cs` — `Application`; **merges `<XamlControlsResources>`** (mandatory, see §8); has an `UnhandledException` handler that logs XAML errors.
- `MainWindow.xaml(.cs)` — `NavigationView` shell, Mica backdrop (`MicaController`, falls back gracefully), `Frame` navigation, custom window sizing (900×640).
- `Pages/` — `HomePage`, `GuidePage`, `AliasesPage`, `SettingsPage`, `LogViewerPage`, `AboutPage`. Each is a `Page` navigated into `ContentFrame`.

---

## 4. Project Structure

```
gdriveHandler/
├── .github/
│   ├── workflows/build.yml      # CI: tests, publish folder, zip, release on tag
│   └── RELEASE_NOTES.md         # Body for GitHub releases
├── src/gdriveHandler/
│   ├── Core/                    # UI-free business logic (10 files)
│   ├── Installer/Installer.cs   # User + system install/uninstall/repair
│   ├── Pages/                   # 6 WinUI 3 XAML pages (.xaml + .xaml.cs)
│   ├── Assets/                  # App.ico, d.ico
│   ├── App.xaml(.cs)            # Application + XamlControlsResources + UnhandledException
│   ├── MainWindow.xaml(.cs)     # NavigationView shell + Mica
│   ├── Program.cs               # Entry point (dual-mode)
│   ├── Ui.cs                    # Native TaskDialog / MessageBox helpers
│   ├── GlobalUsings.cs
│   ├── app.manifest             # PerMonitorV2 DPI, ComCtl v6, UTF-8, SegmentHeap
│   └── gdriveHandler.csproj
├── tests/
│   ├── gdriveHandler.Tests/     # xUnit (ShortcutParser, UrlBuilder, Settings)
│   └── sample-files/            # .gdoc/.gsheet/.gslides/.glink fixtures
├── tools/                       # make-icon.ps1, icon-preview.png
├── build.ps1                    # Build + test + zip
├── install.ps1                  # irm|iex silent installer
├── global.json                  # Pins .NET SDK 10.0.300
├── gdriveHandler.slnx           # Solution
├── README.md  ROADMAP.md  PROJECT.md  LICENSE
├── .gitignore  .gitattributes
```

---

## 5. Key Project Files (verbatim, for rebuild)

### `gdriveHandler.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.22000.0</TargetPlatformMinVersion>
    <AssemblyName>gdriveHandler</AssemblyName>
    <RootNamespace>GdriveHandler</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <InvariantGlobalization>true</InvariantGlobalization>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\App.ico</ApplicationIcon>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
    <Version>1.0.1</Version>
    <Company>devSpaceOB</Company>
    <Product>gdriveHandler</Product>
    <AssemblyTitle>gdriveHandler</AssemblyTitle>
    <!-- Folder deployment (NOT single-file): see PROJECT.md §8 -->
    <EnableMsixTooling>true</EnableMsixTooling>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <DebugType>embedded</DebugType>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="gdriveHandler.Tests" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.0.1" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000.1839" />
  </ItemGroup>
</Project>
```

### `App.xaml` (the `XamlControlsResources` merge is mandatory)
```xml
<?xml version="1.0" encoding="utf-8"?>
<Application x:Class="GdriveHandler.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
    </Application.Resources>
</Application>
```

---

## 6. Installation Model & Paths

### Decision: user install = one folder; system install splits (unavoidable)
| | User install (`--install`) | System install (`--install --system`) |
|--|--|--|
| Elevation | None | UAC required |
| Exe + DLLs | `%LOCALAPPDATA%\Programs\gdriveHandler\` | `%ProgramFiles%\gdriveHandler\` |
| `config.ini` | same folder | `%LOCALAPPDATA%\Programs\gdriveHandler\` (per-user data can't live in ProgramFiles) |
| `logs\` | `…\gdriveHandler\logs\` | per-user as above |
| Registry | HKCU `Software\Classes` | HKLM `SOFTWARE\Classes` |

`AppConstants` path properties:
```
InstallDir       = %LOCALAPPDATA%\Programs\gdriveHandler
InstalledExePath = InstallDir\gdriveHandler.exe
ConfigFile       = InstallDir\config.ini
LogDir           = InstallDir\logs
LogFile          = InstallDir\logs\launcher.log
SystemInstallDir = %ProgramFiles%\gdriveHandler
IsSystemInstall  = (running exe is under %ProgramFiles%)
```

> **Clean-uninstall trade-off:** because config lives in the single install folder, uninstall removes it too. To preserve aliases across reinstalls, move config to a separate `%LOCALAPPDATA%\gdriveHandler\` dir (was the pre-1.0 layout).

---

## 7. Build & Publish

### Local build
```powershell
.\build.ps1                 # tests + self-contained folder + zip
.\build.ps1 -SkipTests
```
Outputs:
- `dist\gdriveHandler-x64\` — published app folder (~215 MB, ~333 files)
- `dist\gdriveHandler-x64.zip` — release asset (~84 MB)

### Publish command (what build.ps1 runs)
```powershell
dotnet publish src\gdriveHandler\gdriveHandler.csproj `
  -c Release -r win-x64 --self-contained true `
  -o dist\gdriveHandler-x64 --nologo
# then: Compress-Archive dist\gdriveHandler-x64\* -> dist\gdriveHandler-x64.zip
```

### Prerequisites to build
- Windows 10 1809+/11 x64
- .NET 10 SDK (10.0.300, per `global.json`)
- PowerShell 5.1+
- NuGet restore pulls WinAppSDK 2.0.1 automatically (no Visual Studio needed)

---

## 8. ⚠️ Critical Deployment Gotchas (do NOT regress)

These cost real debugging time. The GUI silently fail-fasted (no window, no error) until all four were fixed:

1. **Ship as a FOLDER, never single-file.**
   `PublishSingleFile` self-extracts the WinAppSDK native DLLs to `%TEMP%\.net\...`, which fail-fasts on load (`CoreMessagingXP.dll`, exit `0xc0000602`). The supported, stable layout is exe + DLLs together in one folder, zipped for release.

2. **Self-contained ⇒ do NOT call `Bootstrap.Initialize`.**
   The bootstrapper hunts for a registered WinAppSDK *framework package*, which self-contained apps never install. Calling it throws/crashes. Just `ComWrappersSupport.InitializeComWrappers()` + `Application.Start()`.

3. **`App.xaml` MUST merge `<XamlControlsResources>`.**
   Without it, `NavigationView` and other Fluent controls can't resolve theme keys (e.g. `TabViewButtonBackground`) → `XamlParseException` at `InitializeComponent()` → fail-fast.

4. **Use `<FontIcon Glyph="..."/>`, not `Icon="Symbol"` strings, on NavigationViewItem.**
   `Icon="Info"` crashes — `Info` is not a valid `Symbol` enum value. Glyphs used: Home `E80F`, Guide/Help `E897`, Aliases/People `E716`, Settings `E713`, Logs/Page `E7C3`, About/Info `E946`.

**Debugging aid baked in:** `App.UnhandledException` logs the real XAML exception to `launcher.log` (otherwise startup crashes leave no trace). Diagnose future GUI crashes by checking `%LOCALAPPDATA%\Programs\gdriveHandler\logs\launcher.log` and the Windows **Application** event log (faulting module + exception code).

---

## 9. CLI Reference & Exit Codes

```
gdriveHandler <file>               Open a Google Workspace shortcut (handler use)
gdriveHandler                      Open the settings GUI (Home)
gdriveHandler --settings           Open the settings GUI
gdriveHandler --install            Install for current user (no admin)
gdriveHandler --install --system   Install for all users (UAC)
gdriveHandler --uninstall          Remove associations + uninstall (auto-detects scope)
gdriveHandler --repair             Re-register associations
gdriveHandler --diagnose [file]    List browsers/profiles; optionally parse a file
gdriveHandler --help               Usage
```

| Code | Meaning | Code | Meaning |
|--|--|--|--|
| 0 | Success | 7 | URL/doc-id not found |
| 2 | Invalid arguments | 8 | Browser launch failed |
| 3 | File not found | 10 | Install failed |
| 4 | Unsupported extension | 11 | Uninstall failed |
| 5 | Parse failed | 99 | Unhandled exception |

---

## 10. Config File (`config.ini`)

```ini
[settings]
openInNewWindow=false    ; true = add --new-window when launching Chrome/Edge
includeEdge=true         ; false = skip Edge profiles in matching

[aliases]
; old address in shortcut  =  current signed-in address
user@gmail.com=user@domain.com
```
- Hand-written INI, no dependency. Parsing is pure & unit-tested.
- Alias resolution is **single-hop**, case-insensitive.

---

## 11. Supported Extensions

**URL-template (need doc ID):** `.gdoc .gsheet .gslides .gdraw .gform .gscript`
**URL-only (file must carry a usable URL):** `.gmap .glink .gsite .gtable .gjam`

Templates (`{0}` = doc ID):
```
.gdoc    https://docs.google.com/document/d/{0}/edit
.gsheet  https://docs.google.com/spreadsheets/d/{0}/edit
.gslides https://docs.google.com/presentation/d/{0}/edit
.gdraw   https://docs.google.com/drawings/d/{0}/edit
.gform   https://docs.google.com/forms/d/{0}/edit
.gscript https://script.google.com/d/{0}/edit
```

---

## 12. Testing

```powershell
dotnet test tests\gdriveHandler.Tests\gdriveHandler.Tests.csproj -c Release -r win-x64
```
- 43 xUnit tests over pure functions: `ShortcutParser`, `UrlBuilder`, `Settings`.
- `InternalsVisibleTo` exposes internals to the test project.
- Business logic is deliberately I/O-free so it stays deterministic.

---

## 13. CI/CD & Release Process

`.github/workflows/build.yml` (trigger: push tag `v*.*.*`, or manual):
1. Checkout → setup .NET 10 → restore
2. `dotnet test`
3. `dotnet publish` self-contained → `dist/gdriveHandler-x64/`
4. `Compress-Archive` → `dist/gdriveHandler-x64.zip`
5. On tag: `softprops/action-gh-release@v2` creates the release with `gdriveHandler-x64.zip` + `install.ps1`, body from `.github/RELEASE_NOTES.md`

### Cutting a release
```powershell
# bump <Version> in gdriveHandler.csproj AND Version in Core/AppConstants.cs
git commit -am "release vX.Y.Z"
git tag -a vX.Y.Z -m "gdriveHandler vX.Y.Z"
git push origin main --tags        # CI builds + publishes the release
```
Token note: the gh CLI token needs `repo` + `workflow` scopes to push `.github/workflows`.

### Silent install (end user)
```powershell
irm https://raw.githubusercontent.com/devspaceOB/gdriveHandler/main/install.ps1 | iex
```
`install.ps1` fetches the latest release zip, extracts it, and runs `gdriveHandler.exe --install`.

---

## 14. Roadmap

See [ROADMAP.md](ROADMAP.md) for the full feature list, v2.0 ideas, and known limitations. Highlights to revisit:
- Multi-hop alias chains
- Framework-dependent build option (smaller download) and ARM64
- Auto-update via GitHub Releases API
- Visual profile picker when no match is found
- Code-signing the binaries (removes SmartScreen "unknown publisher" warning)

---

## 15. Changelog

> Format: [Keep a Changelog](https://keepachangelog.com/) style. Newest first. Update on every release.

### [1.0.1] — 2026-05-29
**Fixed — the GUI now launches** (it previously fail-fasted with no window). Four root causes:
- Switched from `PublishSingleFile` to **self-contained folder deployment** (single-file extracted WinAppSDK native DLLs to `%TEMP%` and crashed on load). Release asset is now `gdriveHandler-x64.zip`.
- `App.xaml` now merges `<XamlControlsResources>` (NavigationView couldn't resolve theme keys → `XamlParseException`).
- Replaced `NavigationViewItem Icon="…"` Symbol strings with explicit `<FontIcon>` glyphs (`Icon="Info"` is not a valid Symbol).
- Removed the incorrect `Bootstrap.Initialize` call (wrong for self-contained apps).

**Changed**
- Installer now copies the entire app folder (`CopyAppFolder`), not a single exe.
- `build.ps1` produces a folder + zip; CI updated to match.
- Added `App.UnhandledException` handler that logs XAML errors to `launcher.log`.

### [1.0.0] — 2026-05-29  *(release deleted; superseded by 1.0.1)*
- Initial public release. Converted the app from WPF (code-only window) to **WinUI 3** (NavigationView, Mica, 6 pages: Home/Guide/Aliases/Settings/Logs/About).
- Restructured into `Core/`, `Installer/`, `Pages/`.
- Consolidated user install to one folder (`%LOCALAPPDATA%\Programs\gdriveHandler\`).
- Added system-wide install option (HKLM + UAC).
- Added README, ROADMAP, Personal Use LICENSE, GitHub Actions CI, `install.ps1`.
- *(Binaries in this release were broken — single-file GUI crash; do not use.)*

---

## 16. Conventions for Future Work

- Keep namespace `GdriveHandler` for all files; `internal` visibility for non-UI types.
- Never let logging or shortcut helpers throw on the headless path.
- Do not initialize WinUI/WinAppSDK on the file-handling hot path.
- Re-read §8 before touching the csproj publish settings, `App.xaml`, or nav icons.
- Bump version in **both** `gdriveHandler.csproj` and `Core/AppConstants.cs`.
- Run `build.ps1` (full, with tests) before tagging a release.
- After any GUI change, launch the published exe and confirm a window appears (the log shows the init sequence; a crash shows the XAML error).
