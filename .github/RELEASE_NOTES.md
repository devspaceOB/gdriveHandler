## v1.2.1

This release hardens the PowerShell installer and uninstall flow. The installer now prefers the smaller framework-dependent zip only when .NET Desktop Runtime 10.x is present and the app passes a smoke test; otherwise it falls back to the self-contained zip. Uninstall now performs full cleanup of app files, app data, shortcuts, and app-owned registry entries.

The app also detects installed copies more reliably, so the portable-folder install prompt no longer appears after a successful install.

## v1.2.0

This release adds bundled file icons for Google Workspace shortcut types. Legacy installs now register per-extension ProgIDs so Windows can show distinct icons for Docs, Sheets, Slides, Forms, Sites, and Drive shortcuts; unsupported icon types fall back to the app icon.

## Downloads

| File | Description |
|------|-------------|
| `gdriveHandler-<version>-x64-selfcontained.zip` | Portable app folder with .NET and Windows App SDK runtime files bundled. |
| `gdriveHandler-<version>-x64-fd.zip` | Smaller framework-dependent app folder for machines with .NET Desktop Runtime 10.x. |
| `install.ps1` | PowerShell installer that auto-selects FD zip when safe, otherwise uses self-contained zip. |

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

Uninstall removes app files, file associations, logs, config, aliases, and app-owned registry entries.
