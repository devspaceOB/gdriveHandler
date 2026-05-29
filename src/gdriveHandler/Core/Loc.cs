using Microsoft.Windows.ApplicationModel.Resources;

namespace GdriveHandler;

/// <summary>
/// Thin wrapper around <see cref="ResourceLoader"/> for code-behind string lookups.
/// Works for unpackaged, self-contained WinAppSDK apps: the build produces
/// <c>gdriveHandler.pri</c> (named after the assembly) in the exe directory, and
/// MRT Core resolves strings from that file at runtime.
///
/// For unpackaged apps <c>new ResourceLoader()</c> with no arguments resolves against
/// the process's resource map using the PRI file in the same directory as the executable.
/// We pass the explicit PRI path to be safe across all invocation contexts.
///
/// Usage:
/// <code>
///   string label = Loc.Get("NavHome");
/// </code>
/// </summary>
internal static class Loc
{
    private static ResourceLoader? _loader;

    private static ResourceLoader Loader
    {
        get
        {
            if (_loader != null) return _loader;

            try
            {
                // For unpackaged WinAppSDK apps, construct the ResourceLoader with
                // the path to the PRI file so it works regardless of working directory.
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath)
                             ?? AppDomain.CurrentDomain.BaseDirectory;
                var priPath = Path.Combine(exeDir, "gdriveHandler.pri");

                _loader = File.Exists(priPath)
                    ? new ResourceLoader(priPath, "Resources")
                    : new ResourceLoader();
            }
            catch
            {
                // Fallback: try the default constructor (works when resources are
                // registered under the process's resource map).
                try { _loader = new ResourceLoader(); }
                catch { _loader = null!; }
            }

            return _loader!;
        }
    }

    /// <summary>
    /// Returns the localized string for <paramref name="resourceKey"/>.
    /// Returns <paramref name="resourceKey"/> itself when the resource is not found,
    /// so the UI degrades gracefully rather than throwing.
    /// </summary>
    /// <summary>
    /// Drops the cached loader so the next lookup picks up a changed
    /// <c>PrimaryLanguageOverride</c> (used by the in-place language switch).
    /// </summary>
    internal static void Reset() => _loader = null;

    public static string Get(string resourceKey)
    {
        try
        {
            var value = Loader?.GetString(resourceKey);
            return string.IsNullOrEmpty(value) ? resourceKey : value;
        }
        catch
        {
            return resourceKey;
        }
    }
}
