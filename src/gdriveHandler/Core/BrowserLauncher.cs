using System.Diagnostics;

namespace GdriveHandler;

/// <summary>
/// Launches the URL. All arguments are passed via <see cref="ProcessStartInfo.ArgumentList"/>
/// so each is escaped independently by the runtime — a crafted URL cannot inject
/// additional browser command-line flags.
/// </summary>
internal static class BrowserLauncher
{
    /// <summary>
    /// Launch the URL in the exact matched profile.
    /// <paramref name="openInNewWindow"/> overrides the compiled-in default when
    /// a user preference is available; pass <see cref="AppConstants.OpenInNewWindow"/>
    /// when no preference is loaded.
    /// </summary>
    public static bool LaunchMatched(ProfileMatch match, string url, Logger log,
        bool openInNewWindow = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = match.Browser.ExePath,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add($"--user-data-dir={match.Browser.UserDataDir}");
        psi.ArgumentList.Add($"--profile-directory={match.ProfileDir}");
        psi.ArgumentList.Add("--no-first-run");
        psi.ArgumentList.Add("--no-default-browser-check");
        if (openInNewWindow)
        {
            psi.ArgumentList.Add("--new-window");
        }
        psi.ArgumentList.Add(url);

        try
        {
            log.Info($"Launching {match.Browser.Channel}/{match.ProfileDir} for {match.MatchedEmail}");
            log.Info("Args: " + string.Join(" ", psi.ArgumentList));
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Matched launch failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fallback when no profile matched: open the URL in Chrome's currently
    /// active / last-active profile (Chrome routes it automatically). If no
    /// Chrome is installed, hand it to the system default browser.
    /// </summary>
    public static bool LaunchFallback(string? chromeExe, string url, Logger log)
    {
        if (!string.IsNullOrEmpty(chromeExe))
        {
            var psi = new ProcessStartInfo
            {
                FileName = chromeExe,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("--no-first-run");
            psi.ArgumentList.Add("--no-default-browser-check");
            psi.ArgumentList.Add(url);

            try
            {
                log.Warn($"No profile match; opening in Chrome's active profile: {chromeExe}");
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"Chrome fallback failed, trying system default browser: {ex.Message}");
            }
        }

        // System default browser.
        try
        {
            log.Warn("Opening in system default browser.");
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Default browser launch failed: {ex.Message}");
            return false;
        }
    }
}
