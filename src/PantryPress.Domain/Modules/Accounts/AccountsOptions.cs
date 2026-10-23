using System.Net;

namespace PantryPress.Domain.Modules.Accounts;

/// <summary>Settings for the Accounts module, from the <c>Accounts</c> configuration section.</summary>
public sealed class AccountsOptions
{
    /// <summary>
    /// The admin app's public origin, used for links in account emails (<c>https://app.pantrypress.app</c>; the gateway
    /// at <c>https://app.localhost:…</c> in local runs). Set by the AppHost; never derived from a request's Host header,
    /// so a forged Host cannot point a reset link somewhere else.
    /// </summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    /// <summary>An absolute origin with no path, query, or fragment; plain HTTP only on this machine.</summary>
    public bool IsValid() =>
        Uri.TryCreate(AppBaseUrl, UriKind.Absolute, out var uri)
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
