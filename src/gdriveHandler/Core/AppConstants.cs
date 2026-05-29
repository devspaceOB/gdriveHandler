namespace GdriveHandler;

/// <summary>
/// Process exit codes. Kept stable so scripts/diagnostics can rely on them.
/// </summary>
internal enum ExitCode
{
    Success = 0,
    GeneralError = 1,
    InvalidArguments = 2,
    FileNotFound = 3,
    UnsupportedExtension = 4,
    ParseFailed = 5,
    EmailNotFound = 6,        // reserved; email-less shortcuts now fall back rather than fail
    UrlOrDocIdNotFound = 7,
    BrowserOpenFailed = 8,    // even the fallback could not open the URL
    InstallFailed = 10,
    UninstallFailed = 11,
    Unhandled = 99,
}

/// <summary>
/// Central, dependency-free configuration: identity, supported extensions,
/// URL templates, and per-user install/log locations.
/// </summary>
internal static class AppConstants
{
    public const string AppId = "gdriveHandler";
    public const string DisplayName = "gdriveHandler";
    public const string Publisher = "devSpaceOB";
    public const string ProgId = "devSpaceOB.gdriveHandler";
    public const string ProgIdDescription = "Google Workspace Shortcut";
    public const string Version = "1.0.0";

    /// <summary>Open a new browser window instead of a new tab. Default: new tab.</summary>
    public static readonly bool OpenInNewWindow = false;

    /// <summary>Extensions with a deterministic Google URL template ({0} = doc id).</summary>
    public static readonly IReadOnlyDictionary<string, string> UrlTemplates =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".gdoc"] = "https://docs.google.com/document/d/{0}/edit",
            [".gsheet"] = "https://docs.google.com/spreadsheets/d/{0}/edit",
            [".gslides"] = "https://docs.google.com/presentation/d/{0}/edit",
            [".gdraw"] = "https://docs.google.com/drawings/d/{0}/edit",
            [".gform"] = "https://docs.google.com/forms/d/{0}/edit",
            [".gscript"] = "https://script.google.com/d/{0}/edit",
        };

    /// <summary>Extensions supported only when the file already carries a usable URL.</summary>
    public static readonly IReadOnlyList<string> UrlOnlyExtensions =
        new[] { ".gmap", ".glink", ".gsite", ".gtable", ".gjam" };

    /// <summary>Every extension we register a handler for.</summary>
    public static IReadOnlyList<string> AllExtensions { get; } =
        UrlTemplates.Keys.Concat(UrlOnlyExtensions).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsSupported(string extension) =>
        UrlTemplates.ContainsKey(extension) ||
        UrlOnlyExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    // ----- Path helpers -----

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string ProgramFilesDir =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    // User-level install: exe, config, and logs all under one folder tree
    public static string InstallDir => Path.Combine(LocalAppData, "Programs", AppId);
    public static string InstalledExePath => Path.Combine(InstallDir, AppId + ".exe");

    /// <summary>config.ini lives in the same folder as the installed exe.</summary>
    public static string ConfigFile => Path.Combine(InstallDir, "config.ini");

    /// <summary>Log files are in a subfolder of the install directory.</summary>
    public static string LogDir => Path.Combine(InstallDir, "logs");

    public static string LogFile => Path.Combine(LogDir, "launcher.log");

    // System-wide install paths (used when --install --system is passed)
    public static string SystemInstallDir => Path.Combine(ProgramFilesDir, AppId);
    public static string SystemExePath => Path.Combine(SystemInstallDir, AppId + ".exe");

    /// <summary>
    /// True when the running exe is located under %ProgramFiles% — indicates a
    /// system-wide install rather than a per-user install.
    /// </summary>
    public static bool IsSystemInstall
    {
        get
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

            return exePath.StartsWith(ProgramFilesDir, StringComparison.OrdinalIgnoreCase);
        }
    }
}
