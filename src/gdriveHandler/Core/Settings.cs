namespace GdriveHandler;

/// <summary>
/// User-editable settings loaded from <see cref="AppConstants.ConfigFile"/>.
/// Stored as a hand-written INI file so the format is human-readable and requires
/// no third-party dependency. The file survives reinstalls intentionally.
///
/// Format:
/// <code>
/// [settings]
/// openInNewWindow=false
/// includeEdge=true
///
/// [aliases]
/// old.name@gmail.com=user@domain.fyi
/// </code>
///
/// Parsing is exposed via pure static methods (<see cref="Parse"/> /
/// <see cref="ToIni"/>) for unit-testability; <see cref="Load"/> / <see cref="Save"/>
/// are thin wrappers that do the actual file I/O.
/// </summary>
internal sealed class Settings
{
    // ------------------------------------------------------------------ defaults

    /// <summary>Open the matched browser in a new window. Default: false (new tab).</summary>
    public bool OpenInNewWindow { get; set; } = AppConstants.OpenInNewWindow;

    /// <summary>Include Edge profiles in the authoritative match step. Default: true.</summary>
    public bool IncludeEdge { get; set; } = true;

    /// <summary>
    /// UI language preference. Accepted values: "en" (English) and "tr" (Türkçe).
    /// Default: "en".
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// When true, the Settings page shows the Advanced and Logs subtabs, and the
    /// Home page shows advanced management actions. Default: false.
    /// </summary>
    public bool AdvancedMode { get; set; } = false;

    /// <summary>
    /// Email-to-email alias map (OrdinalIgnoreCase). Keys are "old" addresses that
    /// may appear in shortcut files; values are the current address stored in a
    /// browser profile.
    /// </summary>
    public Dictionary<string, string> Aliases { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ file I/O

    /// <summary>
    /// Load settings from <see cref="AppConstants.ConfigFile"/>. Returns defaults
    /// when the file is absent or unreadable.
    /// </summary>
    public static Settings Load()
    {
        try
        {
            var path = AppConstants.ConfigFile;
            if (File.Exists(path))
            {
                return Parse(File.ReadAllText(path));
            }
        }
        catch
        {
            // Unreadable config → silently return defaults.
        }

        return new Settings();
    }

    /// <summary>Write settings to <see cref="AppConstants.ConfigFile"/>.</summary>
    public static void Save(Settings settings)
    {
        var dir = Path.GetDirectoryName(AppConstants.ConfigFile);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(AppConstants.ConfigFile, ToIni(settings));
    }

    // ------------------------------------------------------------------ pure INI parsing

    /// <summary>
    /// Parse an INI text into a <see cref="Settings"/> instance. Pure; no file I/O.
    /// Supports <c>[settings]</c> and <c>[aliases]</c> sections (case-insensitive).
    /// Blank lines and lines starting with <c>;</c> or <c>#</c> are ignored.
    /// </summary>
    public static Settings Parse(string iniText)
    {
        var result = new Settings();
        var section = "";

        foreach (var rawLine in iniText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            var eqIdx = line.IndexOf('=', StringComparison.Ordinal);
            if (eqIdx < 0)
            {
                continue;
            }

            var key = line[..eqIdx].Trim();
            var value = line[(eqIdx + 1)..].Trim();

            if (section == "settings")
            {
                switch (key.ToLowerInvariant())
                {
                    case "openinnewwindow":
                        result.OpenInNewWindow = ParseBool(value, AppConstants.OpenInNewWindow);
                        break;
                    case "includeedge":
                        result.IncludeEdge = ParseBool(value, true);
                        break;
                    case "language":
                        // Accept only known codes; unknown values silently fall back to "en".
                        result.Language = value.Equals("tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
                        break;
                    case "advancedmode":
                        result.AdvancedMode = ParseBool(value, false);
                        break;
                }
            }
            else if (section == "aliases")
            {
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    result.Aliases[key] = value;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Serialize a <see cref="Settings"/> instance to INI text. Pure; no file I/O.
    /// </summary>
    public static string ToIni(Settings settings)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[settings]");
        sb.AppendLine($"openInNewWindow={settings.OpenInNewWindow.ToString().ToLowerInvariant()}");
        sb.AppendLine($"includeEdge={settings.IncludeEdge.ToString().ToLowerInvariant()}");
        sb.AppendLine($"language={settings.Language}");
        sb.AppendLine($"advancedMode={settings.AdvancedMode.ToString().ToLowerInvariant()}");

        sb.AppendLine();
        sb.AppendLine("[aliases]");
        foreach (var kv in settings.Aliases.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"{kv.Key}={kv.Value}");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------ alias resolution

    /// <summary>
    /// Returns the alias target for <paramref name="email"/> (case-insensitive), or
    /// <c>null</c> if no alias is configured. Single-hop only.
    /// </summary>
    public string? ResolveAlias(string email) =>
        Aliases.TryGetValue(email.Trim(), out var target) ? target : null;

    // ------------------------------------------------------------------ helpers

    private static bool ParseBool(string value, bool fallback)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.Ordinal) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("0", StringComparison.Ordinal) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fallback;
    }
}
