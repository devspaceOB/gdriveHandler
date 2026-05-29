using System.Text.Json;
using System.Text.RegularExpressions;

namespace GdriveHandler;

/// <summary>
/// Extracts the email, URL, doc id, and resource key from a shortcut file.
/// Pure and side-effect free so it can be unit tested without a filesystem.
/// Strategy: parse as JSON (the modern format) and read known keys
/// case-insensitively; fall back to regex over the raw text when JSON parsing
/// fails or a key is missing.
/// </summary>
internal static partial class ShortcutParser
{
    private static readonly string[] EmailKeys =
        { "email", "user_email", "account_email", "owner_email", "last_email" };

    private static readonly string[] UrlKeys =
        { "url", "doc_url", "open_url", "web_url", "alternateLink", "webViewLink" };

    private static readonly string[] DocIdKeys =
        { "doc_id", "docId", "id", "file_id", "fileId", "document_id", "resource_id", "resourceId" };

    private static readonly string[] ResourceKeyKeys =
        { "resource_key", "resourceKey", "target_resource_key", "targetResourceKey" };

    /// <param name="extension">Lower-cased extension including the dot (e.g. ".gsheet").</param>
    /// <param name="content">Raw file text.</param>
    public static ShortcutInfo Parse(string extension, string content)
    {
        JsonElement? root = TryParseJson(content);

        string? email = FirstString(root, EmailKeys);
        if (string.IsNullOrWhiteSpace(email))
        {
            var match = EmailRegex().Match(content);
            email = match.Success ? match.Value.Trim() : null;
        }

        string? url = FirstString(root, UrlKeys);
        if (string.IsNullOrWhiteSpace(url))
        {
            var match = UrlRegex().Match(content);
            url = match.Success ? match.Value.Trim() : null;
        }

        string? docId = FirstString(root, DocIdKeys);
        if (string.IsNullOrWhiteSpace(docId))
        {
            var match = DocIdJsonRegex().Match(content);
            if (match.Success)
            {
                docId = match.Groups[1].Value;
            }
        }

        string? resourceKey = FirstString(root, ResourceKeyKeys);
        if (string.IsNullOrWhiteSpace(resourceKey) && !string.IsNullOrWhiteSpace(url))
        {
            var match = ResourceKeyRegex().Match(url);
            if (match.Success)
            {
                resourceKey = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        return new ShortcutInfo(extension, Trim(email), Trim(url), Trim(docId), Trim(resourceKey));
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JsonElement? TryParseJson(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            // Clone so the element survives disposal of the document.
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the first non-empty string value among <paramref name="keys"/>
    /// (case-insensitive), honoring key priority order. Accepts string values
    /// and arrays of strings.
    /// </summary>
    private static string? FirstString(JsonElement? root, string[] keys)
    {
        if (root is not { ValueKind: JsonValueKind.Object } obj)
        {
            return null;
        }

        foreach (var key in keys)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (!string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = ValueToString(prop.Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static string? ValueToString(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.Number:
                return value.GetRawText();
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    var s = ValueToString(item);
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        return s;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"https?://[^\s""'<>]+")]
    private static partial Regex UrlRegex();

    [GeneratedRegex("\"doc_id\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex DocIdJsonRegex();

    [GeneratedRegex(@"[?&]resourcekey=([^&#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceKeyRegex();
}
