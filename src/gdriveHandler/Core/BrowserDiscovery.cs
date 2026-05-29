using Microsoft.Win32;

namespace GdriveHandler;

/// <summary>A discovered, installed Chromium browser channel.</summary>
internal sealed record BrowserCandidate(
    string Family,        // "Chrome" or "Edge"
    string Channel,       // e.g. "Chrome Stable"
    string ExePath,
    string UserDataDir,
    int Priority);        // lower = preferred

/// <summary>
/// Enumerates installed Chrome and Edge channels and their User Data
/// directories. Chrome families rank ahead of Edge; within a family, Stable
/// ahead of Beta/Dev/Canary/Testing.
/// </summary>
internal static class BrowserDiscovery
{
    private static string Pf => Environment.GetEnvironmentVariable("ProgramFiles") ?? "";
    private static string Pf86 => Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "";
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private sealed record ChannelSpec(
        string Family,
        string Channel,
        string RelExe,        // relative path under a program-files / local root
        string UserDataRel,   // relative path under LOCALAPPDATA
        string AppPathsExe,   // App Paths registry key name, or "" if none
        int Priority);

    private static readonly ChannelSpec[] Specs =
    {
        // Chrome
        new("Chrome", "Chrome Stable",      @"Google\Chrome\Application\chrome.exe",             @"Google\Chrome\User Data",             "chrome.exe", 10),
        new("Chrome", "Chrome Beta",        @"Google\Chrome Beta\Application\chrome.exe",        @"Google\Chrome Beta\User Data",        "",           20),
        new("Chrome", "Chrome Dev",         @"Google\Chrome Dev\Application\chrome.exe",         @"Google\Chrome Dev\User Data",         "",           30),
        new("Chrome", "Chrome Canary",      @"Google\Chrome SxS\Application\chrome.exe",         @"Google\Chrome SxS\User Data",         "",           40),
        new("Chrome", "Chrome for Testing", @"Google\Chrome for Testing\chrome.exe",             @"Google\Chrome for Testing\User Data", "",           50),
        // Edge
        new("Edge",   "Edge Stable",        @"Microsoft\Edge\Application\msedge.exe",            @"Microsoft\Edge\User Data",            "msedge.exe", 110),
        new("Edge",   "Edge Beta",          @"Microsoft\Edge Beta\Application\msedge.exe",       @"Microsoft\Edge Beta\User Data",       "",           120),
        new("Edge",   "Edge Dev",           @"Microsoft\Edge Dev\Application\msedge.exe",        @"Microsoft\Edge Dev\User Data",        "",           130),
        new("Edge",   "Edge Canary",        @"Microsoft\Edge SxS\Application\msedge.exe",        @"Microsoft\Edge SxS\User Data",        "",           140),
    };

    public static IReadOnlyList<BrowserCandidate> Discover(Logger? log = null)
    {
        var results = new List<BrowserCandidate>();

        foreach (var spec in Specs)
        {
            var userData = Path.Combine(Local, spec.UserDataRel);
            var exe = ResolveExe(spec);
            if (exe is null)
            {
                continue;
            }

            // De-dupe on (exe, userData).
            if (results.Any(c =>
                    string.Equals(c.ExePath, exe, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.UserDataDir, userData, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            results.Add(new BrowserCandidate(spec.Family, spec.Channel, exe, userData, spec.Priority));
            log?.Info($"Browser found: {spec.Channel} -> {exe}");
        }

        return results;
    }

    /// <summary>The preferred installed Chrome executable, for the fallback launch.</summary>
    public static string? BestChromeExe(IEnumerable<BrowserCandidate> candidates) =>
        candidates
            .Where(c => string.Equals(c.Family, "Chrome", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Priority)
            .Select(c => c.ExePath)
            .FirstOrDefault();

    private static string? ResolveExe(ChannelSpec spec)
    {
        foreach (var candidate in ExeCandidates(spec))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> ExeCandidates(ChannelSpec spec)
    {
        // Registry App Paths first (authoritative for the stable channel).
        if (!string.IsNullOrEmpty(spec.AppPathsExe))
        {
            foreach (var p in AppPaths(spec.AppPathsExe))
            {
                yield return p;
            }
        }

        if (!string.IsNullOrEmpty(Pf)) yield return Path.Combine(Pf, spec.RelExe);
        if (!string.IsNullOrEmpty(Pf86)) yield return Path.Combine(Pf86, spec.RelExe);
        yield return Path.Combine(Local, spec.RelExe);
    }

    private static IEnumerable<string> AppPaths(string exeName)
    {
        var subKey = $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exeName}";
        var sources = new (RegistryKey Root, string Path)[]
        {
            (Registry.CurrentUser, subKey),
            (Registry.LocalMachine, subKey),
            (Registry.LocalMachine, $@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\{exeName}"),
        };

        foreach (var (root, path) in sources)
        {
            string? value = null;
            try
            {
                using var key = root.OpenSubKey(path);
                value = key?.GetValue(null) as string;
            }
            catch (Exception)
            {
                // Inaccessible hive; ignore.
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value.Trim('"');
            }
        }
    }
}
