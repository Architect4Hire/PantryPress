using System.Net;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>
/// Reads a public request's <c>Host</c> header as the host to look up (ARCH-003, PSEC-004; PP2.12): the port split off,
/// IP literals refused, and the rest in <see cref="SiteHostNames"/>' stored form. What this refuses is never looked up,
/// so a Host that cannot name a site costs neither a cache read nor a query.
/// </summary>
public static class PublicHostNames
{
    private const int MaxPort = 65535;

    /// <summary>
    /// The stored-form host <paramref name="hostHeader"/> names. Refused: an empty header; an IPv6 literal, bracketed
    /// or not; a port that is not 1 to 65535 in plain digits; an IPv4 literal in any spelling, the parser's or the
    /// last-label rule's (<c>127.0.0.0x1</c>); and anything <see cref="SiteHostNames.TryNormalize"/> refuses.
    /// </summary>
    public static bool TryRead(string? hostHeader, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrEmpty(hostHeader) || hostHeader[0] == '[')
        {
            return false;
        }

        var name = hostHeader;
        var colon = hostHeader.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            // One colon is a port; more is an IPv6 address without its brackets, or nothing at all.
            if (hostHeader.IndexOf(':', colon + 1) >= 0 || !IsPort(hostHeader.AsSpan(colon + 1)))
            {
                return false;
            }

            name = hostHeader[..colon];
        }

        // The parser takes the shorthand spellings too (127.1, 2130706433, 0x7f.1); none of them is a site's.
        if (IPAddress.TryParse(name.EndsWith('.') ? name[..^1] : name, out _))
        {
            return false;
        }

        return SiteHostNames.TryNormalize(name, out host);
    }

    // Plain digits with no leading zero, 1 to 65535: what a browser sends, and nothing that parses to it another way.
    private static bool IsPort(ReadOnlySpan<char> port)
    {
        if (port.Length is 0 or > 5 || port[0] == '0')
        {
            return false;
        }

        var value = 0;
        foreach (var c in port)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            value = (value * 10) + (c - '0');
        }

        return value <= MaxPort;
    }
}
