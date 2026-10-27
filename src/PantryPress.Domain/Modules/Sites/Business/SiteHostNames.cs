using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>
/// The stored form of a host name (SITE-005; PP2.9): what a site's host is written as, and what a request's Host is
/// looked up as. Only the shape, the same one the schema's check holds: which hosts a site may take - its own subdomain,
/// never another zone of the platform's - is for the code that adds one (PP2.10, PP17.2).
/// </summary>
public static class SiteHostNames
{
    /// <summary>
    /// The stored form of <paramref name="value"/> - ASCII lower-case with one trailing dot dropped - when it is a host
    /// name of two labels or more. Refused: anything but ASCII letters, digits, dots, and hyphens, checked before anything
    /// is lower-cased so no look-alike letter becomes a real one; a port, which the caller splits off first; an empty
    /// label or one longer than 63; a hyphen at either end of a label; over 253 characters; and a last label that does not
    /// start with a letter. Every top-level domain does, and an IPv4 literal in any of its spellings - decimal, octal, or
    /// hex, such as <c>127.0.0.0x1</c> - does not.
    /// </summary>
    public static bool TryNormalize(string? value, out string host)
    {
        host = string.Empty;
        if (value is null)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '.'))
            {
                return false;
            }
        }

        // The fully qualified spelling names the same host; only one dot is that spelling.
        var name = value.EndsWith('.') ? value[..^1] : value;
        if (name.Length > SiteLimits.MaxHostLength)
        {
            return false;
        }

        var labels = name.Split('.');
        if (labels.Length < 2 || !labels.All(IsLabel) || !char.IsAsciiLetter(labels[^1][0]))
        {
            return false;
        }

        host = name.ToLowerInvariant();
        return true;
    }

    private static bool IsLabel(string label) =>
        label.Length is > 0 and <= SiteLimits.MaxHostLabelLength && label[0] != '-' && label[^1] != '-';
}
