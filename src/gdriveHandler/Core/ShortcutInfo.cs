namespace GdriveHandler;

/// <summary>
/// Raw fields extracted from a Google Workspace shortcut file. URL construction
/// is the responsibility of <see cref="UrlBuilder"/>, keeping parsing pure.
/// </summary>
internal sealed record ShortcutInfo(
    string Extension,
    string? Email,
    string? Url,
    string? DocId,
    string? ResourceKey);
