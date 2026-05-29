# gdriveHandler — Project Specification & Build Guide

> **Single source of truth for this project.** This document captures the goals, architecture, tech stack, build/deploy process, hard-won gotchas, and changelog. It is detailed enough to rebuild a near-identical project from scratch, and to onboard future iteration.

- **Repo:** https://github.com/devspaceOB/gdriveHandler
- **Current version:** 1.1.0
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
| UI framework | WinUI 3 / Windows App SDK **2.0.1** | MSIX primary; portable build passes `WindowsPackageType=None` |
| Build tools | Microsoft.Windows.SDK.BuildTools `10.0.26100.8249` + MSIX `1.7.260518100` | |
| Min OS | `TargetPlatformMinVersion=10.0.22000.0` | Win11; runs on Win10 1809+ |
| JSON | `System.Text.Json` | No reflection; direct element access |
| Registry / shell | `Microsoft.Win32.Registry`, P/Invoke | Legacy zip file assoc, `SHChangeNotify`, COM `WScript.Shell` |
| Tests | xUnit (`Microsoft.NET.Test.Sdk` 17.x) | Pure-function tests, 53 cases |
| Build script | PowerShell 5.1+ (`build.ps1`) | |
| CI/CD | GitHub Actions (`windows-latest`) | Tag-triggered releases |
| Distribution | GitHub Releases (signed MSIX + cert, zip fallback) + `install.ps1` (`irm \| iex`) | |

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
- `App.xaml` / `App.xaml.cs` — `Application`; **merges `<XamlControlsResources>`** (mandatory, see §8); `UnhandledException` handler logs XAML errors; `SwitchLanguage()` recreates the window in place for the in-app language switch.
- `MainWindow.xaml(.cs)` — `NavigationView` shell (`OpenPaneLength=220`), Mica backdrop (`MicaController`, falls back gracefully), `Frame` navigation, custom **extended title bar** (app-icon favicon + theme-following title), `AppWindow.SetIcon`, **DPI-aware** sizing (base 1000×700 scaled by `GetDpiForWindow`, centered on the work area).
- `Pages/` — `HomePage`, `GuidePage`, `AliasesPage`, `SettingsPage`, `AboutPage`. Settings hosts **General / Advanced / Logs** subtabs (`SelectorBar`); Advanced+Logs are gated by the **Advanced Settings** toggle. (`LogViewerPage` was folded into Settings → Logs and removed in 1.1.0.)
- Localization: `Core/Loc.cs` (ResourceLoader wrapper) + `Strings/{en-US,tr-TR}/Resources.resw` — `x:Uid` in XAML, `Loc.Get(key)` in code-behind. English + Türkçe, selectable in Settings.

---

## 4. Project Structure

```
gdriveHandler/
├── .github/
│   ├── workflows/build.yml      # CI: tests, publish folder, zip, release on tag
│   └── RELEASE_NOTES.md         # Body for GitHub releases
├── src/gdriveHandler/
│   ├── Core/                    # UI-free business logic + Loc.cs (localization helper)
│   ├── Installer/Installer.cs   # User + system install/uninstall/repair
│   ├── Pages/                   # 5 WinUI 3 XAML pages (Home/Guide/Aliases/Settings/About)
│   ├── Strings/                 # en-US/ + tr-TR/ Resources.resw (localization)
│   ├── Assets/                  # App.ico, d.ico, AppLogo.png
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
    <DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
    <Version>1.1.0</Version>
    <Company>devSpaceOB</Company>
    <Product>gdriveHandler</Product>
    <AssemblyTitle>gdriveHandler</AssemblyTitle>
    <!-- Folder deployment (NOT single-file): see PROJECT.md §8 -->
    <EnableMsixTooling>true</EnableMsixTooling>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <GenerateAppxPackageOnBuild>false</GenerateAppxPackageOnBuild>
    <AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>
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

**GUI / localization gotchas added in 1.1.0 (do NOT regress):**

5. **`AppWindow.Resize` takes PHYSICAL pixels, not logical.** On a 125%/150%/175% display a raw `Resize(900,640)` comes up tiny/garbled. Scale by `GetDpiForWindow(hwnd)/96.0` (see `MainWindow.SetupWindow`).
6. **Images referenced from `Pages/*.xaml` need an absolute `ms-appx:///Assets/...` URI.** A relative `Source="Assets/AppLogo.png"` on a page under `Pages/` resolves to `ms-appx:///Pages/Assets/...` and silently shows nothing. Root-level `MainWindow.xaml` can use the relative form.
7. **`Assets\App.ico` must be `Content` (copied to output)** for `AppWindow.SetIcon` to find it at runtime (taskbar/thumbnail icon). `<ApplicationIcon>` only embeds the icon into the exe — it does not place the file next to the exe.
8. **Localization wiring:** `InvariantGlobalization=false`; `.resw` under `Strings/**` are auto-discovered (no explicit `PRIResource`); the built PRI is `gdriveHandler.pri` (assembly-name convention for unpackaged apps), loaded by `Core/Loc.cs`. Set `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride` **before** `Application.Start` (see `Program.LaunchGui`). The in-app language switch (`App.SwitchLanguage`) sets the override, calls **`Loc.Reset()`** (drops the cached `ResourceLoader` so code-behind strings re-resolve), then recreates the window — no process restart.

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

### [1.1.0] — 2026-05-29
**GUI overhaul + Türkçe localization (Phase 1).**

**Fixed**
- **Garbled window size** on high-DPI: `AppWindow.Resize` uses physical pixels; now DPI-scaled (base 1000×700) and centered.
- **Title bar white in Dark mode**: replaced the default caption with a custom extended, theme-following title bar that also shows the app icon (favicon).
- **App icon missing in-GUI**: Home hero and About now show the real icon (`Assets/AppLogo.png` via `ms-appx:///`); window/taskbar/thumbnail icon set via `AppWindow.SetIcon` (`App.ico` now shipped as Content). Replaced the About "G" placeholder.
- Settings toggle/combo right-alignment (`MinWidth=0` removes the empty ToggleSwitch content gap); consistent page width (`MaxWidth=720`); tighter edge padding (24,20).

**Changed**
- **Navigation**: Logs removed from the top-level nav. Settings reorganized into **General / Advanced / Logs** subtabs (`SelectorBar`); Advanced + Logs appear only when the new **Advanced Settings** toggle is on. Install / Install-system / Repair / Reinstall / Uninstall, Open config, Open logs, diagnostics, Paths, and the log viewer all moved into Settings → Advanced/Logs. `LogViewerPage` removed.
- **Home** redesigned: hero + status card with a single "Set up" CTA when not installed + plain-English "How it works".
- **Guide** rewritten: sectioned, plain English, bullet/numbered lists; removed the Gmail→Workspace section; `.fyi`→`.com` in the example.
- **Dialogs**: Detect-profiles is now a larger card `ListView`; diagnostics enlarged.
- Narrower nav pane (`OpenPaneLength=220`).

**Added**
- **Türkçe** localization across all user-facing UI: `Core/Loc.cs` + `Strings/{en-US,tr-TR}/Resources.resw` (121 keys), `x:Uid` in XAML. Language selector in Settings; **in-process** switch (no restart) via `App.SwitchLanguage` + `Loc.Reset`.
- `Language` and `AdvancedMode` settings in `Settings.cs` (pure Parse/ToIni + tests). `GetDpiForWindow` P/Invoke. `InvariantGlobalization=false`. `Assets/AppLogo.png`.
- Tests: **53** (added Language + AdvancedMode round-trips).

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

---

## 17. Phase 2 — MSIX packaging + self-signed signing (PARTIALLY IMPLEMENTED)

> Phase 1 (GUI overhaul + Türkçe, shipped in 1.1.0) was done on the current **unpackaged** folder/zip model. Phase 2 migrates distribution to a **signed MSIX** and is intended to be implemented next. It rewrites the install model, so the in-app Advanced "management" buttons (install/repair/uninstall) become Windows-managed and must be revised. Decisions already made: **free self-signed cert** stored as a GitHub Actions secret; keep the zip as a fallback channel.

### 17.1 Single-project MSIX
- Add `src/gdriveHandler/Package.appxmanifest`: Identity (`Name`/`Publisher` **must match** the signing cert subject), DisplayName, visual assets (`Square44x44Logo`, `Square150x150Logo`, `StoreLogo`, splash — generate from the icon), and a `<uap:Extension Category="windows.fileTypeAssociation">` block declaring **all 11 extensions** (`.gdoc .gsheet .gslides .gdraw .gform .gscript .gmap .glink .gsite .gtable .gjam` — from `AppConstants.AllExtensions`).
- `gdriveHandler.csproj`: remove `<WindowsPackageType>None</WindowsPackageType>` (→ packaged), keep `EnableMsixTooling=true` and `WindowsAppSDKSelfContained=true`, configure `GenerateAppxPackageOnBuild` for CI.

### 17.2 Activation refactor (`Program.cs`)
Packaged file-association launches do NOT arrive via `args[]`. Use `Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs()`:
- `ExtendedActivationKind.File` → `FileActivatedEventArgs.Files[0].Path` → existing `HandleFile` headless fast path (must still **not** init WinUI).
- `Launch`/no file → GUI. Preserve the dual-mode speed guarantee (§3).

### 17.3 Writable paths (`Core/AppConstants.cs`)
MSIX installs read-only under `WindowsApps`. Move `config.ini` + `logs\` to **`%LOCALAPPDATA%\gdriveHandler\`** (outside the install dir) for both packaged and unpackaged builds. Bonus: fixes the "uninstall wipes config/aliases" trade-off in §6.

### 17.4 Installer changes (`Installer/Installer.cs` + GUI)
Under MSIX, associations are declarative and install/uninstall are Windows-managed. The Advanced → "Setup & management" section becomes "Managed by Windows" + a button to open Apps & Features; the registry `RegisterProgId/RegisterExtensions/WriteUninstallEntry` path is kept only for the legacy unpackaged build (or removed if MSIX becomes the sole channel).

### 17.5 Self-signed signing (free) — exact steps
1. **Generate once:** `New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=devSpaceOB" -KeyUsage DigitalSignature -CertStoreLocation Cert:\CurrentUser\My` — `CN` **must equal** the manifest `Identity Publisher`. Export `.pfx` (password) and `.cer` (public).
2. **GitHub secrets:** base64 the `.pfx` → `SIGNING_PFX_BASE64`; password → `SIGNING_PFX_PASSWORD`.
3. **CI signs every build:** decode → `cert.pfx`, build MSIX, then `signtool sign /fd SHA256 /a /f cert.pfx /p <pwd> /tr http://timestamp.digicert.com /td SHA256 gdriveHandler.msix` (or `AppxPackageSigningEnabled=true` + thumbprint).
4. **`install.ps1` (irm|iex):** download `.msix` + `.cer`; import `.cer` to `LocalMachine\TrustedPeople` (and `Root`) — prompts for admin once; then `Add-AppxPackage gdriveHandler.msix`. Manual: double-click `.cer` → Install to Trusted People, then double-click `.msix`.
- Trade-off: self-signed still shows "unknown publisher"; importing the cert removes the *install* block. A paid OV/EV cert would remove SmartScreen entirely (deferred by choice).

### 17.6 CI/CD & release assets (`.github/workflows/build.yml`, `build.ps1`, `install.ps1`)
Produce: `gdriveHandler-<ver>-x64.msix` (primary, signed), `…-x64.cer` (public cert), `…-x64-selfcontained.zip` (portable fallback), `…-x64-fd.exe` (framework-dependent, tiny), `install.ps1`.

### 17.7 Clean install-folder layout (root = 2 files, rest in `\files\`) — user request, best-effort
Target on-disk layout for the **unpackaged folder / zip / `--install`** channel:
```
<install dir>\
├── gdriveHandler.exe
├── config.ini
└── files\      ← everything else (runtime DLLs, WinAppSDK, gdriveHandler.pri, Assets, *.dll/*.json deps)
```
A literal 2-files-only root is hard: the apphost normally needs `gdriveHandler.dll` + `.runtimeconfig.json` + `.deps.json` beside it, and native WinAppSDK DLLs load from the exe dir.
- **Recommended (robust):** relocate dependencies into `files\` — managed via `runtimeconfig` `additionalProbingPaths` → `files`; native via `SetDefaultDllDirectories` + `AddDllDirectory("<dir>\files")` at the **very top of `Main`** (cheap P/Invokes; must not slow the headless path). Yields root = exe + config + the small bootstrap trio, with the ~300-file bulk under `files\`.
- **Literal 2-file root (fallback):** a tiny stub launcher that adds `files\` to the DLL search path and loads the real app in-process — only if the recommended approach can't hide the trio (a relaunch would violate the §3 fast-path rule).
- For **MSIX** the internal layout is invisible to users, so this applies to the zip/`--install` channel; the MSIX keeps its standard layout.

### 17.8 Phase 2 verification
- Portable clean-layout work in §17.7 is still deferred; current zip fallback keeps the normal WinUI folder layout, while config/logs live under `%LOCALAPPDATA%\gdriveHandler\`.
- Build + sign MSIX locally; install the signed `.msix` after trusting the `.cer`; opening a `.gdoc` launches the correct profile (headless path); uninstall via Windows Settings is clean and `%LOCALAPPDATA%\gdriveHandler\` aliases survive; `install.ps1` works end-to-end on a clean VM.

### 17.9 Implementation notes
- `Package.appxmanifest` defines the MSIX identity (`devSpaceOB.gdriveHandler`, `CN=devSpaceOB`), full-trust desktop entry point, visual assets, and all 11 Google Workspace file associations.
- Packaged file activation is handled in `Program.cs` through `Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs()` and routes directly to the existing headless `HandleFile(...)` path.
- `config.ini` and logs now live under `%LOCALAPPDATA%\gdriveHandler\` for both MSIX and legacy zip installs.
- Packaged builds show Windows-managed setup/uninstall UI; legacy zip builds keep the HKCU/HKLM registry installer path.
- `build.ps1` produces `gdriveHandler-<ver>-x64.msix`, `gdriveHandler-<ver>-x64.cer` when signing with a PFX, `gdriveHandler-<ver>-x64-selfcontained.zip`, and `gdriveHandler-<ver>-x64-fd.exe`.
- The .NET 10 SDK plus current `Microsoft.Windows.SDK.BuildTools.MSIX` task requires `System.Security.Permissions.dll` beside the MSIX task assembly. `gdriveHandler.csproj` restores and copies that dependency before manifest generation so local and CI MSIX builds are reproducible.
- §17.7 clean install-folder layout has not been implemented yet; it remains a separate risky change because WinUI/native runtime probing can break both GUI and headless launch.
