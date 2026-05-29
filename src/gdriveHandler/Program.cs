using System.Text;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using Microsoft.UI.Dispatching;
using WinRT;

namespace GdriveHandler;

internal static class Program
{
    private static bool _consoleReady;

    [STAThread]
    private static int Main(string[] args)
    {
        var log = new Logger();
        try
        {
            log.Info("---- start ---- args: " + string.Join(" ", args));
            var code = Run(args, log);
            log.Info($"---- exit {(int)code} ({code}) ----");
            return (int)code;
        }
        catch (Exception ex)
        {
            log.Error("Unhandled: " + ex);
            Ui.ShowError("Unexpected error." + Environment.NewLine + Environment.NewLine + ex.Message);
            return (int)ExitCode.Unhandled;
        }
    }

    private static ExitCode Run(string[] args, Logger log)
    {
        // No args → open the WinUI GUI (Home page).
        if (args.Length == 0)
        {
            return LaunchGui(initialPage: "home");
        }

        var first = args[0];
        if (first.StartsWith("--", StringComparison.Ordinal) || first.StartsWith('/'))
        {
            // Accept and ignore a trailing value form like "--scope=user".
            var mode = first.TrimStart('/').TrimStart('-').Split('=')[0].ToLowerInvariant();
            var hasSystem = args.Any(a => a.Equals("--system", StringComparison.OrdinalIgnoreCase));
            switch (mode)
            {
                case "install":
                    return Installer.Install(log, systemWide: hasSystem);
                case "uninstall":
                    return Installer.Uninstall(log);
                case "repair":
                    return Installer.Repair(log);
                case "diagnose":
                    return Diagnose(args.Length > 1 ? args[1] : null, log);
                case "settings":
                    return LaunchGui(initialPage: "settings");
                case "help":
                case "?":
                    ShowHelp();
                    return ExitCode.Success;
                default:
                    ShowHelp();
                    return ExitCode.InvalidArguments;
            }
        }

        return HandleFile(first, log);
    }

    // ----- GUI launcher (no-args / --settings) -----

    private static ExitCode LaunchGui(string initialPage)
    {
        // Initialize WinAppSDK bootstrap for unpackaged apps.
        // Only reached on the GUI path — the file-handling hot path never gets here.
        Bootstrap.Initialize(0x00020000); // WinAppSDK 2.0

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

    // ----- normal handler path -----

    private static ExitCode HandleFile(string path, Logger log)
    {
        if (!File.Exists(path))
        {
            return Fail(log, ExitCode.FileNotFound, $"File not found:{Environment.NewLine}{path}");
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!AppConstants.IsSupported(ext))
        {
            var supported = string.Join(", ", AppConstants.AllExtensions.OrderBy(e => e, StringComparer.Ordinal));
            return Fail(log, ExitCode.UnsupportedExtension,
                $"Unsupported file type '{ext}'.{Environment.NewLine}{Environment.NewLine}Supported: {supported}");
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return Fail(log, ExitCode.ParseFailed, "Could not read the shortcut file." + Environment.NewLine + Environment.NewLine + ex.Message);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Fail(log, ExitCode.ParseFailed, "The shortcut file is empty.");
        }

        var info = ShortcutParser.Parse(ext, content);
        var finalUrl = UrlBuilder.BuildFinalUrl(info);
        if (string.IsNullOrWhiteSpace(finalUrl))
        {
            return Fail(log, ExitCode.UrlOrDocIdNotFound,
                "Could not determine a URL from the shortcut file (no usable url or doc id).");
        }

        log.Info($"email={info.Email ?? "(none)"} finalUrl={finalUrl}");

        // Load settings once for this file-handling operation.
        var settings = Settings.Load();
        log.Info($"settings: openInNewWindow={settings.OpenInNewWindow} includeEdge={settings.IncludeEdge} aliases={settings.Aliases.Count}");

        var allCandidates = BrowserDiscovery.Discover(log);

        // Respect IncludeEdge for the authoritative match step only (Chrome
        // fallback still considers all candidates below).
        var matchCandidates = settings.IncludeEdge
            ? allCandidates
            : allCandidates.Where(c => !string.Equals(c.Family, "Edge", StringComparison.OrdinalIgnoreCase)).ToList();

        ProfileMatch? match = null;
        string? aliasTarget = null;

        if (!string.IsNullOrWhiteSpace(info.Email))
        {
            // (1) Direct authoritative match.
            match = ProfileMatcher.FindBest(matchCandidates, info.Email!, log);

            if (match != null)
            {
                log.Info($"Match (direct): {match.Browser.Channel}/{match.ProfileDir} for {match.MatchedEmail}");
            }
            else
            {
                // (2) Alias resolution: if a mapping exists, retry with the target email.
                aliasTarget = settings.ResolveAlias(info.Email!);
                if (aliasTarget != null)
                {
                    log.Info($"No direct match; trying alias {info.Email} -> {aliasTarget}");
                    match = ProfileMatcher.FindBest(matchCandidates, aliasTarget, log);
                    if (match != null)
                    {
                        log.Info($"Match (via alias {info.Email} -> {aliasTarget}): {match.Browser.Channel}/{match.ProfileDir}");
                    }
                }

                if (match == null)
                {
                    log.Warn($"No profile match for {info.Email}" +
                        (aliasTarget != null ? $" (alias target: {aliasTarget})" : "") +
                        "; falling back.");
                }
            }
        }
        else
        {
            log.Warn("No email in shortcut; using active-profile fallback.");
        }

        if (match != null && BrowserLauncher.LaunchMatched(match, finalUrl, log, settings.OpenInNewWindow))
        {
            return ExitCode.Success;
        }

        // (3) Fallback: Chrome's active/last profile, else system default browser.
        var chromeExe = BrowserDiscovery.BestChromeExe(allCandidates);
        if (BrowserLauncher.LaunchFallback(chromeExe, finalUrl, log))
        {
            return ExitCode.Success;
        }

        return Fail(log, ExitCode.BrowserOpenFailed, "Could not open the URL in any browser.");
    }

    private static ExitCode Fail(Logger log, ExitCode code, string message)
    {
        log.Error($"[{(int)code}] {message}");
        Ui.ShowError(message);
        return code;
    }

    // ----- diagnose / help -----

    private static ExitCode Diagnose(string? file, Logger log)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppConstants.DisplayName} {AppConstants.Version}  (publisher: {AppConstants.Publisher})");
        sb.AppendLine($"Installed exe : {AppConstants.InstalledExePath}  (exists: {File.Exists(AppConstants.InstalledExePath)})");
        sb.AppendLine($"Log file      : {AppConstants.LogFile}");
        sb.AppendLine($"Config file   : {AppConstants.ConfigFile}  (exists: {File.Exists(AppConstants.ConfigFile)})");
        sb.AppendLine($"Extensions    : {string.Join(", ", AppConstants.AllExtensions.OrderBy(e => e, StringComparer.Ordinal))}");
        sb.AppendLine();

        // Show loaded settings.
        var settings = Settings.Load();
        sb.AppendLine($"Settings:");
        sb.AppendLine($"  openInNewWindow : {settings.OpenInNewWindow}");
        sb.AppendLine($"  includeEdge     : {settings.IncludeEdge}");
        sb.AppendLine($"  aliases         : {settings.Aliases.Count}");
        foreach (var kv in settings.Aliases.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"    {kv.Key} -> {kv.Value}");
        }
        sb.AppendLine();

        var candidates = BrowserDiscovery.Discover(log);
        sb.AppendLine($"Browsers found: {candidates.Count}");
        foreach (var c in candidates.OrderBy(c => c.Priority))
        {
            sb.AppendLine($"  [{c.Priority,3}] {c.Channel,-20} {c.ExePath}");
        }
        sb.AppendLine();

        sb.AppendLine("Profiles and signed-in identities:");
        foreach (var id in ProfileMatcher.EnumerateProfiles(candidates, log))
        {
            var gaiaStr = !string.IsNullOrEmpty(id.GaiaId) ? $"  gaia:{id.GaiaId}" : "";
            sb.AppendLine($"  {id.Browser.Channel,-20} {id.ProfileDir,-12} {id.UserName ?? "(not signed in)"}{gaiaStr}");
        }
        sb.AppendLine();

        var resultCode = ExitCode.Success;
        if (!string.IsNullOrWhiteSpace(file))
        {
            sb.AppendLine($"Parsing: {file}");
            if (!File.Exists(file))
            {
                sb.AppendLine("  (file not found)");
                resultCode = ExitCode.FileNotFound;
            }
            else
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var info = ShortcutParser.Parse(ext, File.ReadAllText(file));
                sb.AppendLine($"  email       : {info.Email ?? "(none)"}");
                sb.AppendLine($"  url         : {info.Url ?? "(none)"}");
                sb.AppendLine($"  docId       : {info.DocId ?? "(none)"}");
                sb.AppendLine($"  resourceKey : {info.ResourceKey ?? "(none)"}");
                sb.AppendLine($"  final URL   : {UrlBuilder.BuildFinalUrl(info) ?? "(could not build)"}");

                if (!string.IsNullOrWhiteSpace(info.Email))
                {
                    // Respect IncludeEdge for the match step.
                    var matchCandidates = settings.IncludeEdge
                        ? candidates
                        : candidates.Where(c => !string.Equals(c.Family, "Edge", StringComparison.OrdinalIgnoreCase)).ToList();

                    var directMatch = ProfileMatcher.FindBest(matchCandidates, info.Email!, log);
                    if (directMatch != null)
                    {
                        sb.AppendLine($"  match       : {directMatch.Browser.Channel}/{directMatch.ProfileDir}");
                    }
                    else
                    {
                        sb.AppendLine($"  match       : (none - would use fallback)");

                        // Show alias-resolved match if available.
                        var aliasTarget = settings.ResolveAlias(info.Email!);
                        if (aliasTarget != null)
                        {
                            var aliasMatch = ProfileMatcher.FindBest(matchCandidates, aliasTarget, log);
                            if (aliasMatch != null)
                            {
                                sb.AppendLine($"  match (via alias {info.Email} -> {aliasTarget}): {aliasMatch.Browser.Channel}/{aliasMatch.ProfileDir}");
                            }
                            else
                            {
                                sb.AppendLine($"  match (via alias {info.Email} -> {aliasTarget}): (none - would use fallback)");
                            }
                        }
                    }
                }
            }
        }

        Output("gdriveHandler diagnostics", sb.ToString());
        return resultCode;
    }

    private static void ShowHelp()
    {
        var text =
            $"{AppConstants.DisplayName} {AppConstants.Version}" + Environment.NewLine + Environment.NewLine +
            "Opens Google Workspace shortcut files (.gdoc, .gsheet, ...) in the Chrome/Edge" + Environment.NewLine +
            "profile signed in with the account stored inside the file." + Environment.NewLine + Environment.NewLine +
            "Usage:" + Environment.NewLine +
            "  gdriveHandler <file>                  Open a shortcut file (normal handler use)" + Environment.NewLine +
            "  gdriveHandler --install               Install for the current user (no admin)" + Environment.NewLine +
            "  gdriveHandler --install --system      Install for all users (admin / UAC)" + Environment.NewLine +
            "  gdriveHandler --uninstall             Remove associations and uninstall" + Environment.NewLine +
            "  gdriveHandler --repair                Re-register associations" + Environment.NewLine +
            "  gdriveHandler --diagnose [file]       List browsers/profiles (and optionally parse a file)" + Environment.NewLine +
            "  gdriveHandler --settings              Open the settings GUI" + Environment.NewLine +
            "  gdriveHandler --help                  Show this help" + Environment.NewLine + Environment.NewLine +
            "  (no args)                             Open the settings/info GUI" + Environment.NewLine + Environment.NewLine +
            "Config: " + AppConstants.ConfigFile + Environment.NewLine +
            "Logs:   " + AppConstants.LogFile;

        Output("gdriveHandler help", text);
    }

    /// <summary>
    /// Writes text to the parent console when launched from a terminal; otherwise
    /// (e.g. double-clicked) shows it in an information dialog.
    /// </summary>
    private static void Output(string title, string text)
    {
        EnsureConsole();
        if (_consoleReady)
        {
            Console.WriteLine(text);
        }
        else
        {
            Ui.ShowInfo(title, text);
        }
    }

    private static void EnsureConsole()
    {
        if (_consoleReady)
        {
            return;
        }

        if (NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS))
        {
            try
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(stdout);
            }
            catch
            {
                // Keep going; worst case Console.WriteLine is a no-op.
            }

            _consoleReady = true;
        }
    }
}
