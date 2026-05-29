using System.Text.Json;

namespace GdriveHandler;

/// <summary>A profile whose authoritative signed-in identity matches the target email.</summary>
internal sealed record ProfileMatch(BrowserCandidate Browser, string ProfileDir, string MatchedEmail);

/// <summary>
/// The signed-in identity recorded for one browser profile.
/// <see cref="GaiaId"/> is the stable Google account identifier stored in
/// <c>profile.info_cache[&lt;profile&gt;].gaia_id</c>; it is non-null when the
/// profile is signed in (even after a Gmail→Workspace address change the
/// gaia_id stays the same).
/// </summary>
internal sealed record ProfileIdentity(BrowserCandidate Browser, string ProfileDir, string? UserName, string? GaiaId = null);

/// <summary>
/// Resolves which browser profile is signed in with a given Google account by
/// reading the authoritative <c>profile.info_cache[&lt;profile&gt;].user_name</c>
/// field from each browser's <c>Local State</c>. No substring scanning of
/// Preferences, so an email that merely appears in a shared doc or contact can
/// never cause a false-positive match.
/// </summary>
internal static class ProfileMatcher
{
    /// <summary>
    /// Best matching profile for <paramref name="targetEmail"/>, or null if none.
    /// Ranks by browser priority, then prefers the "Default" profile.
    /// </summary>
    public static ProfileMatch? FindBest(
        IEnumerable<BrowserCandidate> candidates, string targetEmail, Logger? log = null)
    {
        var matches = new List<ProfileMatch>();

        foreach (var identity in EnumerateProfiles(candidates, log))
        {
            if (!string.IsNullOrWhiteSpace(identity.UserName) &&
                string.Equals(identity.UserName.Trim(), targetEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(new ProfileMatch(identity.Browser, identity.ProfileDir, identity.UserName.Trim()));
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }

        var best = matches
            .OrderBy(m => m.Browser.Priority)
            .ThenBy(m => m.ProfileDir.Equals("Default", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.ProfileDir, StringComparer.OrdinalIgnoreCase)
            .First();

        if (matches.Count > 1)
        {
            var all = string.Join("; ", matches.Select(m => $"{m.Browser.Channel}/{m.ProfileDir}"));
            log?.Warn($"Multiple matches for {targetEmail}: {all}. Chose {best.Browser.Channel}/{best.ProfileDir}.");
        }

        return best;
    }

    /// <summary>
    /// Every profile across all candidates with its recorded signed-in identity
    /// (null when the profile is not signed in). Used by both matching and
    /// <c>--diagnose</c>.
    /// </summary>
    public static IReadOnlyList<ProfileIdentity> EnumerateProfiles(
        IEnumerable<BrowserCandidate> candidates, Logger? log = null)
    {
        var output = new List<ProfileIdentity>();

        foreach (var candidate in candidates)
        {
            var localStatePath = Path.Combine(candidate.UserDataDir, "Local State");
            if (!File.Exists(localStatePath))
            {
                log?.Warn($"No Local State for {candidate.Channel}: {localStatePath}");
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
                if (!doc.RootElement.TryGetProperty("profile", out var profile) ||
                    !profile.TryGetProperty("info_cache", out var infoCache) ||
                    infoCache.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var profileProp in infoCache.EnumerateObject())
                {
                    var name = profileProp.Name;
                    if (name.Equals("System Profile", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? userName = null;
                    string? gaiaId = null;

                    if (profileProp.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (profileProp.Value.TryGetProperty("user_name", out var u) &&
                            u.ValueKind == JsonValueKind.String)
                        {
                            userName = u.GetString();
                        }

                        if (profileProp.Value.TryGetProperty("gaia_id", out var g) &&
                            g.ValueKind == JsonValueKind.String)
                        {
                            gaiaId = g.GetString();
                        }
                    }

                    output.Add(new ProfileIdentity(candidate, name, userName, gaiaId));
                }
            }
            catch (Exception ex)
            {
                // Locked / malformed / unreadable Local State: log and continue.
                log?.Warn($"Could not read Local State for {candidate.Channel}: {ex.Message}");
            }
        }

        return output;
    }
}
