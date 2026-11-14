namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// The hosts a recipe's video may be on (RCP-001; content.md: allow-listed video embeds; owner decision, PP4.11): YouTube
/// and Vimeo. Platform reference data held as code, like the conversion lists: versioned, site-free, never taken from a
/// request. A change is a list edit and a version bump; a recipe already saved keeps its address. PP5's video block and the
/// per-site content-security policy (MON-005) read the same list when they arrive.
/// </summary>
public static class RecipeVideoHosts
{
    public const int Version = 1;

    private const string Https = "https://";

    /// <summary>Each host exactly, lower-case; a subdomain not listed is not allowed.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "youtu.be",
        "vimeo.com",
        "www.vimeo.com",
        "player.vimeo.com",
    ];

    /// <summary>
    /// Whether <paramref name="url"/> is a video address a recipe may hold: printable ASCII only; <c>https://</c> spelled
    /// exactly so, as the table checks; an absolute address with no user name or password and no port of its own; and a
    /// host on the list, compared exactly once lower-cased. Anything that would read one way here and another way in a
    /// browser (a backslash, white space, an encoded host) is refused rather than guessed at.
    /// </summary>
    public static bool IsAllowed(string url)
    {
        if (!url.StartsWith(Https, StringComparison.Ordinal) || url.Length <= Https.Length)
        {
            return false;
        }

        foreach (var c in url)
        {
            if (c is < '!' or > '~' or '\\')
            {
                return false;
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0
            || !uri.IsDefaultPort
            || uri.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        // The host as written, between the scheme and the first '/', '?', or '#', so no normalizing parser decides it.
        var rest = url.AsSpan(Https.Length);
        var end = rest.IndexOfAny('/', '?', '#');
        var host = (end < 0 ? rest : rest[..end]).ToString();
        return All.Contains(host.ToLowerInvariant(), StringComparer.Ordinal)
            && string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase);
    }
}
