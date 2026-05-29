using System.Text.RegularExpressions;

namespace GdriveHandler;

/// <summary>
/// Builds the final browser URL from parsed shortcut fields. Pure; unit tested.
/// Enforces an http/https scheme on the result so the launcher can never be
/// coerced into handing a non-web URI to the browser.
/// </summary>
internal static partial class UrlBuilder
{
    /// <summary>
    /// Produces the URL to open, or null if neither a usable URL nor a doc id
    /// is available. Appends the resource key when present and not already set.
    /// </summary>
    public static string? BuildFinalUrl(ShortcutInfo info)
    {
        string? finalUrl = null;

        if (IsAbsoluteWebUrl(info.Url))
        {
            finalUrl = info.Url!.Trim();
        }
        else
        {
            var docId = NormalizeDocId(info.DocId) ?? NormalizeDocId(info.Url);
            if (!string.IsNullOrWhiteSpace(docId))
            {
                finalUrl = AppConstants.UrlTemplates.TryGetValue(info.Extension, out var template)
                    ? string.Format(template, docId)
                    : $"https://drive.google.com/open?id={docId}";
            }
        }

        if (string.IsNullOrWhiteSpace(finalUrl) || !IsAbsoluteWebUrl(finalUrl))
        {
            return null;
        }

        finalUrl = AppendResourceKey(finalUrl, info.ResourceKey);
        return finalUrl;
    }

    /// <summary>True for absolute http/https URLs only.</summary>
    public static bool IsAbsoluteWebUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Extracts a Google document id from common encodings: bare id,
    /// "type:id", ".../d/id...", and "...?id=id...".
    /// </summary>
    public static string? NormalizeDocId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var v = value.Trim();

        foreach (var regex in new[] { PathIdRegex(), QueryIdRegex(), PrefixedIdRegex(), BareIdRegex() })
        {
            var match = regex.Match(v);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static string AppendResourceKey(string url, string? resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey) || HasResourceKeyRegex().IsMatch(url))
        {
            return url;
        }

        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + "resourcekey=" + Uri.EscapeDataString(resourceKey.Trim());
    }

    [GeneratedRegex(@"/d/([A-Za-z0-9_-]{10,})")]
    private static partial Regex PathIdRegex();

    [GeneratedRegex(@"[?&]id=([A-Za-z0-9_-]{10,})")]
    private static partial Regex QueryIdRegex();

    [GeneratedRegex(@"^[A-Za-z]+:([A-Za-z0-9_-]{10,})$")]
    private static partial Regex PrefixedIdRegex();

    [GeneratedRegex(@"^([A-Za-z0-9_-]{10,})$")]
    private static partial Regex BareIdRegex();

    [GeneratedRegex(@"[?&]resourcekey=", RegexOptions.IgnoreCase)]
    private static partial Regex HasResourceKeyRegex();
}
