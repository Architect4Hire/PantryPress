using System.Net;

namespace PantryPress.Domain.Managers.Email;

/// <summary>
/// The admin app's origin, as links in email are written against it: account email (PP1.2) and site invitations (PP2.11).
/// Set by the AppHost, never derived from a request's Host header, so a forged Host cannot point a link somewhere else.
/// </summary>
public static class EmailLinkOrigin
{
    /// <summary>An absolute origin with no path, query, or fragment; plain HTTP only on this machine.</summary>
    public static bool IsValid(string? origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri)))
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback
        || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
}
