using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace GdriveHandler;

/// <summary>
/// Install/uninstall/repair for per-user (HKCU, no elevation) and system-wide
/// (HKLM, requires admin / UAC elevation) deployments.
///
/// Per-user:   --install            (HKCU, no elevation)
/// System:     --install --system   (HKLM, UAC prompt if not admin)
/// Uninstall:  --uninstall          (auto-detects install type from exe location)
/// </summary>
internal static class Installer
{
    private const string ClassesRoot = @"Software\Classes";
    private const string HklmClassesRoot = @"SOFTWARE\Classes";
    private const string UninstallSubKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppConstants.AppId;
    private const string HklmUninstallSubKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + AppConstants.AppId;

    // Per-user Start Menu (APPDATA)
    private static string UserStartMenuLinkPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs",
            AppConstants.AppId + ".lnk");

    // All-users Start Menu (ALLUSERSPROFILE / CommonApplicationData)
    private static string SystemStartMenuLinkPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Windows\Start Menu\Programs",
            AppConstants.AppId + ".lnk");

    // ----- public API -----

    public static ExitCode Install(Logger log, bool systemWide = false)
    {
        if (systemWide)
        {
            return InstallSystem(log);
        }

        return InstallUser(log);
    }

    /// <summary>Re-register everything; idempotent.</summary>
    public static ExitCode Repair(Logger log)
    {
        return Install(log, systemWide: AppConstants.IsSystemInstall);
    }

    public static ExitCode Uninstall(Logger log)
    {
        return AppConstants.IsSystemInstall ? UninstallSystem(log) : UninstallUser(log);
    }

    // ----- per-user install (HKCU, no elevation) -----

    private static ExitCode InstallUser(Logger log)
    {
        try
        {
            var src = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine the current executable path.");

            Directory.CreateDirectory(AppConstants.InstallDir);
            var dest = AppConstants.InstalledExePath;
            if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(src, dest, overwrite: true);
                log.Info($"Copied executable to {dest}");
            }

            RegisterProgId(Registry.CurrentUser, ClassesRoot, dest, log);
            RegisterExtensions(Registry.CurrentUser, ClassesRoot, log);
            WriteUninstallEntry(Registry.CurrentUser, UninstallSubKey, dest, AppConstants.InstallDir, log);
            CreateShortcut(UserStartMenuLinkPath, dest, log);
            NotifyShell();

            log.Info("User install complete.");
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            log.Error($"User install failed: {ex}");
            Ui.ShowError("Installation failed." + Environment.NewLine + Environment.NewLine + ex.Message);
            return ExitCode.InstallFailed;
        }
    }

    // ----- system-wide install (HKLM, requires elevation) -----

    private static ExitCode InstallSystem(Logger log)
    {
        if (!IsRunningAsAdmin())
        {
            return RelaunchAsAdmin("--install --system", log);
        }

        try
        {
            var src = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine the current executable path.");

            Directory.CreateDirectory(AppConstants.SystemInstallDir);
            var dest = AppConstants.SystemExePath;
            if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(src, dest, overwrite: true);
                log.Info($"Copied executable to {dest}");
            }

            RegisterProgId(Registry.LocalMachine, HklmClassesRoot, dest, log);
            RegisterExtensions(Registry.LocalMachine, HklmClassesRoot, log);
            WriteUninstallEntry(Registry.LocalMachine, HklmUninstallSubKey, dest, AppConstants.SystemInstallDir, log);
            CreateShortcut(SystemStartMenuLinkPath, dest, log);
            NotifyShell();

            log.Info("System install complete.");
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            log.Error($"System install failed: {ex}");
            Ui.ShowError("System installation failed." + Environment.NewLine + Environment.NewLine + ex.Message);
            return ExitCode.InstallFailed;
        }
    }

    // ----- per-user uninstall (HKCU) -----

    private static ExitCode UninstallUser(Logger log)
    {
        try
        {
            using (var classes = Registry.CurrentUser.OpenSubKey(ClassesRoot, writable: true))
            {
                if (classes != null)
                {
                    RemoveProgIdAndExtensions(classes, log);
                }
            }

            Registry.CurrentUser.DeleteSubKeyTree(UninstallSubKey, throwOnMissingSubKey: false);

            // Best-effort: remove Start-Menu shortcut.
            // NOTE: config.ini is deliberately left in place so reinstalls keep aliases.
            RemoveShortcut(UserStartMenuLinkPath, log);

            NotifyShell();
            ScheduleSelfDelete(AppConstants.InstallDir, log);

            log.Info("User uninstall complete.");
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            log.Error($"User uninstall failed: {ex}");
            Ui.ShowError("Uninstall failed." + Environment.NewLine + Environment.NewLine + ex.Message);
            return ExitCode.UninstallFailed;
        }
    }

    // ----- system-wide uninstall (HKLM) -----

    private static ExitCode UninstallSystem(Logger log)
    {
        if (!IsRunningAsAdmin())
        {
            return RelaunchAsAdmin("--uninstall", log);
        }

        try
        {
            using (var classes = Registry.LocalMachine.OpenSubKey(HklmClassesRoot, writable: true))
            {
                if (classes != null)
                {
                    RemoveProgIdAndExtensions(classes, log);
                }
            }

            Registry.LocalMachine.DeleteSubKeyTree(HklmUninstallSubKey, throwOnMissingSubKey: false);
            RemoveShortcut(SystemStartMenuLinkPath, log);

            NotifyShell();
            ScheduleSelfDelete(AppConstants.SystemInstallDir, log);

            log.Info("System uninstall complete.");
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            log.Error($"System uninstall failed: {ex}");
            Ui.ShowError("System uninstall failed." + Environment.NewLine + Environment.NewLine + ex.Message);
            return ExitCode.UninstallFailed;
        }
    }

    // ----- shared registry helpers -----

    private static void RegisterProgId(RegistryKey hive, string classesPath, string exePath, Logger log)
    {
        using var progid = hive.CreateSubKey($@"{classesPath}\{AppConstants.ProgId}");
        progid.SetValue(null, AppConstants.ProgIdDescription);

        using (var icon = progid.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue(null, ResolveDefaultIcon(exePath));
        }

        using (var cmd = progid.CreateSubKey(@"shell\open\command"))
        {
            cmd.SetValue(null, $"\"{exePath}\" \"%1\"");
        }

        log.Info("Registered ProgID.");
    }

    private static void RegisterExtensions(RegistryKey hive, string classesPath, Logger log)
    {
        foreach (var ext in AppConstants.AllExtensions)
        {
            using var extKey = hive.CreateSubKey($@"{classesPath}\{ext}");
            extKey.SetValue(null, AppConstants.ProgId);

            using var owp = extKey.CreateSubKey("OpenWithProgids");
            owp.SetValue(AppConstants.ProgId, string.Empty, RegistryValueKind.String);
        }

        log.Info($"Registered {AppConstants.AllExtensions.Count} file extensions.");
    }

    private static void RemoveProgIdAndExtensions(RegistryKey classesKey, Logger log)
    {
        classesKey.DeleteSubKeyTree(AppConstants.ProgId, throwOnMissingSubKey: false);

        foreach (var ext in AppConstants.AllExtensions)
        {
            using (var owp = classesKey.OpenSubKey($@"{ext}\OpenWithProgids", writable: true))
            {
                owp?.DeleteValue(AppConstants.ProgId, throwOnMissingValue: false);
            }

            using var extKey = classesKey.OpenSubKey(ext, writable: true);
            if (extKey?.GetValue(null) as string == AppConstants.ProgId)
            {
                extKey.DeleteValue(string.Empty, throwOnMissingValue: false);
            }
        }

        log.Info("Removed ProgID and extensions.");
    }

    private static void WriteUninstallEntry(RegistryKey hive, string subKeyPath, string exePath, string installDir, Logger log)
    {
        using var key = hive.CreateSubKey(subKeyPath);
        key.SetValue("DisplayName", AppConstants.DisplayName);
        key.SetValue("DisplayVersion", AppConstants.Version);
        key.SetValue("Publisher", AppConstants.Publisher);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", exePath + ",0");
        key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));

        try
        {
            var sizeKb = (int)(new FileInfo(exePath).Length / 1024);
            key.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
        }
        catch
        {
            // EstimatedSize is optional.
        }

        log.Info("Wrote uninstall entry.");
    }

    /// <summary>
    /// Prefer the locally-installed Google Drive icon (referenced by path, never
    /// copied into our directory); otherwise use our own embedded icon.
    /// </summary>
    private static string ResolveDefaultIcon(string exePath)
    {
        var drive = FindGoogleDriveExe();
        return drive != null ? $"{drive},0" : $"{exePath},0";
    }

    private static string? FindGoogleDriveExe()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            var dfsDir = Path.Combine(root, "Google", "Drive File Stream");
            if (!Directory.Exists(dfsDir))
            {
                continue;
            }

            try
            {
                // GoogleDriveFS.exe lives under a versioned subfolder.
                var exe = Directory
                    .EnumerateFiles(dfsDir, "GoogleDriveFS.exe", SearchOption.AllDirectories)
                    .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (exe != null)
                {
                    return exe;
                }
            }
            catch
            {
                // Permission/enumeration issue: fall back to our own icon.
            }
        }

        return null;
    }

    // ----- shortcut helpers -----

    private static void CreateShortcut(string lnkPath, string exePath, Logger log)
    {
        try
        {
            var lnkDir = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(lnkDir))
            {
                Directory.CreateDirectory(lnkDir);
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                log.Warn("WScript.Shell not available; skipping Start-Menu shortcut.");
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath = exePath;
            shortcut.Description = AppConstants.ProgIdDescription;
            shortcut.IconLocation = exePath + ",0";
            shortcut.Save();

            log.Info($"Created Start-Menu shortcut: {lnkPath}");
        }
        catch (Exception ex)
        {
            // Never fail the install because of a shortcut issue.
            log.Warn($"Could not create Start-Menu shortcut: {ex.Message}");
        }
    }

    private static void RemoveShortcut(string lnkPath, Logger log)
    {
        try
        {
            if (File.Exists(lnkPath))
            {
                File.Delete(lnkPath);
                log.Info($"Removed Start-Menu shortcut: {lnkPath}");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Could not remove Start-Menu shortcut: {ex.Message}");
        }
    }

    // ----- elevation helpers -----

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static ExitCode RelaunchAsAdmin(string args, Logger log)
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine the current executable path.");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                Verb = "runas",
                UseShellExecute = true,
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit();
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            log.Error($"UAC elevation failed: {ex.Message}");
            Ui.ShowError("Administrator privileges required for system-wide install." +
                Environment.NewLine + Environment.NewLine + ex.Message);
            return ExitCode.InstallFailed;
        }
    }

    private static void NotifyShell() =>
        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    private static void ScheduleSelfDelete(string installDir, Logger log)
    {
        // A running exe cannot delete itself; spawn a detached cmd to remove the
        // install directory a couple of seconds after we exit.
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{installDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process.Start(psi);
            log.Info("Scheduled install-directory cleanup.");
        }
        catch (Exception ex)
        {
            log.Warn($"Could not schedule cleanup: {ex.Message}");
        }
    }
}
