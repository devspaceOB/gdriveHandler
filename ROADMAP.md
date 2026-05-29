# gdriveHandler — Roadmap

This file tracks planned features, improvements, and known issues. Items are roughly prioritized; earlier sections are more likely to land in the next release.

---

## v1.x — Near-term improvements

### Core
- [ ] Multi-hop alias chains (A → B → C) — currently only single-hop
- [ ] Browser profile auto-detection refresh without restarting the app
- [ ] Support for `.gform` URL-only fallback (form links that don't embed a doc ID)
- [ ] Configurable fallback browser (currently hard-coded to Chrome → system default)
- [ ] Per-extension URL template overrides in config.ini

### Installer / Distribution
- [ ] Silent installer flag `--silent` (suppress all dialogs during install)
- [ ] Auto-update check via GitHub Releases API (opt-in, settings toggle)
- [ ] Windows Store / MSIX packaging option for enterprise distribution
- [ ] ARM64 build (`win-arm64` publish target)

### Settings UI
- [ ] In-app notification when a new release is available
- [ ] Import / export config.ini from the Aliases page
- [ ] Per-profile color tagging in the Aliases view
- [ ] Keyboard shortcuts for common actions

---

## v2.0 — Larger features

### Profile Management
- [ ] Visual profile picker when no match is found (instead of silent fallback)
- [ ] Remember last-used profile per email (MRU cache in config.ini)
- [ ] Firefox profile support

### Developer / Power User
- [ ] JSON-based config schema with schema validation (alternative to INI)
- [ ] Named rule sets: define multiple routing profiles and switch between them
- [ ] Webhook / pipe notifications when a file is opened (for automation)

### UI / UX
- [ ] Tray icon with quick-toggle for Include Edge / New Window settings
- [ ] In-app "Test shortcut" file picker to simulate routing without opening Chrome
- [ ] Compact / mini mode window

---

## Known Issues & Limitations

| # | Description | Workaround |
|---|-------------|------------|
| 1 | Edge profile matching only works when the Edge profile's **primary** signed-in account matches. Google accounts used in a guest or secondary Edge session are not detected. | Disable "Include Edge profiles" if false positives occur. |
| 2 | After installing, Windows may keep a different app as the default handler. | Open **Settings → Apps → Default apps → gdriveHandler** and set manually. |
| 3 | Self-contained exe is large (~150 MB) because it bundles .NET 10 and WinAppSDK. | Use the framework-dependent build if disk space is a concern. |
| 4 | System-wide install requires administrator elevation. | Use user-only install (default) — no admin needed. |

---

## Completed

- [x] WinUI 3 native UI (Mica backdrop, dark/light theme)
- [x] NavigationView shell (Home, Guide, Aliases, Settings, Logs, About)
- [x] Per-user install (no elevation) + per-machine install option
- [x] One-folder install: exe + config.ini + logs in a single directory (user install)
- [x] Email alias resolution (single-hop, persists across reinstalls)
- [x] Chrome and Edge multi-profile support
- [x] All Google Workspace shortcut types: .gdoc, .gsheet, .gslides, .gdraw, .gform, .gscript, .gmap, .glink, .gsite, .gtable, .gjam
- [x] Headless file-handler path (zero GUI overhead for normal use)
- [x] Diagnostic mode (`--diagnose`)
- [x] Self-contained + framework-dependent release builds
- [x] GitHub Actions CI with automated releases
- [x] PowerShell one-liner silent install
