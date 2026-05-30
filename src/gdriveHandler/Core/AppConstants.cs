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
/// URL templates, install locations, and writable app data paths.
/// </summary>
internal static class AppConstants
{
    public const string AppId = "gdriveHandler";
    public const string DisplayName = "gdriveHandler";
    public const string Publisher = "devSpaceOB";
    public const string ProgId = "devSpaceOB.gdriveHandler";
    public const string ProgIdDescription = "Google Workspace Shortcut";
    public const string Version = "1.2.1";
    public const string AppIconAsset = @"Assets\App.ico";

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

    public static string ProgIdForExtension(string extension) =>
        $"{ProgId}.{extension.TrimStart('.').ToLowerInvariant()}";

    public static string DescriptionForExtension(string extension) =>
        ExtensionDescriptions.TryGetValue(extension, out var description)
            ? description
            : ProgIdDescription;

    public static string IconAssetForExtension(string extension) =>
        ExtensionIconAssets.TryGetValue(extension, out var icon)
            ? icon
            : AppIconAsset;

    private static readonly IReadOnlyDictionary<string, string> ExtensionDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".gdoc"] = "Google Docs Shortcut",
            [".gsheet"] = "Google Sheets Shortcut",
            [".gslides"] = "Google Slides Shortcut",
            [".gform"] = "Google Forms Shortcut",
            [".gsite"] = "Google Sites Shortcut",
            [".glink"] = "Google Drive Shortcut",
        };

    private static readonly IReadOnlyDictionary<string, string> ExtensionIconAssets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".gdoc"] = @"Assets\FileIcons\Docs.ico",
            [".gsheet"] = @"Assets\FileIcons\Sheets.ico",
            [".gslides"] = @"Assets\FileIcons\Slides.ico",
            [".gform"] = @"Assets\FileIcons\Forms.ico",
            [".gsite"] = @"Assets\FileIcons\Sites.ico",
            [".glink"] = @"Assets\FileIcons\Drive.ico",
        };

    // ----- Path helpers -----

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string ProgramFilesDir =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static bool IsPackaged
    {
        get
        {
            try
            {
                _ = Windows.ApplicationModel.Package.Current.Id.FullName;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // User-level legacy install path for the unpackaged folder/zip channel.
    public static string InstallDir => Path.Combine(LocalAppData, "Programs", AppId);
    public static string InstalledExePath => Path.Combine(InstallDir, AppId + ".exe");

    /// <summary>Writable app data lives outside the install directory for MSIX and zip installs.</summary>
    public static string DataDir => Path.Combine(LocalAppData, AppId);

    public static string ConfigFile => Path.Combine(DataDir, "config.ini");

    public static string LogDir => Path.Combine(DataDir, "logs");

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
